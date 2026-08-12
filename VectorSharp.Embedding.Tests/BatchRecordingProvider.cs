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

        /// <param name="recorder">Collects what every instance of this provider received. Omit it
        /// in a test that does not read what arrived: the provider then records into one of its
        /// own, so the test's Arrange block holds only what the test is actually about. Pass one
        /// whenever the assertions look at the calls, since the service builds a provider per
        /// worker and only a shared recorder sees all of them.</param>
        /// <param name="usageForBatch">Produces the usage this provider reports for a batch, or
        /// null to report none. Null by default, which is the local-model case.</param>
        /// <param name="supportsBatching">Whether this provider tells the service it batches.</param>
        public BatchRecordingProvider(BatchRecorder? recorder = null, Func<IReadOnlyList<string>, EmbeddingUsage?>? usageForBatch = null, bool supportsBatching = true)
        {
            _recorder = recorder ?? new BatchRecorder();
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
        /// A vector that identifies the text it came from, so a test can tell whether vectors came
        /// back matched to the texts that produced them.
        /// </summary>
        /// <remarks>
        /// Every element is derived from the whole string, in order. An earlier version stored the
        /// length and a plain sum of the characters, which is equal for any two anagrams and for
        /// any two same-length strings whose characters happen to add up the same — so a test
        /// asserting that vectors came back in input order held only while its literals happened
        /// to avoid the collision, and adding one more text could silently stop it detecting
        /// misordering. <c>VectorFor_DistinguishesTextsAPlainLengthAndSumWouldNot</c> pins this.
        /// </remarks>
        public static float[] VectorFor(string text)
        {
            float[] vector = new float[VectorLength];

            // FNV-1a over the text, then one further round per element, so position matters and
            // every element depends on every character.
            uint hash = 2166136261;
            foreach (char character in text)
            {
                hash = (hash ^ character) * 16777619;
            }

            for (int i = 0; i < VectorLength; i++)
            {
                hash = (hash ^ (uint)i) * 16777619;
                vector[i] = hash % 65536;
            }

            return vector;
        }

        public void Dispose() { }
    }
}
