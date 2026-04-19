using System.Buffers;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace VectorSharp.Storage
{
    /// <summary>
    /// A disk-backed vector store that uses memory-mapped files for persistence and search.
    /// Vectors are stored in a binary file and searched via sequential scan through mapped memory.
    /// Supports concurrent reads and exclusive writes.
    /// </summary>
    /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
    public sealed class DiskVectorStore<TKey> : IVectorStore<TKey>
        where TKey : unmanaged, IEquatable<TKey>
    {
        private static readonly int ParallelThreshold = Math.Max(512, Environment.ProcessorCount * 256);

        private readonly string _filePath;
        private readonly int _dimension;
        private readonly int _keySize;
        private readonly int _recordSize;
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly HashSet<TKey> _deletedKeys = new();
        private readonly Dictionary<TKey, int> _keyIndex = new();

        private FileStream? _fileStream;
        private MemoryMappedFile? _mappedFile;
        private int _recordCount;
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
                    return _recordCount - _deletedKeys.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiskVectorStore{TKey}"/> class.
        /// If the file does not exist, a new empty store is created.
        /// If the file exists, its header is read and validated.
        /// </summary>
        /// <param name="name">A human-readable name for this store.</param>
        /// <param name="filePath">The path to the backing file.</param>
        /// <param name="dimension">The dimension of vectors to store. Must be positive.</param>
        /// <exception cref="ArgumentNullException">Thrown when name or filePath is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when dimension is less than or equal to zero.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an existing file has incompatible dimensions or key size.</exception>
        public DiskVectorStore(string name, string filePath, int dimension)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(filePath);

            if (dimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive.");

            Name = name;
            _filePath = filePath;
            _dimension = dimension;
            _keySize = Unsafe.SizeOf<TKey>();
            _recordSize = BinaryFormat.CalculateRecordSize(_keySize, dimension);

            if (File.Exists(filePath))
            {
                OpenExistingFile();
            }
            else
            {
                CreateNewFile();
            }
        }

        /// <inheritdoc />
        public Task AddAsync(TKey id, float[] values, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(values);

            if (values.Length != _dimension)
                throw new ArgumentException($"Vector must have {_dimension} dimensions.", nameof(values));

            float magnitude = CosineSimilarity.CalculateMagnitude(values.AsSpan());

            _lock.EnterWriteLock();
            try
            {
                _deletedKeys.Remove(id);

                DisposeMappedFile();
                try
                {
                    long recordOffset = BinaryFormat.HeaderSize + ((long)_recordCount * _recordSize);
                    _fileStream!.Position = recordOffset;
                    BinaryFormat.WriteRecord(_fileStream, id, magnitude, values.AsSpan());
                    _fileStream.Flush();

                    _recordCount++;
                    _fileStream.Position = 0;
                    BinaryFormat.WriteHeader(_fileStream, _dimension, _keySize, _recordCount);
                    _fileStream.Flush();

                    _keyIndex[id] = _recordCount - 1;
                }
                finally
                {
                    RecreateMappedFile();
                }
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
                if (_deletedKeys.Contains(id))
                    return Task.FromResult(false);

                if (!_keyIndex.TryGetValue(id, out int recordIndex))
                    return Task.FromResult(false);

                // Write deleted status byte to disk
                long statusOffset = BinaryFormat.HeaderSize + ((long)recordIndex * _recordSize);
                DisposeMappedFile();
                try
                {
                    _fileStream!.Position = statusOffset;
                    _fileStream.WriteByte(BinaryFormat.RecordStatusDeleted);
                    _fileStream.Flush();
                }
                finally
                {
                    RecreateMappedFile();
                }

                _deletedKeys.Add(id);
                return Task.FromResult(true);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count, CancellationToken cancellationToken = default)
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
                    int activeRecordCount = _recordCount - _deletedKeys.Count;
                    if (activeRecordCount == 0)
                        return Array.Empty<SearchResult<TKey>>();

                    if (activeRecordCount > ParallelThreshold)
                    {
                        return FindMostSimilarParallel(queryVector, queryMagnitude, count, cancellationToken);
                    }
                    else
                    {
                        return FindMostSimilarSequential(queryVector, queryMagnitude, count, cancellationToken);
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Rewrites the backing file without deleted records, reclaiming disk space.
        /// </summary>
        /// <returns>A task that represents the asynchronous compact operation.</returns>
        public async Task CompactAsync()
        {
            await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    if (_deletedKeys.Count == 0)
                        return;

                    string tempPath = _filePath + ".tmp";
                    int newRecordCount = 0;

                    using (FileStream tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        // Write placeholder header — will be overwritten with actual count
                        BinaryFormat.WriteHeader(tempStream, _dimension, _keySize, 0);

                        using (MemoryMappedViewAccessor accessor = _mappedFile!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                        {
                            float[] valueBuffer = new float[_dimension];

                            for (int i = 0; i < _recordCount; i++)
                            {
                                long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);

                                byte status = ReadStatusFromAccessor(accessor, offset);
                                if (status == BinaryFormat.RecordStatusDeleted)
                                    continue;

                                TKey key = ReadKeyFromAccessor(accessor, offset + 1);

                                if (_deletedKeys.Contains(key))
                                    continue;

                                float magnitude = ReadMagnitudeFromAccessor(accessor, offset + 1 + _keySize);
                                ReadValuesFromAccessor(accessor, offset + 1 + _keySize + 4, valueBuffer);

                                BinaryFormat.WriteRecord(tempStream, key, magnitude, valueBuffer.AsSpan());
                                newRecordCount++;
                            }
                        }

                        // Rewrite header with actual record count
                        tempStream.Position = 0;
                        BinaryFormat.WriteHeader(tempStream, _dimension, _keySize, newRecordCount);
                    }

                    // Replace original file using rename-dance for crash safety
                    DisposeMappedFile();
                    _fileStream!.Dispose();
                    _fileStream = null;

                    string backupPath = _filePath + ".bak";
                    File.Move(_filePath, backupPath);
                    File.Move(tempPath, _filePath);
                    File.Delete(backupPath);

                    _recordCount = newRecordCount;
                    _deletedKeys.Clear();

                    // Re-open
                    _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    RecreateMappedFile();

                    // Rebuild index with new record positions
                    _keyIndex.Clear();
                    if (_recordCount > 0 && _mappedFile != null)
                    {
                        using MemoryMappedViewAccessor accessor = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                        for (int i = 0; i < _recordCount; i++)
                        {
                            long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);
                            TKey key = ReadKeyFromAccessor(accessor, offset + 1);
                            _keyIndex[key] = i;
                        }
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
            if (!_disposed)
            {
                DisposeMappedFile();
                _fileStream?.Dispose();
                _fileStream = null;
                _lock.Dispose();
                _disposed = true;
            }
        }

        private void CreateNewFile()
        {
            _fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            BinaryFormat.WriteHeader(_fileStream, _dimension, _keySize, 0);
            _fileStream.Flush();
            _recordCount = 0;
            RecreateMappedFile();
        }

        private void OpenExistingFile()
        {
            _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            (int dimension, int keySize, int recordCount) = BinaryFormat.ReadHeader(_fileStream);

            if (dimension != _dimension)
                throw new InvalidOperationException($"File contains vectors with dimension {dimension} but store expects dimension {_dimension}.");

            if (keySize != _keySize)
                throw new InvalidOperationException($"File contains keys of {keySize} bytes but store expects keys of {_keySize} bytes.");

            _recordCount = recordCount;
            RecreateMappedFile();

            // Populate _keyIndex and _deletedKeys from existing records
            if (_recordCount > 0 && _mappedFile != null)
            {
                using MemoryMappedViewAccessor accessor = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                for (int i = 0; i < _recordCount; i++)
                {
                    long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);
                    byte status = ReadStatusFromAccessor(accessor, offset);
                    TKey key = ReadKeyFromAccessor(accessor, offset + 1);
                    _keyIndex[key] = i;

                    if (status == BinaryFormat.RecordStatusDeleted)
                    {
                        _deletedKeys.Add(key);
                    }
                }
            }
        }

        private void RecreateMappedFile()
        {
            long fileSize = _fileStream!.Length;
            if (fileSize == 0)
                return;

            _mappedFile = MemoryMappedFile.CreateFromFile(
                _fileStream,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: true);
        }

        private void DisposeMappedFile()
        {
            _mappedFile?.Dispose();
            _mappedFile = null;
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarSequential(float[] queryVector, float queryMagnitude, int count, CancellationToken cancellationToken)
        {
            MinHeap<SearchResult<TKey>> minHeap = new MinHeap<SearchResult<TKey>>(count);
            ReadOnlySpan<float> querySpan = queryVector.AsSpan();

            float[] valueBuffer = ArrayPool<float>.Shared.Rent(_dimension);
            try
            {
                using MemoryMappedViewAccessor accessor = _mappedFile!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                for (int i = 0; i < _recordCount; i++)
                {
                    if (i % 256 == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);

                    byte status = ReadStatusFromAccessor(accessor, offset);
                    if (status == BinaryFormat.RecordStatusDeleted)
                        continue;

                    TKey key = ReadKeyFromAccessor(accessor, offset + 1);

                    if (_deletedKeys.Count > 0 && _deletedKeys.Contains(key))
                        continue;

                    float magnitude = ReadMagnitudeFromAccessor(accessor, offset + 1 + _keySize);
                    ReadValuesFromAccessor(accessor, offset + 1 + _keySize + 4, valueBuffer);

                    ReadOnlySpan<float> storedSpan = valueBuffer.AsSpan(0, _dimension);
                    float similarity = CosineSimilarity.Calculate(querySpan, queryMagnitude, storedSpan, magnitude);

                    SearchResult<TKey> result = new SearchResult<TKey> { Id = key, Score = similarity, StoreName = Name };
                    minHeap.Add(result, similarity);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(valueBuffer);
            }

            return minHeap.ExtractSortedResults();
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarParallel(float[] queryVector, float queryMagnitude, int count, CancellationToken cancellationToken)
        {
            ConcurrentBag<(SearchResult<TKey> Item, float Priority)[]> partitionResults = new();

            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.ForEach(
                Partitioner.Create(0, _recordCount),
                parallelOptions,
                () => new MinHeap<SearchResult<TKey>>(count),
                (range, state, localHeap) =>
                {
                    ReadOnlySpan<float> querySpan = queryVector.AsSpan();
                    float[] valueBuffer = ArrayPool<float>.Shared.Rent(_dimension);

                    try
                    {
                        using MemoryMappedViewAccessor accessor = _mappedFile!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);

                            byte status = ReadStatusFromAccessor(accessor, offset);
                            if (status == BinaryFormat.RecordStatusDeleted)
                                continue;

                            TKey key = ReadKeyFromAccessor(accessor, offset + 1);

                            if (_deletedKeys.Count > 0 && _deletedKeys.Contains(key))
                                continue;

                            float magnitude = ReadMagnitudeFromAccessor(accessor, offset + 1 + _keySize);
                            ReadValuesFromAccessor(accessor, offset + 1 + _keySize + 4, valueBuffer);

                            ReadOnlySpan<float> storedSpan = valueBuffer.AsSpan(0, _dimension);
                            float similarity = CosineSimilarity.Calculate(querySpan, queryMagnitude, storedSpan, magnitude);

                            SearchResult<TKey> result = new SearchResult<TKey> { Id = key, Score = similarity, StoreName = Name };
                            localHeap.Add(result, similarity);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(valueBuffer);
                    }

                    return localHeap;
                },
                localHeap => partitionResults.Add(localHeap.GetSortedDescending()));

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ReadStatusFromAccessor(MemoryMappedViewAccessor accessor, long offset)
        {
            return accessor.ReadByte(offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TKey ReadKeyFromAccessor(MemoryMappedViewAccessor accessor, long offset)
        {
            accessor.Read(offset, out TKey key);
            return key;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ReadMagnitudeFromAccessor(MemoryMappedViewAccessor accessor, long offset)
        {
            accessor.Read(offset, out float magnitude);
            return magnitude;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadValuesFromAccessor(MemoryMappedViewAccessor accessor, long offset, float[] buffer)
        {
            accessor.ReadArray(offset, buffer, 0, _dimension);
        }

    }
}
