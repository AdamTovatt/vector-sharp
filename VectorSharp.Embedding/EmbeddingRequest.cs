namespace VectorSharp.Embedding
{
    /// <summary>
    /// Internal message type representing a pending embedding request in the channel.
    /// </summary>
    internal sealed class EmbeddingRequest
    {
        /// <summary>
        /// The text to produce an embedding for.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// The intended purpose of the embedding.
        /// </summary>
        public required EmbeddingPurpose Purpose { get; init; }

        /// <summary>
        /// The completion source that the caller awaits. The worker sets the result
        /// after producing the embedding, or sets an exception if inference fails.
        /// </summary>
        public required TaskCompletionSource<float[]> CompletionSource { get; init; }
    }
}
