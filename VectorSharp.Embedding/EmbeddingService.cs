using System.Threading.Channels;

namespace VectorSharp.Embedding
{
    /// <summary>
    /// A channel-based embedding service that manages a pool of workers for concurrent
    /// embedding production. Requests are submitted through the public API and dispatched
    /// to available workers via a bounded channel.
    /// </summary>
    public sealed class EmbeddingService : IAsyncDisposable
    {
        private readonly Channel<EmbeddingRequest> _channel;
        private readonly Task[] _workerTasks;
        private readonly IEmbeddingProvider[] _providers;
        private readonly CancellationTokenSource _cts;
        private readonly BatchingPolicy _batchingPolicy;
        private int _disposed;

        /// <summary>
        /// Gets the dimensionality of the embedding vectors produced by this service.
        /// </summary>
        public int Dimension { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddingService"/> class.
        /// Creates the specified number of provider instances and starts worker tasks immediately.
        /// </summary>
        /// <param name="providerFactory">A factory that creates <see cref="IEmbeddingProvider"/> instances.
        /// Called once per worker. Each worker owns its own provider instance.</param>
        /// <param name="options">Configuration options. If null, defaults are used.</param>
        /// <exception cref="ArgumentNullException">Thrown when providerFactory is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when concurrency, channel capacity or either batch limit is less than 1.</exception>
        public EmbeddingService(Func<IEmbeddingProvider> providerFactory, EmbeddingServiceOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(providerFactory);

            EmbeddingServiceOptions effectiveOptions = options ?? new EmbeddingServiceOptions();

            if (effectiveOptions.Concurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Concurrency must be at least 1.");

            if (effectiveOptions.ChannelCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "ChannelCapacity must be at least 1.");

            if (effectiveOptions.MaxTextsPerBatch < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxTextsPerBatch must be at least 1.");

            if (effectiveOptions.MaxCharactersPerBatch < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxCharactersPerBatch must be at least 1.");

            BoundedChannelOptions channelOptions = new BoundedChannelOptions(effectiveOptions.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = effectiveOptions.Concurrency == 1,
                SingleWriter = false
            };

            _channel = Channel.CreateBounded<EmbeddingRequest>(channelOptions);
            _cts = new CancellationTokenSource();
            _providers = new IEmbeddingProvider[effectiveOptions.Concurrency];
            _workerTasks = new Task[effectiveOptions.Concurrency];

            // Create provider instances. If the factory fails partway, the ones already built are
            // disposed here: the constructor never returns, so the caller has no service to
            // dispose and an ONNX session would keep its native memory with nothing referencing it.
            int created = 0;
            try
            {
                for (; created < effectiveOptions.Concurrency; created++)
                {
                    _providers[created] = providerFactory();
                }
            }
            catch
            {
                for (int i = 0; i < created; i++)
                {
                    try
                    {
                        _providers[i].Dispose();
                    }
                    catch
                    {
                        // The factory's failure is what the caller needs to see, so a provider
                        // failing to clean up must not replace it.
                    }
                }

                _cts.Dispose();
                throw;
            }

            Dimension = _providers[0].Dimension;

            // The capability is read from the first provider because the factory produces one kind
            // of provider, which Dimension above already assumes.
            _batchingPolicy = BatchingPolicy.For(effectiveOptions, _providers[0].SupportsBatching);

            // Start worker tasks
            for (int i = 0; i < effectiveOptions.Concurrency; i++)
            {
                int workerIndex = i;
                _workerTasks[i] = Task.Run(() => RunWorkerAsync(_providers[workerIndex], _cts.Token));
            }
        }

        /// <summary>
        /// Produces a vector embedding for the given text. The request is queued and
        /// processed by an available worker.
        /// </summary>
        /// <param name="text">The text to embed.</param>
        /// <param name="purpose">The intended purpose of the embedding.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A float array representing the embedding.</returns>
        /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        public async Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            EmbeddingResult result = await EmbedWithUsageAsync(text, purpose, cancellationToken).ConfigureAwait(false);
            return result.Vectors[0];
        }

        /// <summary>
        /// Produces a vector embedding for the given text, reporting what it cost if the provider
        /// meters that.
        /// </summary>
        /// <param name="text">The text to embed.</param>
        /// <param name="purpose">The intended purpose of the embedding.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A result holding the single embedding, and usage when the provider reports it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        public async Task<EmbeddingResult> EmbedWithUsageAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            ArgumentNullException.ThrowIfNull(text);

            return await SubmitBatchAsync([text], purpose, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Produces vector embeddings for multiple texts. The texts are split into batches
        /// according to the batching policy and each batch is queued as one request.
        /// </summary>
        /// <param name="texts">The texts to embed.</param>
        /// <param name="purpose">The intended purpose of the embeddings.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>An array of float arrays, one embedding per input text, in the same order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when texts is null, or when any element of it is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider returns a number of
        /// vectors that does not match the number of texts it was given, which leaves them
        /// unmatchable to their inputs.</exception>
        public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            EmbeddingResult result = await EmbedBatchWithUsageAsync(texts, purpose, cancellationToken).ConfigureAwait(false);
            return result.Vectors.ToArray();
        }

        /// <summary>
        /// Produces vector embeddings for multiple texts, reporting what they cost if the provider
        /// meters that. The texts are split into batches according to the batching policy and each
        /// batch is queued as one request.
        /// </summary>
        /// <param name="texts">The texts to embed.</param>
        /// <param name="purpose">The intended purpose of the embeddings.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A result holding one embedding per input text in the same order, and the
        /// combined usage when the provider reports it. The usage is null unless every batch the
        /// call was split into reported one.</returns>
        /// <exception cref="ArgumentNullException">Thrown when texts is null, or when any element of it is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider returns a number of
        /// vectors that does not match the number of texts it was given, which leaves them
        /// unmatchable to their inputs.</exception>
        public async Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            ArgumentNullException.ThrowIfNull(texts);

            if (texts.Count == 0)
                return new EmbeddingResult { Vectors = Array.Empty<float[]>(), Usage = null };

            // Checked up front rather than left to the provider: a null in the middle of a batch
            // would otherwise reach a provider that has no way to report which text was bad.
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i] == null)
                    throw new ArgumentNullException($"{nameof(texts)}[{i}]");
            }

