namespace VectorSharp.Reranking
{
    /// <summary>
    /// What a provider reported spending on a rerank call. Only ever present when the provider
    /// actually reported it: a provider that meters nothing produces no usage at all rather than a
    /// usage record full of zeroes.
    /// </summary>
    /// <remarks>
    /// Reads the same as <c>VectorSharp.Embedding.EmbeddingUsage</c> today, and is deliberately a
    /// separate type rather than a shared one: this package takes no dependencies, including on
    /// its sibling packages, so a caller can rerank without pulling in an embedding service it is
    /// not using. The resemblance is a convenience for anyone reading both, not a contract —
    /// what a rerank API meters is its own business, and a field added to one of the two does not
    /// oblige the other.
    /// </remarks>
    public sealed class RerankUsage
    {
        /// <summary>
        /// Gets the number of tokens the provider reported for the call.
        /// </summary>
        /// <remarks>
        /// Required rather than nullable, so that a <see cref="RerankUsage"/> cannot be built
        /// without a real count. "Nothing was reported" is expressed by the absence of the whole
        /// record, which keeps it distinguishable from a call that genuinely cost zero tokens.
        /// </remarks>
        public required int TokenCount { get; init; }

        /// <summary>
        /// Gets the model identifier the provider attributed the call to, or null when the
        /// provider does not name one.
        /// </summary>
        public string? Model { get; init; }
    }
}
