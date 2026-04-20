namespace VectorSharp.Storage.Tests
{
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

        [Fact]
        public void Constructor_CreatesNewFile_WhenNotExists()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                Assert.True(File.Exists(filePath));
                Assert.Equal(0, store.Count);
                Assert.Equal(DefaultDimension, store.Dimension);
                Assert.Equal(DefaultName, store.Name);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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
                Assert.Equal(2, reopened.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task Constructor_DimensionMismatch_Throws()
        {
            string filePath = GetTempFilePath();
            try
            {
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, 128))
                {
                    await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(128));
                }

                Assert.Throws<InvalidOperationException>(() =>
                    new DiskVectorStore<Guid>(DefaultName, filePath, 256));
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public void Constructor_NullName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DiskVectorStore<Guid>(null!, "file.dat", 128));
        }

        [Fact]
        public void Constructor_NullFilePath_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DiskVectorStore<Guid>(DefaultName, null!, 128));
        }

        [Fact]
        public void Constructor_ZeroDimension_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DiskVectorStore<Guid>(DefaultName, GetTempFilePath(), 0));
        }

        #endregion

        #region Add and Remove

        [Fact]
        public async Task AddAsync_AppendsRecord()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));
                Assert.Equal(1, store.Count);

                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));
                Assert.Equal(2, store.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task AddAsync_DimensionMismatch_Throws()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                await Assert.ThrowsAsync<ArgumentException>(() =>
                    store.AddAsync(Guid.NewGuid(), new float[64]));
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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

                Assert.True(removed);
                Assert.Equal(1, store.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task RemoveAsync_NonExistent_ReturnsFalse()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

                bool removed = await store.RemoveAsync(Guid.NewGuid());

                Assert.False(removed);
                Assert.Equal(1, store.Count);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Search

        [Fact]
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

                Assert.Single(results);
                Assert.Equal(targetId, results[0].Id);
                TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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

                Assert.Single(results);
                Assert.Equal(otherId, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_EmptyStore_ReturnsEmpty()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);

                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension), 10);

                Assert.Empty(results);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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

                Assert.Equal(3, results.Count);
                for (int i = 1; i < results.Count; i++)
                {
                    Assert.True(results[i - 1].Score >= results[i].Score,
                        $"Results not sorted at index {i}");
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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

                Assert.Single(results);
                Assert.Equal(42, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Compact

        [Fact]
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
                Assert.Equal(2, store.Count);

                await store.CompactAsync();
                Assert.Equal(2, store.Count);

                // Verify search still works after compaction
                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(keepVector, 1);
                Assert.Equal(keepId, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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
                Assert.Equal(sizeBefore, sizeAfter);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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
                Assert.Equal(2, reopened.Count);

                IReadOnlyList<SearchResult<Guid>> results = await reopened.FindMostSimilarAsync(keepVector, 1);
                Assert.Equal(keepId, results[0].Id);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Persistence Across Reopen

        [Fact]
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
                Assert.Equal(1, reopened.Count);

                IReadOnlyList<SearchResult<Guid>> results = await reopened.FindMostSimilarAsync(vector, 1);
                Assert.Equal(id, results[0].Id);
                TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Format Compatibility

        [Fact]
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
                Assert.Equal(1, diskStore.Count);

                IReadOnlyList<SearchResult<Guid>> results = await diskStore.FindMostSimilarAsync(vector, 1);
                Assert.Equal(id, results[0].Id);
                TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Concurrency

        [Fact]
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
                    Assert.Equal(5, results.Count);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Delete Persistence

        [Fact]
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
                    Assert.Equal(2, store.Count);

                    bool removed = await store.RemoveAsync(deleteId);
                    Assert.True(removed);
                    Assert.Equal(1, store.Count);
                }

                // Reopen and verify delete persisted
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    Assert.Equal(1, store.Count);

                    IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(keepVector, 10);
                    Assert.Single(results);
                    Assert.Equal(keepId, results[0].Id);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
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
                    Assert.Equal(1, store.Count);
                }

                // Reopen and verify
                using (DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension))
                {
                    Assert.Equal(1, store.Count);

                    IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(keepVector, 10);
                    Assert.Single(results);
                    Assert.Equal(keepId, results[0].Id);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Null Query

        [Fact]
        public async Task FindMostSimilarAsync_NullQuery_Throws()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<Guid> store = new DiskVectorStore<Guid>(DefaultName, filePath, DefaultDimension);
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    store.FindMostSimilarAsync(null!, 10));
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion

        #region Filter

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_OnlyReturnsAllowedIds()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);
                for (int i = 0; i < 20; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                HashSet<int> allowed = new HashSet<int> { 2, 5, 7 };
                IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 10, allowed.Contains);

                Assert.Equal(3, results.Count);
                foreach (SearchResult<int> result in results)
                    Assert.Contains(result.Id, allowed);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_WithNullFilter_SameAsNoFilter()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);
                for (int i = 0; i < 10; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: 99);
                IReadOnlyList<SearchResult<int>> unfiltered = await store.FindMostSimilarAsync(query, 5);
                IReadOnlyList<SearchResult<int>> nullFilter = await store.FindMostSimilarAsync(query, 5, filter: null);

                Assert.Equal(unfiltered.Count, nullFilter.Count);
                for (int i = 0; i < unfiltered.Count; i++)
                {
                    Assert.Equal(unfiltered[i].Id, nullFilter[i].Id);
                    Assert.Equal(unfiltered[i].Score, nullFilter[i].Score);
                }
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_WithFilterExcludingAll_ReturnsEmpty()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);
                for (int i = 0; i < 10; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 10, _ => false);

                Assert.Empty(results);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_CountAppliesAfterFiltering()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);
                for (int i = 0; i < 20; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                HashSet<int> allowed = new HashSet<int> { 1, 3, 5, 7, 9, 11 };
                IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 3, allowed.Contains);

                Assert.Equal(3, results.Count);
                foreach (SearchResult<int> result in results)
                    Assert.Contains(result.Id, allowed);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_SkipsDeletedKeys()
        {
            string filePath = GetTempFilePath();
            try
            {
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);
                for (int i = 0; i < 10; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                // Delete a key the filter would otherwise allow.
                await store.RemoveAsync(5);

                HashSet<int> allowed = new HashSet<int> { 2, 5, 7 };
                IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 10, allowed.Contains);

                Assert.Equal(2, results.Count);
                Assert.DoesNotContain(results, result => result.Id == 5);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_WorksInParallelPath()
        {
            string filePath = GetTempFilePath();
            try
            {
                int vectorCount = DiskVectorStore<int>.ParallelThreshold + 1;
                using DiskVectorStore<int> store = new DiskVectorStore<int>(DefaultName, filePath, DefaultDimension);
                for (int i = 0; i < vectorCount; i++)
                {
                    await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
                }

                HashSet<int> allowed = new HashSet<int>(Enumerable.Range(0, vectorCount).Where(i => i % 7 == 0));
                IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(
                    TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 10, allowed.Contains);

                Assert.Equal(10, results.Count);
                foreach (SearchResult<int> result in results)
                    Assert.Contains(result.Id, allowed);
            }
            finally
            {
                CleanupFile(filePath);
            }
        }

        #endregion
    }
}
