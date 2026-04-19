namespace VectorSharp.Embedding
{
    /// <summary>
    /// Defines the contract for a component that produces vector embeddings from text.
    /// Implementations may use local model inference, remote API calls, or any other source.
    /// Each instance owns its own resources and must be disposed when no longer needed.
    /// Individual instances are NOT required to be thread-safe — the <see cref="EmbeddingService"/>
    /// creates one instance per worker to avoid shared state.
    /// </summary>
    public interface IEmbeddingProvider : IDisposable
    {
        /// <summary>
        /// Gets the dimensionality of the embedding vectors produced by this provider.
        /// </summary>
        int Dimension { get; }

        /// <summary>
        /// Produces a vector embedding for the given text.
        /// </summary>
        /// <param name="text">The text to embed.</param>
        /// <param name="purpose">The intended purpose of the embedding. Providers that support
        /// task-specific embeddings (e.g., Nomic Embed) will use this to optimize the output.
        /// Providers that do not distinguish between purposes will ignore this parameter.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A float array of length <see cref="Dimension"/> representing the embedding.</returns>
        Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default);
    }
}
