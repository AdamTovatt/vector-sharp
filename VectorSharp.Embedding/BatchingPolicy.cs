namespace VectorSharp.Embedding
{
    /// <summary>
    /// Decides how a caller's texts are divided into the batches the service sends to a provider.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="EmbeddingService"/> so that the splitting rule can be exercised on
    /// its own. Reached through the service it needs a service, a factory, a provider and a
    /// recording double to observe a partition, which buries the rule being checked.
    ///
    /// Internal rather than public: this is how the service behaves, not a knob a caller sets.
    /// Callers configure it through <see cref="EmbeddingServiceOptions"/>.
    /// </remarks>
    internal sealed class BatchingPolicy
    {
        /// <summary>
        /// The largest number of texts one batch may hold. This is the effective limit rather than
        /// the configured one: see <see cref="For"/>.
        /// </summary>
        internal int MaxTextsPerBatch { get; }

        /// <summary>
        /// The largest total number of characters one batch may hold, except where a single text
        /// exceeds it on its own.
        /// </summary>
        internal int MaxCharactersPerBatch { get; }

        internal BatchingPolicy(int maxTextsPerBatch, int maxCharactersPerBatch)
        {
            if (maxTextsPerBatch < 1)
                throw new ArgumentOutOfRangeException(nameof(maxTextsPerBatch), "MaxTextsPerBatch must be at least 1.");

            if (maxCharactersPerBatch < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCharactersPerBatch), "MaxCharactersPerBatch must be at least 1.");

            MaxTextsPerBatch = maxTextsPerBatch;
            MaxCharactersPerBatch = maxCharactersPerBatch;
        }

        /// <summary>
        /// Builds the policy the service will actually apply to a given provider.
        /// </summary>
        /// <remarks>
        /// A provider that answers false to <see cref="IEmbeddingProvider.SupportsBatching"/> gets
        /// one text per batch whatever the options say, because grouping for it would run the
        /// texts serially inside one worker instead of spreading them across the pool. See
        /// <see cref="IEmbeddingProvider.SupportsBatching"/> for the whole of that reasoning.
        /// </remarks>
        internal static BatchingPolicy For(EmbeddingServiceOptions options, bool providerSupportsBatching)
        {
            return new BatchingPolicy(
                providerSupportsBatching ? options.MaxTextsPerBatch : 1,
                options.MaxCharactersPerBatch);
        }

        /// <summary>
        /// Splits the texts into batches that respect both limits.
        /// </summary>
        /// <remarks>
        /// A single text over the character limit becomes a batch of its own rather than being
        /// dropped or split, since only the caller can decide how to shorten it. Order is
        /// preserved: the batches concatenated in order reproduce the input, which is what lets
        /// the service reassemble the vectors by position.
        /// </remarks>
        internal IReadOnlyList<IReadOnlyList<string>> Split(IReadOnlyList<string> texts)
        {
            List<IReadOnlyList<string>> batches = new List<IReadOnlyList<string>>();
            List<string> currentBatch = new List<string>();
            long currentCharacters = 0;

            foreach (string text in texts)
            {
                bool wouldExceedCount = currentBatch.Count >= MaxTextsPerBatch;
                bool wouldExceedCharacters = currentBatch.Count > 0 && currentCharacters + text.Length > MaxCharactersPerBatch;

                if (wouldExceedCount || wouldExceedCharacters)
                {
                    batches.Add(currentBatch);
                    currentBatch = new List<string>();
                    currentCharacters = 0;
                }

                currentBatch.Add(text);
                currentCharacters += text.Length;
            }

            if (currentBatch.Count > 0)
                batches.Add(currentBatch);

            return batches;
        }
    }
}
