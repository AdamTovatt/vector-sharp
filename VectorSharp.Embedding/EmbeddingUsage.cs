namespace VectorSharp.Embedding
{
    /// <summary>
    /// What a provider reported spending on a call. Only ever present when the provider actually
    /// reported it: a local model that meters nothing produces no usage at all rather than a
    /// usage record full of zeroes.
    /// </summary>
    public sealed class EmbeddingUsage
    {
        /// <summary>
        /// Gets the number of tokens the provider reported for the call.
        /// </summary>
        /// <remarks>
        /// Required rather than nullable, so that an <see cref="EmbeddingUsage"/> cannot be built
        /// without a real count. "Nothing was reported" is expressed by the absence of the whole
        /// record, which keeps it distinguishable from a call that genuinely cost zero tokens.
        /// </remarks>
        public required int TokenCount { get; init; }

        /// <summary>
        /// Gets the model identifier the provider attributed the call to, or null when the
        /// provider does not name one.
        /// </summary>
        public string? Model { get; init; }

        /// <summary>
        /// Combines the usage of the batches one call was split into.
        /// </summary>
        /// <remarks>
        /// Reports a total only when every batch reported one. A partial total would understate
        /// what the call actually cost, and under-reporting spend is worse than reporting nothing:
        /// null already means "unknown", which is what a partially reported call is. An empty
        /// sequence is the same case — there is nothing to report, and totalling nothing up would
        /// produce a reported zero, which means "this was free".
        ///
        /// Lives beside the type rather than inside <see cref="EmbeddingService"/> because what it
        /// encodes is this type's own "null means unknown, never free" rule, and because a rule
        /// reachable only through a worker pool is a rule that can only be tested through one.
        /// </remarks>
        internal static EmbeddingUsage? Combine(IEnumerable<EmbeddingUsage?> reported)
        {
            int totalTokens = 0;
            string? model = null;
            bool haveSeenABatch = false;
            bool modelsAgree = true;

            foreach (EmbeddingUsage? usage in reported)
            {
                if (usage == null)
                    return null;

                totalTokens += usage.TokenCount;

                // Tracked with its own flag rather than by testing model for null, which cannot
                // tell "no batch seen yet" from "this batch named no model" — and would then
                // attribute the whole call to a model only one of the batches reported.
                if (!haveSeenABatch)
                {
                    model = usage.Model;
                    haveSeenABatch = true;
                }
                else if (usage.Model != model)
                {
                    modelsAgree = false;
                }
            }

            if (!haveSeenABatch)
                return null;

            return new EmbeddingUsage
            {
                TokenCount = totalTokens,
                Model = modelsAgree ? model : null
            };
        }
    }
}