            IReadOnlyList<IReadOnlyList<string>> batches = _batchingPolicy.Split(texts);

            Task<EmbeddingResult>[] batchTasks = new Task<EmbeddingResult>[batches.Count];
            for (int i = 0; i < batches.Count; i++)
            {
                batchTasks[i] = SubmitBatchAsync(batches[i], purpose, cancellationToken);
            }

            EmbeddingResult[] batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

            float[][] vectors = new float[texts.Count][];
            int nextVector = 0;
            foreach (EmbeddingResult batchResult in batchResults)
            {
                foreach (float[] vector in batchResult.Vectors)
                {
                    vectors[nextVector++] = vector;
                }
            }

            return new EmbeddingResult
            {
                Vectors = vectors,
                Usage = EmbeddingUsage.Combine(batchResults.Select(batchResult => batchResult.Usage))
            };
        }

        /// <summary>
        /// Queues one batch and awaits the worker that takes it.
        /// </summary>
        private async Task<EmbeddingResult> SubmitBatchAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken)
        {
            TaskCompletionSource<EmbeddingResult> tcs = new TaskCompletionSource<EmbeddingResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            }

            EmbeddingRequest request = new EmbeddingRequest
            {
                Texts = texts,
                Purpose = purpose,
                CallerToken = cancellationToken,
                CompletionSource = tcs
            };

            // One finally over both the write and the wait. Disposing only around the wait leaks
            // the registration whenever the write throws, which is the disposed-mid-queue path
            // below, and the registration outlives the call attached to the caller's token.
            try
            {
                try
                {
                    await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops all workers and disposes all provider instances.
        /// </summary>
        /// <remarks>
        /// Cancels the shutdown token and then waits for every worker to return, so a provider
        /// call already in flight decides how long this takes. A provider that observes the
        /// cancellation token it is handed makes disposal prompt; one that ignores it holds
        /// disposal open for a full round trip, which against a remote API can be seconds. There
        /// is deliberately no timeout: abandoning the wait would dispose a provider while a call
        /// was still running inside it, which is worse than waiting.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            // Signal no more items
            _channel.Writer.TryComplete();

            // Cancel workers
            await _cts.CancelAsync().ConfigureAwait(false);

            // Wait for workers to drain
            try
            {
                await Task.WhenAll(_workerTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            finally
            {
                // In a finally rather than after the catch, so that a worker failing in a way that
                // is not a cancellation still leaves the providers disposed and the queued callers
                // released. The constructor already goes to this trouble when the factory fails
                // partway; letting a native ONNX session leak on a different failure would give
                // that guarantee away. The worker's exception still propagates from here.
                ReleaseAbandonedRequests();

                foreach (IEmbeddingProvider provider in _providers)
                {
                    provider.Dispose();
                }

                _cts.Dispose();
            }
        }

        /// <summary>
        /// Fails every request still sitting in the channel once the workers have stopped.
        /// </summary>
        /// <remarks>
        /// Nothing will pick these up any more, and each one has a caller awaiting a completion
        /// source that nothing else will ever complete. Failing them here is what keeps disposal
        /// from leaving a caller waiting forever.
        /// </remarks>
        private void ReleaseAbandonedRequests()
        {
            while (_channel.Reader.TryRead(out EmbeddingRequest? abandoned))
            {
                abandoned.CompletionSource.TrySetException(new ObjectDisposedException(GetType().FullName));
            }
        }

        private async Task RunWorkerAsync(IEmbeddingProvider provider, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (EmbeddingRequest request in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    // The caller's token has to reach the provider, not just complete the caller's
                    // task: an abandoned batch is up to MaxTextsPerBatch texts still being billed
                    // by a remote API, with this worker held for the whole round trip. Linked with
                    // the shutdown token so either can stop the call. Skipped entirely when the
                    // caller passed a token that can never fire, which is the common case.
                    using CancellationTokenSource? linkedSource = request.CallerToken.CanBeCanceled
                        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.CallerToken)
                        : null;

                    CancellationToken callToken = linkedSource?.Token ?? cancellationToken;

                    try
                    {
                        // One channel item is one provider call, which is what makes a batch cost
                        // one request against a remote API rather than one per text.
                        EmbeddingResult result = await provider.EmbedBatchWithUsageAsync(request.Texts, request.Purpose, callToken).ConfigureAwait(false);

                        // Vectors are matched to texts by position all the way back to the caller,
                        // so a provider that returns a different number of them has already lost
                        // the mapping. Failing the request beats handing back misaligned vectors.
                        if (result.Vectors.Count != request.Texts.Count)
                        {
                            throw new InvalidOperationException(
                                $"{provider.GetType().Name} returned {result.Vectors.Count} vectors for {request.Texts.Count} texts.");
                        }

                        request.CompletionSource.TrySetResult(result);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        request.CompletionSource.TrySetCanceled(cancellationToken);
                    }
                    catch (OperationCanceledException) when (request.CallerToken.IsCancellationRequested)
                    {
                        request.CompletionSource.TrySetCanceled(request.CallerToken);
                    }
                    catch (Exception ex)
                    {
                        // Including an OperationCanceledException raised from a token of the
                        // provider's own, which is a failure of that call rather than a
                        // cancellation of this one.
                        request.CompletionSource.TrySetException(ex);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown
            }
        }
    }
}
