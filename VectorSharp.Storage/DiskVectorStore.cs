using System.Buffers;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

        private FileStream? _fileStream;
        private MemoryMappedFile? _mappedFile;
        private int _recordCount;
        private bool _disposed;

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public int Dimension => _dimension;

        /// <inheritdoc />
        public int Count => _recordCount - _deletedKeys.Count;

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
        public async Task AddAsync(TKey id, float[] values)
        {
            ArgumentNullException.ThrowIfNull(values);

            if (values.Length != _dimension)
                throw new ArgumentException($"Vector must have {_dimension} dimensions.", nameof(values));

            float magnitude = CosineSimilarity.CalculateMagnitude(values.AsSpan());

            await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    // Dispose existing mmap
                    DisposeMappedFile();

                    // Extend file and write record
                    long recordOffset = BinaryFormat.HeaderSize + ((long)_recordCount * _recordSize);
                    _fileStream!.Position = recordOffset;
                    BinaryFormat.WriteRecord(_fileStream, id, magnitude, values.AsSpan());
                    _fileStream.Flush();

                    // Update header record count
                    _recordCount++;
                    _fileStream.Position = 0;
                    BinaryFormat.WriteHeader(_fileStream, _dimension, _keySize, _recordCount);
                    _fileStream.Flush();

                    // Re-create mmap
                    RecreateMappedFile();
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            });
        }

        /// <inheritdoc />
        public async Task<bool> RemoveAsync(TKey id)
        {
            return await Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    // Check if the key exists in the file by scanning
                    if (_deletedKeys.Contains(id))
                        return false;

                    bool found = FindKeyInFile(id);
                    if (found)
                    {
                        _deletedKeys.Add(id);
                        return true;
                    }

                    return false;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            });
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count)
        {
            if (queryVector == null || queryVector.Length == 0 || count <= 0)
                return Array.Empty<SearchResult<TKey>>();

            if (queryVector.Length != _dimension)
                throw new ArgumentException($"Query vector must have {_dimension} dimensions.", nameof(queryVector));

            float queryMagnitude = CosineSimilarity.CalculateMagnitude(queryVector.AsSpan());
            if (queryMagnitude == 0)
                return Array.Empty<SearchResult<TKey>>();

            return await Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    int activeRecordCount = _recordCount - _deletedKeys.Count;
                    if (activeRecordCount == 0)
                        return Array.Empty<SearchResult<TKey>>();

                    if (activeRecordCount > ParallelThreshold)
                    {
                        return FindMostSimilarParallel(queryVector, queryMagnitude, count);
                    }
                    else
                    {
                        return FindMostSimilarSequential(queryVector, queryMagnitude, count);
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            });
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
                    int newRecordCount = _recordCount - _deletedKeys.Count;

                    using (FileStream tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        BinaryFormat.WriteHeader(tempStream, _dimension, _keySize, newRecordCount);

                        using (MemoryMappedViewAccessor accessor = _mappedFile!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                        {
                            float[] valueBuffer = new float[_dimension];

                            for (int i = 0; i < _recordCount; i++)
                            {
                                long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);

                                TKey key = ReadKeyFromAccessor(accessor, offset);

                                if (_deletedKeys.Contains(key))
                                    continue;

                                float magnitude = ReadMagnitudeFromAccessor(accessor, offset + _keySize);
                                ReadValuesFromAccessor(accessor, offset + _keySize + 4, valueBuffer);

                                BinaryFormat.WriteRecord(tempStream, key, magnitude, valueBuffer.AsSpan());
                            }
                        }
                    }

                    // Replace original file
                    DisposeMappedFile();
                    _fileStream!.Dispose();
                    _fileStream = null;

                    File.Delete(_filePath);
                    File.Move(tempPath, _filePath);

                    _recordCount = newRecordCount;
                    _deletedKeys.Clear();

                    // Re-open
                    _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    RecreateMappedFile();
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

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarSequential(float[] queryVector, float queryMagnitude, int count)
        {
            MinHeap<SearchResult<TKey>> minHeap = new MinHeap<SearchResult<TKey>>(count);
            ReadOnlySpan<float> querySpan = queryVector.AsSpan();

            float[] valueBuffer = ArrayPool<float>.Shared.Rent(_dimension);
            try
            {
                using MemoryMappedViewAccessor accessor = _mappedFile!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                for (int i = 0; i < _recordCount; i++)
                {
                    long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);

                    TKey key = ReadKeyFromAccessor(accessor, offset);

                    if (_deletedKeys.Count > 0 && _deletedKeys.Contains(key))
                        continue;

                    float magnitude = ReadMagnitudeFromAccessor(accessor, offset + _keySize);
                    ReadValuesFromAccessor(accessor, offset + _keySize + 4, valueBuffer);

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

            return ExtractSortedResults(minHeap);
        }

        private IReadOnlyList<SearchResult<TKey>> FindMostSimilarParallel(float[] queryVector, float queryMagnitude, int count)
        {
            ConcurrentBag<(SearchResult<TKey> Item, float Priority)[]> partitionResults = new();

            Parallel.ForEach(
                Partitioner.Create(0, _recordCount),
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

                            TKey key = ReadKeyFromAccessor(accessor, offset);

                            if (_deletedKeys.Count > 0 && _deletedKeys.Contains(key))
                                continue;

                            float magnitude = ReadMagnitudeFromAccessor(accessor, offset + _keySize);
                            ReadValuesFromAccessor(accessor, offset + _keySize + 4, valueBuffer);

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

            return ExtractSortedResults(finalHeap);
        }

        private bool FindKeyInFile(TKey id)
        {
            if (_mappedFile == null || _recordCount == 0)
                return false;

            using MemoryMappedViewAccessor accessor = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            for (int i = 0; i < _recordCount; i++)
            {
                long offset = BinaryFormat.HeaderSize + ((long)i * _recordSize);
                TKey key = ReadKeyFromAccessor(accessor, offset);

                if (key.Equals(id))
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TKey ReadKeyFromAccessor(MemoryMappedViewAccessor accessor, long offset)
        {
            byte[] keyBytes = new byte[_keySize];
            accessor.ReadArray(offset, keyBytes, 0, _keySize);
            return MemoryMarshal.Read<TKey>(keyBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ReadMagnitudeFromAccessor(MemoryMappedViewAccessor accessor, long offset)
        {
            byte[] magBytes = new byte[4];
            accessor.ReadArray(offset, magBytes, 0, 4);
            return BitConverter.ToSingle(magBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadValuesFromAccessor(MemoryMappedViewAccessor accessor, long offset, float[] buffer)
        {
            byte[] valueBytes = new byte[_dimension * 4];
            accessor.ReadArray(offset, valueBytes, 0, valueBytes.Length);
            MemoryMarshal.Cast<byte, float>(valueBytes.AsSpan()).CopyTo(buffer.AsSpan(0, _dimension));
        }

        private static SearchResult<TKey>[] ExtractSortedResults(MinHeap<SearchResult<TKey>> heap)
        {
            (SearchResult<TKey> Item, float Priority)[] sorted = heap.GetSortedDescending();
            SearchResult<TKey>[] results = new SearchResult<TKey>[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                results[i] = sorted[i].Item;
            }

            return results;
        }
    }
}
