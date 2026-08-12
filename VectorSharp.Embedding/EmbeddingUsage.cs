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
    }
}
