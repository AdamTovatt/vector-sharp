namespace VectorSharp.Embedding.Tests
{
    // The convention for this project: an IEmbeddingProvider double is a top-level type in a file
    // of its own, never nested inside the test class that happens to use it first. Doubles get
    // reused across files as soon as a second test needs the same behaviour, and one nested in a
    // test class is invisible from the next file — which is how this project ended up with two
    // hand-rolled recorders doing the same job. TestEmbeddingProvider and BatchRecordingProvider
    // are the two big enough to warrant their own files; the small ones live here together.

    /// <summary>
    /// Releases every waiter once enough calls have arrived, counting arrivals cumulatively
    /// rather than tracking how many are inside at once — a call that has already returned
    /// still counts. That is all the tests using it need, because a serialised path never
    /// produces a second arrival while the first is held.
    /// </summary>
    internal sealed class ArrivalLatch
    {
        private readonly int _arrivalsRequired;
        private readonly TaskCompletionSource _reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public ArrivalLatch(int arrivalsRequired)
        {
            _arrivalsRequired = arrivalsRequired;
        }

        public Task ArriveAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrivals) >= _arrivalsRequired)
                _reached.TrySetResult();

            return _reached.Task;
        }
    }

    /// <summary>
    /// A provider that only implements the single-text method, standing in for a local model.
    /// </summary>
    internal sealed class LatchedSingleTextProvider : IEmbeddingProvider
    {
        private readonly ArrivalLatch _latch;

        public int Dimension => 8;

        public LatchedSingleTextProvider(ArrivalLatch latch)
        {
            _latch = latch;
        }

        public async Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            // Bounded and cancellable on purpose. If the calls are ever serialised the latch
            // is never reached, and a wait that could not be broken out of would hang the
            // service's disposal — and with it the whole test run — instead of failing.
            await _latch.ArriveAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return new float[8];
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Reports whether the token the service handed it was the one the caller cancelled.
    /// </summary>
    internal sealed class ObservedCancellationProvider : IEmbeddingProvider
    {
        public int Dimension => 8;

        public bool SupportsBatching => true;

        public TaskCompletionSource CallStarted { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SawCancellation { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new float[8]);
        }

        public async Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            CallStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SawCancellation.TrySetResult(true);
                throw;
            }

            throw new InvalidOperationException("Unreachable: the delay never completes on its own.");
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Fails a call by cancelling a token of its own, which no one outside it holds.
    /// </summary>
    /// <remarks>
    /// The case a library provider produces without meaning to: an HTTP client with its own
    /// per-request timeout raises <see cref="OperationCanceledException"/> from that timeout, not
    /// from the token it was handed. It is a failed call, and the service must not report it to
    /// the caller as a cancellation the caller asked for.
    /// </remarks>
    internal sealed class SelfCancellingProvider : IEmbeddingProvider
    {
        internal const string FailureMessage = "the provider's own token timed out";

        public int Dimension => 8;

        public bool SupportsBatching => true;

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new float[8]);
        }

        public Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource ownSource = new CancellationTokenSource();
            ownSource.Cancel();

            throw new OperationCanceledException(FailureMessage, ownSource.Token);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Batches but meters nothing: overrides only the plain batch method and inherits the usage
    /// one. Records through the shared <see cref="BatchRecorder"/> rather than keeping its own
    /// tally, so there is one implementation of "what did the providers receive".
    /// </summary>
    internal sealed class BatchingOnlyProvider : IEmbeddingProvider
    {
        private readonly BatchRecorder _recorder;

        public int Dimension => 8;

        public bool SupportsBatching => true;

        public BatchingOnlyProvider(BatchRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            _recorder.RecordSingleCall();
            return Task.FromResult(new float[8]);
        }

        public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            _recorder.RecordBatch(texts, purpose);

            float[][] vectors = new float[texts.Count][];
            for (int i = 0; i < texts.Count; i++)
            {
                vectors[i] = new float[8];
            }

            return Task.FromResult(vectors);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A provider that drops a vector, the way a remote API would if it silently skipped an input.
    /// </summary>
    internal sealed class ShortResponseProvider : IEmbeddingProvider
    {
        public int Dimension => 8;

        public bool SupportsBatching => true;

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new float[8]);
        }

        public Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            float[][] vectors = new float[Math.Max(0, texts.Count - 1)][];
            for (int i = 0; i < vectors.Length; i++)
            {
                vectors[i] = new float[8];
            }

            return Task.FromResult(new EmbeddingResult { Vectors = vectors });
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Throws from every call, for the paths that check a provider's failure reaches the caller.
    /// </summary>
    internal sealed class FailingProvider : IEmbeddingProvider
    {
        public int Dimension => 768;

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Intentional test failure");
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A count shared across the provider instances the service builds, one per worker.
    /// </summary>
    internal sealed class SharedCounter
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }

    /// <summary>
    /// Counts every call into <see cref="SharedCounter"/>, so a test can see the total across all
    /// of the service's workers.
    /// </summary>
    internal sealed class CountingProvider : IEmbeddingProvider
    {
        private readonly SharedCounter _counter;

        public int Dimension { get; }

        public CountingProvider(int dimension, SharedCounter counter)
        {
            Dimension = dimension;
            _counter = counter;
        }

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            _counter.Increment();
            return Task.FromResult(new float[Dimension]);
        }

        public void Dispose() { }
    }
}
