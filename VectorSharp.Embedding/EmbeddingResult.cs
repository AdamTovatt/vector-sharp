namespace VectorSharp.Embedding
{
    /// <summary>
    /// The vectors produced for a set of texts, together with what the provider reported spending
    /// on them if it reported anything.
    /// </summary>
    public sealed class EmbeddingResult
    {
        /// <summary>
        /// Gets the embeddings, one per input text, in the order the texts were given.
        /// </summary>
        public required IReadOnlyList<float[]> Vectors { get; init; }

        /// <summary>
        /// Gets what the call cost, or null when the provider reported nothing.
        /// </summary>
        /// <remarks>
        /// Null means unknown, never free. A caller metering spend has to treat it as a gap in its
        /// accounting rather than as a zero, which is why an unreported count is not defaulted.
        /// </remarks>
        public EmbeddingUsage? Usage { get; init; }
    }
}
