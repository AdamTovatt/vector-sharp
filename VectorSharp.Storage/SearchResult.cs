namespace VectorSharp.Storage
{
    /// <summary>
    /// Represents a search result from a vector similarity search.
    /// </summary>
    /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
    public readonly struct SearchResult<TKey>
        where TKey : unmanaged, IEquatable<TKey>
    {
        /// <summary>
        /// Gets the unique identifier of the matched vector.
        /// </summary>
        public required TKey Id { get; init; }

        /// <summary>
        /// Gets the cosine similarity score. Higher values indicate greater similarity.
        /// </summary>
        public required float Score { get; init; }

        /// <summary>
        /// Gets the name of the store that contained this result.
        /// </summary>
        public required string StoreName { get; init; }
    }
}
