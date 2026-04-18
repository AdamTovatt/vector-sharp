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
        private bool _disposed;

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
        /// <exception cref="ArgumentOutOfRangeException">Thrown when concurrency or channel capacity is less than 1.</exception>
        public EmbeddingService(Func<IEmbeddingProvider> providerFactory, EmbeddingServiceOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(providerFactory);

            EmbeddingServiceOptions effectiveOptions = options ?? new EmbeddingServiceOptions();

            if (effectiveOptions.Concurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Concurrency must be at least 1.");

            if (effectiveOptions.ChannelCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "ChannelCapacity must be at least 1.");

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

            // Create provider instances
            for (int i = 0; i < effectiveOptions.Concurrency; i++)
            {
                _providers[i] = providerFactory();
            }

            Dimension = _providers[0].Dimension;

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
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(text);

            TaskCompletionSource<float[]> tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            }

            EmbeddingRequest request = new EmbeddingRequest { Text = text, Purpose = purpose, CompletionSource = tcs };
            await _channel.Writer.WriteAsync(request, cancellationToken);

            try
            {
                return await tcs.Task;
            }
            finally
            {
                await registration.DisposeAsync();
            }
        }

        /// <summary>
        /// Produces vector embeddings for multiple texts. Requests are queued and distributed
        /// across available workers.
        /// </summary>
        /// <param name="texts">The texts to embed.</param>
        /// <param name="purpose">The intended purpose of the embeddings.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>An array of float arrays, one embedding per input text, in the same order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when texts is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(texts);

            if (texts.Count == 0)
                return Array.Empty<float[]>();

            Task<float[]>[] tasks = new Task<float[]>[texts.Count];
            for (int i = 0; i < texts.Count; i++)
            {
                tasks[i] = EmbedAsync(texts[i], purpose, cancellationToken);
            }

            return await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Stops all workers and disposes all provider instances.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Signal no more items
            _channel.Writer.TryComplete();

            // Cancel workers
            await _cts.CancelAsync();

            // Wait for workers to drain
            try
            {
                await Task.WhenAll(_workerTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }

            // Dispose providers
            foreach (IEmbeddingProvider provider in _providers)
            {
                provider.Dispose();
            }

            _cts.Dispose();
        }

        private async Task RunWorkerAsync(IEmbeddingProvider provider, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (EmbeddingRequest request in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        float[] result = await provider.EmbedAsync(request.Text, request.Purpose, cancellationToken);
                        request.CompletionSource.TrySetResult(result);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        request.CompletionSource.TrySetCanceled(cancellationToken);
                    }
                    catch (Exception ex)
                    {
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
