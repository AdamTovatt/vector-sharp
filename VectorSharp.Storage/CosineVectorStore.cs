using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace VectorSharp.Storage
{
    /// <summary>
    /// An in-memory vector store implementation that uses cosine similarity for finding
    /// the most similar vectors. Optimized with SIMD operations, pre-computed magnitudes,
    /// and automatic parallel processing for large datasets.
    /// </summary>
    /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
    public class CosineVectorStore<TKey> : IVectorStore<TKey>
        where TKey : unmanaged, IEquatable<TKey>
    {
        internal static readonly int ParallelThreshold = Math.Max(512, Environment.ProcessorCount * 256);

        private readonly List<StoredVector<TKey>> _vectors = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly int _dimension;
        private bool _disposed;

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public int Dimension => _dimension;

        /// <inheritdoc />
        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _vectors.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CosineVectorStore{TKey}"/> class.
        /// </summary>
        /// <param name="name">A human-readable name for this store.</param>
        /// <param name="dimension">The dimension of vectors that will be stored. Must be positive.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when dimension is less than or equal to zero.</exception>
        /// <exception cref="ArgumentNullException">Thrown when name is null.</exception>
        public CosineVectorStore(string name, int dimension)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (dimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive.");

            Name = name;
            _dimension = dimension;
        }

        /// <inheritdoc />
        public Task AddAsync(TKey id, float[] values, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(values);

            if (values.Length != _dimension)
                throw new ArgumentException($"Vector must have {_dimension} dimensions.", nameof(values));

            float magnitude = CosineSimilarity.CalculateMagnitude(values.AsSpan());
            StoredVector<TKey> vector = new StoredVector<TKey>(id, values, magnitude);

            _lock.EnterWriteLock();
            try
            {
                _vectors.Add(vector);
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> RemoveAsync(TKey id, CancellationToken cancellationToken = default)
        {
            _lock.EnterWriteLock();
            try
            {
                for (int i = 0; i < _vectors.Count; i++)
                {
                    if (_vectors[i].Id.Equals(id))
                    {
                        _vectors.RemoveAt(i);
                        return Task.FromResult(true);
                    }
                }

                return Task.FromResult(false);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count, CancellationToken cancellationToken = default)
        {
            return FindMostSimilarAsync(queryVector, count, filter: null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count, Func<TKey, bool>? filter, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(queryVector);

            if (queryVector.Length == 0 || count <= 0)
                return Array.Empty<SearchResult<TKey>>();

            if (queryVector.Length != _dimension)
                throw new ArgumentException($"Query vector must have {_dimension} dimensions.", nameof(queryVector));

            float queryMagnitude = CosineSimilarity.CalculateMagnitude(queryVector.AsSpan());
            if (queryMagnitude == 0)
                return Array.Empty<SearchResult<TKey>>();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                _lock.EnterReadLock();
                try
                {
                    bool useParallel = _vectors.Count > ParallelThreshold;

                    if (filter is null)
                    {
                        return useParallel
                            ? FindMostSimilarParallel(queryVector, queryMagnitude, count, cancellationToken)
                            : FindMostSimilarSequential(queryVector, queryMagnitude, count, cancellationToken);
                    }

                    return useParallel
                        ? FindMostSimilarParallelFiltered(queryVector, queryMagnitude, count, filter, cancellationToken)
                        : FindMostSimilarSequentialFiltered(queryVector, queryMagnitude, count, filter, cancellationToken);
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Saves all vectors in the store to a stream using the VectorSharp binary format.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <exception cref="ArgumentException">Thrown when stream is not writable.</exception>
        public async Task SaveAsync(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanWrite)
                throw new ArgumentException("Stream must be writable.", nameof(stream));

            await Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    int keySize = Unsafe.SizeOf<TKey>();
                    BinaryFormat.WriteHeader(stream, _dimension, keySize, _vectors.Count);

                    foreach (StoredVector<TKey> vector in _vectors)
                    {
                        BinaryFormat.WriteRecord(stream, vector.Id, vector.Magnitude, vector.GetSpan());
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            });
        }

        /// <summary>
        /// Loads vectors from a stream into the store, replacing any existing vectors.
        /// The stream must contain data in the VectorSharp binary format.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>A task that represents the asynchronous load operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        /// <exception cref="ArgumentException">Thrown when stream is not readable.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the data has incompatible dimensions or key size.</exception>
        public async Task LoadAsync(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable.", nameof(stream));

            await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    (int dimension, int keySize, int recordCount) = BinaryFormat.ReadHeader(stream);

                    if (dimension != _dimension)
                        throw new InvalidOperationException($"File contains vectors with dimension {dimension} but store expects dimension {_dimension}.");

                    int expectedKeySize = Unsafe.SizeOf<TKey>();
                    if (keySize != expectedKeySize)
                        throw new InvalidOperationException($"File contains keys of {keySize} bytes but store expects keys of {expectedKeySize} bytes.");

                    _vectors.Clear();
                    _vectors.Capacity = recordCount;

                    for (int i = 0; i < recordCount; i++)
                    {
                        (byte status, TKey key, float magnitude, float[] values) = BinaryFormat.ReadRecord<TKey>(stream, _dimension);

                        if (status == BinaryFormat.RecordStatusDeleted)
                            continue;

                        _vectors.Add(new StoredVector<TKey>(key, values, magnitude));
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by the <see cref="CosineVectorStore{TKey}"/>.
        /// </summary>
        /// <param name="disposing">True if called from Dispose; false if called from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _lock.Dispose();
                }

                _disposed = true;
            }
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarSequential(float[] queryVector, float queryMagnitude, int count, CancellationToken cancellationToken)
        {
            MinHeap<SearchResult<TKey>> minHeap = new MinHeap<SearchResult<TKey>>(count);
            ReadOnlySpan<float> querySpan = queryVector.AsSpan();

            for (int i = 0; i < _vectors.Count; i++)
            {
                if (i % 256 == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                StoredVector<TKey> stored = _vectors[i];
                float similarity = CosineSimilarity.Calculate(querySpan, queryMagnitude, stored.GetSpan(), stored.Magnitude);
                SearchResult<TKey> result = new SearchResult<TKey> { Id = stored.Id, Score = similarity, StoreName = Name };
                minHeap.Add(result, similarity);
            }

            return minHeap.ExtractSortedResults();
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarSequentialFiltered(float[] queryVector, float queryMagnitude, int count, Func<TKey, bool> filter, CancellationToken cancellationToken)
        {
            MinHeap<SearchResult<TKey>> minHeap = new MinHeap<SearchResult<TKey>>(count);
            ReadOnlySpan<float> querySpan = queryVector.AsSpan();

            for (int i = 0; i < _vectors.Count; i++)
            {
                if (i % 256 == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                StoredVector<TKey> stored = _vectors[i];
                if (!filter(stored.Id))
                    continue;
                float similarity = CosineSimilarity.Calculate(querySpan, queryMagnitude, stored.GetSpan(), stored.Magnitude);
                SearchResult<TKey> result = new SearchResult<TKey> { Id = stored.Id, Score = similarity, StoreName = Name };
                minHeap.Add(result, similarity);
            }

            return minHeap.ExtractSortedResults();
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarParallel(float[] queryVector, float queryMagnitude, int count, CancellationToken cancellationToken)
        {
            ConcurrentBag<(SearchResult<TKey> Item, float Priority)[]> partitionResults = new();
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.ForEach(
                Partitioner.Create(0, _vectors.Count),
                parallelOptions,
                () => new MinHeap<SearchResult<TKey>>(count),
                (range, state, localHeap) =>
                {
                    ReadOnlySpan<float> querySpan = queryVector.AsSpan();

                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        StoredVector<TKey> stored = _vectors[i];
                        float similarity = CosineSimilarity.Calculate(querySpan, queryMagnitude, stored.GetSpan(), stored.Magnitude);
                        SearchResult<TKey> result = new SearchResult<TKey> { Id = stored.Id, Score = similarity, StoreName = Name };
                        localHeap.Add(result, similarity);
                    }

                    return localHeap;
                },
                localHeap => partitionResults.Add(localHeap.GetSortedDescending()));

            return MergePartitions(partitionResults, count);
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarParallelFiltered(float[] queryVector, float queryMagnitude, int count, Func<TKey, bool> filter, CancellationToken cancellationToken)
        {
            ConcurrentBag<(SearchResult<TKey> Item, float Priority)[]> partitionResults = new();
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.ForEach(
                Partitioner.Create(0, _vectors.Count),
                parallelOptions,
                () => new MinHeap<SearchResult<TKey>>(count),
                (range, state, localHeap) =>
                {
                    ReadOnlySpan<float> querySpan = queryVector.AsSpan();

                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        StoredVector<TKey> stored = _vectors[i];
                        if (!filter(stored.Id))
                            continue;
                        float similarity = CosineSimilarity.Calculate(querySpan, queryMagnitude, stored.GetSpan(), stored.Magnitude);
                        SearchResult<TKey> result = new SearchResult<TKey> { Id = stored.Id, Score = similarity, StoreName = Name };
                        localHeap.Add(result, similarity);
                    }

                    return localHeap;
                },
                localHeap => partitionResults.Add(localHeap.GetSortedDescending()));

            return MergePartitions(partitionResults, count);
        }

        private static IReadOnlyList<SearchResult<TKey>> MergePartitions(ConcurrentBag<(SearchResult<TKey> Item, float Priority)[]> partitionResults, int count)
        {
            MinHeap<SearchResult<TKey>> finalHeap = new MinHeap<SearchResult<TKey>>(count);
            foreach ((SearchResult<TKey> Item, float Priority)[] partition in partitionResults)
            {
                foreach ((SearchResult<TKey> item, float priority) in partition)
                {
                    finalHeap.Add(item, priority);
                }
            }

            return finalHeap.ExtractSortedResults();
        }
    }
}
