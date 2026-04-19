namespace VectorSharp.Storage.Tests
{
    [TestClass]
    public class DiskVectorStoreTests
    {
        private const int DefaultDimension = 128;
        private const string DefaultName = "disk-store";

        private string GetTempFilePath() => Path.Combine(Path.GetTempPath(), $"vectorsharp_test_{Guid.NewGuid()}.dat");

        private void CleanupFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        #region Constructor

        [TestMethod]
        public void Constructor_CreatesNewFile_WhenNotExists()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                Assert.IsTrue(File.Exists(filePath));
                Assert.AreEqual(0, store.Count);
                Assert.AreEqual(DefaultDimension, store.Dimension);
                Assert.AreEqual(DefaultName, store.Name);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task Constructor_OpensExistingFile()
        {
            string filePath = GetTempFilePath();
            try
            {
                // Create and populate a store
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));
                    await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));
                }

                // Re-open the same file
                using DiskVectorStore<Guid> reopened = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                Assert.AreEqual(2, reopened.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task Constructor_DimensionMismatch_Throws()
        {
            string filePath = GetTempFilePath();
            try
            {
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, 128))
                {
                    await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(128));
                }

                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    new DiskVectorStore<Guid>(DefaultName, filePath, 256));
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public void Constructor_NullName_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                new DiskVectorStore<Guid>(null!, "file.dat", 128));
        }

        [TestMethod]
        public void Constructor_NullFilePath_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                new DiskVectorStore<Guid>(DefaultName, null!, 128));
        }

        [TestMethod]
        public void Constructor_ZeroDimension_Throws()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new DiskVectorStore<Guid>(DefaultName, GetTempFilePath(), 0));
        }

        #endregion

        #region Add and Remove

        [TestMethod]
        public async Task AddAsync_AppendsRecord()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));
                Assert.AreEqual(1, store.Count);

                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));
                Assert.AreEqual(2, store.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task AddAsync_DimensionMismatch_Throws()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                    store.AddAsync(Guid.NewGuid(), new float[64]));
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task RemoveAsync_MarksDeleted()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                Guid id = Guid.NewGuid();
                await store.AddAsync(id, TestHelpers.CreateRandomVector(DefaultDimension));
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));

                bool removed = await store.RemoveAsync(id);

                Assert.IsTrue(removed);
                Assert.AreEqual(1, store.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task RemoveAsync_NonExistent_ReturnsFalse()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

                bool removed = await store.RemoveAsync(Guid.NewGuid());

                Assert.IsFalse(removed);
                Assert.AreEqual(1, store.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Search

        [TestMethod]
        public async Task FindMostSimilarAsync_ReturnsCorrectResults()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                float[] targetVector = TestHelpers.CreateVector(1.0f, DefaultDimension);
                Guid targetId = Guid.NewGuid();
                await store.AddAsync(targetId, targetVector);
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateVector(-1.0f, DefaultDimension));

                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(targetVector, 1);

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(targetId, results[0].Id);
                Assert.AreEqual(1.0f, results[0].Score, 0.0001f);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task FindMostSimilarAsync_ExcludesDeletedKeys()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                float[] targetVector = TestHelpers.CreateVector(1.0f, DefaultDimension);
                Guid targetId = Guid.NewGuid();
                Guid otherId = Guid.NewGuid();
                await store.AddAsync(targetId, targetVector);
                await store.AddAsync(otherId, TestHelpers.CreateVector(-1.0f, DefaultDimension));

                // Remove the best match
                await store.RemoveAsync(targetId);

                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(targetVector, 1);

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(otherId, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task FindMostSimilarAsync_EmptyStore_ReturnsEmpty()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension), 10);

                Assert.AreEqual(0, results.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task FindMostSimilarAsync_ReturnsSortedDescending()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                float[] query = TestHelpers.CreateVector(1.0f, DefaultDimension);
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateVector(1.0f, DefaultDimension));
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateVector(-1.0f, DefaultDimension));
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 1));

                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(query, 3);

                Assert.AreEqual(3, results.Count);
                for (int i = 1; i < results.Count; i++)
                {
                    Assert.IsTrue(results[i - 1].Score >= results[i].Score,
                        $"Results not sorted at index {i}");
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task FindMostSimilarAsync_WithIntKeys()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);

                float[] vector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);
                await store.AddAsync(42, vector);
                await store.AddAsync(43, TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));

                IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(vector, 1);

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(42, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Compact

        [TestMethod]
        public async Task CompactAsync_RemovesDeletedRecords()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                Guid keepId = Guid.NewGuid();
                Guid deleteId = Guid.NewGuid();
                float[] keepVector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);

                await store.AddAsync(keepId, keepVector);
                await store.AddAsync(deleteId, TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 3));

                await store.RemoveAsync(deleteId);
                Assert.AreEqual(2, store.Count);

                await store.CompactAsync();
                Assert.AreEqual(2, store.Count);

                // Verify search still works after compaction
                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(keepVector, 1);
                Assert.AreEqual(keepId, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task CompactAsync_NoDeleted_IsNoOp()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));
                long sizeBefore = new FileInfo(filePath).Length;

                await store.CompactAsync();

                long sizeAfter = new FileInfo(filePath).Length;
                Assert.AreEqual(sizeBefore, sizeAfter);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task CompactAsync_PreservesDataAcrossReopen()
        {
            string filePath = GetTempFilePath();
            try
            {
                Guid keepId = Guid.NewGuid();
                float[] keepVector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);

                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    await store.AddAsync(keepId, keepVector);
                    await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));
                    await store.RemoveAsync(Guid.NewGuid()); // remove non-existent - should be fine

                    Guid deleteId = Guid.NewGuid();
                    await store.AddAsync(deleteId, TestHelpers.CreateRandomVector(DefaultDimension, seed: 3));
                    await store.RemoveAsync(deleteId);

                    await store.CompactAsync();
                }

                // Re-open and verify
                using DiskVectorStore<Guid> reopened = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                Assert.AreEqual(2, reopened.Count);

                IReadOnlyList<SearchResult<Guid>> results = await reopened.FindMostSimilarAsync(keepVector, 1);
                Assert.AreEqual(keepId, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Persistence Across Reopen

        [TestMethod]
        public async Task Persistence_SurvivesDisposeAndReopen()
        {
            string filePath = GetTempFilePath();
            try
            {
                Guid id = Guid.NewGuid();
                float[] vector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);

                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    await store.AddAsync(id, vector);
                }

                using DiskVectorStore<Guid> reopened = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                Assert.AreEqual(1, reopened.Count);

                IReadOnlyList<SearchResult<Guid>> results = await reopened.FindMostSimilarAsync(vector, 1);
                Assert.AreEqual(id, results[0].Id);
                Assert.AreEqual(1.0f, results[0].Score, 0.0001f);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Format Compatibility

        [TestMethod]
        public async Task FileFormat_CompatibleWithCosineVectorStoreSave()
        {
            string filePath = GetTempFilePath();
            try
            {
                // Save from in-memory store
                Guid id = Guid.NewGuid();
                float[] vector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);

                using CosineVectorStore<Guid> memStore = new CosineVectorStore<Guid>("mem", DefaultDimension);
                await memStore.AddAsync(id, vector);

                using (FileStream fs = File.Create(filePath))
                {
                    await memStore.SaveAsync(fs);
                }

                // Open with disk store
                using DiskVectorStore<Guid> diskStore = new DiskVectorStore<Guid>("disk", filePath, DefaultDimension);
                Assert.AreEqual(1, diskStore.Count);

                IReadOnlyList<SearchResult<Guid>> results = await diskStore.FindMostSimilarAsync(vector, 1);
                Assert.AreEqual(id, results[0].Id);
                Assert.AreEqual(1.0f, results[0].Score, 0.0001f);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Concurrency

        [TestMethod]
        public async Task ConcurrentReads_AreThreadSafe()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);

                for (int i = 0; i < 50; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                Task<IReadOnlyList<SearchResult<int>>>[] searchTasks =
                    new Task<IReadOnlyList<SearchResult<int>>>[10];

                for (int i = 0; i < searchTasks.Length; i++)
                {
                    float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: i + 1000);
                    searchTasks[i] = store.FindMostSimilarAsync(query, 5);
                }

                IReadOnlyList<SearchResult<int>>[] allResults = await Task.WhenAll(searchTasks);

                foreach (IReadOnlyList<SearchResult<int>> results in allResults)
                {
                    Assert.AreEqual(5, results.Count);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Delete Persistence

        [TestMethod]
        public async Task RemoveAsync_DeletePersistsAcrossReopen()
        {
            string filePath = GetTempFilePath();
            try
            {
                Guid keepId = Guid.NewGuid();
                Guid deleteId = Guid.NewGuid();
                float[] keepVector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);
                float[] deleteVector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 2);

                // Add two records and delete one
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    await store.AddAsync(keepId, keepVector);
                    await store.AddAsync(deleteId, deleteVector);
                    Assert.AreEqual(2, store.Count);

                    bool removed = await store.RemoveAsync(deleteId);
                    Assert.IsTrue(removed);
                    Assert.AreEqual(1, store.Count);
                }

                // Reopen and verify delete persisted
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    Assert.AreEqual(1, store.Count);

                    IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(keepVector, 10);
                    Assert.AreEqual(1, results.Count);
                    Assert.AreEqual(keepId, results[0].Id);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [TestMethod]
        public async Task RemoveAsync_DeletePersistsAcrossReopen_ThenCompact()
        {
            string filePath = GetTempFilePath();
            try
            {
                Guid keepId = Guid.NewGuid();
                Guid deleteId = Guid.NewGuid();
                float[] keepVector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);
                float[] deleteVector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 2);

                // Add two records, delete one, compact
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    await store.AddAsync(keepId, keepVector);
                    await store.AddAsync(deleteId, deleteVector);
                    await store.RemoveAsync(deleteId);
                    await store.CompactAsync();
                    Assert.AreEqual(1, store.Count);
                }

                // Reopen and verify
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    Assert.AreEqual(1, store.Count);

                    IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(keepVector, 10);
                    Assert.AreEqual(1, results.Count);
                    Assert.AreEqual(keepId, results[0].Id);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Null Query

        [TestMethod]
        public async Task FindMostSimilarAsync_NullQuery_Throws()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

                await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
                    store.FindMostSimilarAsync(null!, 10));
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion
    }
}
