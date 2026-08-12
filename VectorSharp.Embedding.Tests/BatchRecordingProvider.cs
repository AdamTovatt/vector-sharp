namespace VectorSharp.Embedding.Tests
{
    /// <summary>
    /// What the providers were handed, shared across every instance the factory creates.
    /// Held outside the provider because the service creates one provider per worker, so state a
    /// test needs to read across workers cannot live on the provider itself.
    /// </summary>
    internal sealed class BatchRecorder
    {
        private readonly object _lock = new object();
        private readonly List<RecordedCall> _calls = new List<RecordedCall>();
        private int _singleCallCount;

        /// <summary>
        /// The calls the providers received, in arrival order. A batch of N texts appears as one
        /// entry with N texts; N single calls appear as N entries of one.
        /// </summary>
        public IReadOnlyList<RecordedCall> Calls
        {
            get
            {
                lock (_lock)
                {
                    return _calls.ToList();
                }
            }
        }

        public IReadOnlyList<IReadOnlyList<string>> Batches
        {
            get
            {
                return Calls.Select(call => call.Texts).ToList();
            }
        }

        /// <summary>
        /// How many times the single-text method was called. Stays at zero while the service uses
        /// the batch path, which is what distinguishes real batching from a loop.
        /// </summary>
        public int SingleCallCount => Volatile.Read(ref _singleCallCount);

        public void RecordBatch(IReadOnlyList<string> texts, EmbeddingPurpose purpose)
        {
            lock (_lock)
            {
                _calls.Add(new RecordedCall { Texts = texts.ToList(), Purpose = purpose });
            }
        }

        public void RecordSingleCall()
        {
            Interlocked.Increment(ref _singleCallCount);
        }

        internal sealed class RecordedCall
        {
            public required IReadOnlyList<string> Texts { get; init; }

            public required EmbeddingPurpose Purpose { get; init; }
        }
    }

    /// <summary>
    /// A provider that embeds a whole batch in one call, the way a remote API provider does, and
    /// reports what it received to a shared <see cref="BatchRecorder"/>. Tests assert against what
    /// arrived here rather than against what the service set out to send.
    /// </summary>
    internal sealed class BatchRecordingProvider : IEmbeddingProvider
    {
        private readonly BatchRecorder _recorder;
        private readonly Func<IReadOnlyList<string>, EmbeddingUsage?>? _usageForBatch;

        /// <summary>
        /// Fixed rather than configurable, so it cannot disagree with the length
        /// <see cref="VectorFor"/> actually produces.
        /// </summary>
        public int Dimension => VectorLength;

        /// <summary>
        /// True by default, the way a provider backed by an API that takes an array of inputs
        /// answers. Set false to stand in for one that embeds a text at a time.
        /// </summary>
        public bool SupportsBatching { get; }

        private const int VectorLength = 8;

        /// <param name="recorder">Collects what every instance of this provider received.</param>
        /// <param name="usageForBatch">Produces the usage this provider reports for a batch, or
        /// null to report none. Null by default, which is the local-model case.</param>
        /// <param name="supportsBatching">Whether this provider tells the service it batches.</param>
        public BatchRecordingProvider(BatchRecorder recorder, Func<IReadOnlyList<string>, EmbeddingUsage?>? usageForBatch = null, bool supportsBatching = true)
        {
            _recorder = recorder;
            _usageForBatch = usageForBatch;
            SupportsBatching = supportsBatching;
        }

        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            _recorder.RecordSingleCall();
            return Task.FromResult(VectorFor(text));
        }

        public Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            _recorder.RecordBatch(texts, purpose);

            float[][] vectors = new float[texts.Count][];
            for (int i = 0; i < texts.Count; i++)
            {
                vectors[i] = VectorFor(texts[i]);
            }

            return Task.FromResult(new EmbeddingResult
            {
                Vectors = vectors,
                Usage = _usageForBatch?.Invoke(texts)
            });
        }

        /// <summary>
        /// A vector whose first element identifies the text it came from, so a test can tell
        /// whether vectors came back matched to the texts that produced them.
        /// </summary>
        public static float[] VectorFor(string text)
        {
            float[] vector = new float[VectorLength];
            vector[0] = text.Length;
            for (int i = 0; i < text.Length; i++)
            {
                vector[1] += text[i];
            }
            return vector;
        }

        public void Dispose() { }
    }
}
