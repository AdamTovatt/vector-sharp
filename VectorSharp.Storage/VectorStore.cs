namespace VectorSharp.Storage
{
    /// <summary>
    /// Factory class for creating vector store instances.
    /// </summary>
    public static class VectorStore
    {
        /// <summary>
        /// Creates a new in-memory vector store using cosine similarity.
        /// </summary>
        /// <typeparam name="TKey">The type of the vector identifier (e.g., <see cref="int"/>, <see cref="long"/>, <see cref="Guid"/>).</typeparam>
        /// <param name="name">A human-readable name for the store, used to identify results in multi-store searches.</param>
        /// <param name="dimension">The dimensionality of vectors to store.</param>
        /// <returns>A new in-memory <see cref="CosineVectorStore{TKey}"/>.</returns>
        public static CosineVectorStore<TKey> Create<TKey>(string name, int dimension)
            where TKey : unmanaged, IEquatable<TKey>
        {
            return new CosineVectorStore<TKey>(name, dimension);
        }

        /// <summary>
        /// Opens a disk-backed vector store using a memory-mapped file.
        /// If the file does not exist, a new empty store is created.
        /// </summary>
        /// <typeparam name="TKey">The type of the vector identifier (e.g., <see cref="int"/>, <see cref="long"/>, <see cref="Guid"/>).</typeparam>
        /// <param name="name">A human-readable name for the store, used to identify results in multi-store searches.</param>
        /// <param name="filePath">The path to the backing file.</param>
        /// <param name="dimension">The dimensionality of vectors to store.</param>
        /// <returns>A new disk-backed <see cref="DiskVectorStore{TKey}"/>.</returns>
        public static DiskVectorStore<TKey> OpenFile<TKey>(string name, string filePath, int dimension)
            where TKey : unmanaged, IEquatable<TKey>
        {
            return new DiskVectorStore<TKey>(name, filePath, dimension);
        }
    }
}
