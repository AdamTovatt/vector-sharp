namespace VectorSharp.Embedding
{
    /// <summary>
    /// Specifies the intended purpose of an embedding, allowing providers that support
    /// task-specific embeddings (such as Nomic Embed) to optimize the output accordingly.
    /// Providers that do not distinguish between purposes will produce the same result for both.
    /// </summary>
    public enum EmbeddingPurpose
    {
        /// <summary>
        /// The text is a document being stored for later retrieval.
        /// </summary>
        Document,

        /// <summary>
        /// The text is a search query used to find similar documents.
        /// </summary>
        Query
    }
}
