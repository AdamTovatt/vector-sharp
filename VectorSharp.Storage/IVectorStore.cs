namespace VectorSharp.Storage
{
    /// <summary>
    /// Defines the interface for a vector store that supports adding, removing,
    /// and searching vectors by cosine similarity.
    /// </summary>
    /// <typeparam name="TKey">The type of the vector identifier. Must be an unmanaged, equatable type
    /// such as <see cref="int"/>, <see cref="long"/>, or <see cref="Guid"/>.</typeparam>
    public interface IVectorStore<TKey> : IDisposable
        where TKey : unmanaged, IEquatable<TKey>
    {
        /// <summary>
        /// Gets the human-readable name of this store.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the dimensionality of vectors in this store.
        /// </summary>
        int Dimension { get; }

        /// <summary>
        /// Gets the number of vectors currently in the store.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Adds a vector to the store.
        /// </summary>
        /// <param name="id">The unique identifier for the vector.</param>
        /// <param name="values">The vector values. Must match the store's <see cref="Dimension"/>.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous add operation.</returns>
        Task AddAsync(TKey id, float[] values, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a vector from the store by its identifier.
        /// </summary>
        /// <param name="id">The identifier of the vector to remove.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task whose result is true if the vector was found and removed; otherwise, false.</returns>
        Task<bool> RemoveAsync(TKey id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the most similar vectors to the given query vector using cosine similarity.
        /// </summary>
        /// <param name="queryVector">The query vector to compare against. Must match the store's <see cref="Dimension"/>.</param>
        /// <param name="count">The maximum number of results to return.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>Results sorted by similarity score, highest first.</returns>
        Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the most similar vectors to the given query vector using cosine similarity,
        /// considering only vectors whose identifier satisfies the supplied <paramref name="filter"/>.
        /// </summary>
        /// <remarks>
        /// The default implementation falls back to the unfiltered overload and post-filters the
        /// results client-side, which is correct but may scan more candidates than necessary.
        /// Concrete implementations should override this method to apply the filter during the
        /// scan for better performance. When the store runs the scan in parallel, the filter
        /// may be invoked concurrently from multiple threads, so it should be thread-safe.
        /// Exceptions thrown from the filter on the parallel path may be wrapped in an
        /// <see cref="AggregateException"/>.
        /// </remarks>
        /// <param name="queryVector">The query vector to compare against. Must match the store's <see cref="Dimension"/>.</param>
        /// <param name="count">The maximum number of results to return, after filtering.</param>
        /// <param name="filter">A predicate evaluated once per stored vector; only vectors for which
        /// it returns <c>true</c> are considered. Should be cheap. When <c>null</c>,
        /// behaves identically to the non-filtered overload.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>Results sorted by similarity score, highest first.</returns>
        async Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count, Func<TKey, bool>? filter, CancellationToken cancellationToken = default)
        {
            if (filter is null)
                return await FindMostSimilarAsync(queryVector, count, cancellationToken).ConfigureAwait(false);

            int candidateCount = Count;
            if (candidateCount == 0 || count <= 0)
                return Array.Empty<SearchResult<TKey>>();

            IReadOnlyList<SearchResult<TKey>> candidates = await FindMostSimilarAsync(queryVector, candidateCount, cancellationToken).ConfigureAwait(false);

            List<SearchResult<TKey>> filtered = new List<SearchResult<TKey>>(Math.Min(count, candidates.Count));
            foreach (SearchResult<TKey> candidate in candidates)
            {
                if (filter(candidate.Id))
                {
                    filtered.Add(candidate);
                    if (filtered.Count >= count)
                        break;
                }
            }
            return filtered;
        }
    }
}
