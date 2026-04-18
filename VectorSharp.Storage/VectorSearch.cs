namespace VectorSharp.Storage
{
    /// <summary>
    /// Provides static methods for searching across multiple vector stores simultaneously.
    /// </summary>
    public static class VectorSearch
    {
        /// <summary>
        /// Searches multiple vector stores in parallel and returns the global top-K results
        /// sorted by similarity score, highest first. Each result includes the name of the
        /// store it came from.
        /// </summary>
        /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
        /// <param name="queryVector">The query vector to compare against.</param>
        /// <param name="count">The maximum number of results to return across all stores.</param>
        /// <param name="stores">The stores to search.</param>
        /// <returns>The top results across all stores, sorted by similarity score descending.</returns>
        /// <exception cref="ArgumentNullException">Thrown when queryVector or stores is null.</exception>
        /// <exception cref="ArgumentException">Thrown when no stores are provided or count is less than or equal to zero.</exception>
        public static async Task<IReadOnlyList<SearchResult<TKey>>> SearchAsync<TKey>(
            float[] queryVector,
            int count,
            params IVectorStore<TKey>[] stores)
            where TKey : unmanaged, IEquatable<TKey>
        {
            ArgumentNullException.ThrowIfNull(queryVector);
            ArgumentNullException.ThrowIfNull(stores);

            if (stores.Length == 0)
                throw new ArgumentException("At least one store must be provided.", nameof(stores));

            if (count <= 0)
                return Array.Empty<SearchResult<TKey>>();

            // Query all stores in parallel
            Task<IReadOnlyList<SearchResult<TKey>>>[] tasks =
                new Task<IReadOnlyList<SearchResult<TKey>>>[stores.Length];

            for (int i = 0; i < stores.Length; i++)
            {
                tasks[i] = stores[i].FindMostSimilarAsync(queryVector, count);
            }

            IReadOnlyList<SearchResult<TKey>>[] allResults = await Task.WhenAll(tasks);

            // Merge results using MinHeap for global top-K
            MinHeap<SearchResult<TKey>> heap = new MinHeap<SearchResult<TKey>>(count);
            foreach (IReadOnlyList<SearchResult<TKey>> storeResults in allResults)
            {
                foreach (SearchResult<TKey> result in storeResults)
                {
                    heap.Add(result, result.Score);
                }
            }

            // Return sorted descending
            (SearchResult<TKey> Item, float Priority)[] sorted = heap.GetSortedDescending();
            SearchResult<TKey>[] finalResults = new SearchResult<TKey>[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                finalResults[i] = sorted[i].Item;
            }

            return finalResults;
        }
    }
}
