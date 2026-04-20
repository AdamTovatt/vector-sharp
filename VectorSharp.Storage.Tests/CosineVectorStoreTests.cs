namespace VectorSharp.Storage.Tests
{
    public class CosineVectorStoreTests
    {
        private const int DefaultDimension = 128;
        private const string DefaultName = "test-store";

        #region Constructor and Validation

        [Fact]
        public void Constructor_PositiveDimension_Succeeds()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, 768);

            Assert.Equal(768, store.Dimension);
            Assert.Equal(DefaultName, store.Name);
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void Constructor_ZeroDimension_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CosineVectorStore<Guid>(DefaultName, 0));
        }

        [Fact]
        public void Constructor_NegativeDimension_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CosineVectorStore<Guid>(DefaultName, -1));
        }

        [Fact]
        public void Constructor_NullName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CosineVectorStore<Guid>(null!, 128));
        }

        [Fact]
        public async Task AddAsync_DimensionMismatch_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, 128);
            float[] wrongDimension = new float[64];

            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.AddAsync(Guid.NewGuid(), wrongDimension));
        }

        [Fact]
        public async Task AddAsync_NullValues_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, 128);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                store.AddAsync(Guid.NewGuid(), null!));
        }

        [Fact]
        public async Task FindMostSimilarAsync_DimensionMismatch_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, 128);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(128));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.FindMostSimilarAsync(new float[64], 1));
        }

        #endregion

        #region Basic Operations

        [Fact]
        public async Task AddAsync_IncreasesCount()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            Assert.Equal(1, store.Count);
        }

        [Fact]
        public async Task RemoveAsync_ExistingId_ReturnsTrue()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            Guid id = Guid.NewGuid();
            await store.AddAsync(id, TestHelpers.CreateRandomVector(DefaultDimension));

            bool removed = await store.RemoveAsync(id);

            Assert.True(removed);
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public async Task RemoveAsync_NonExistentId_ReturnsFalse()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            bool removed = await store.RemoveAsync(Guid.NewGuid());

            Assert.False(removed);
            Assert.Equal(1, store.Count);
        }

        [Fact]
        public async Task FindMostSimilarAsync_EmptyStore_ReturnsEmpty()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                TestHelpers.CreateRandomVector(DefaultDimension), 10);

            Assert.Empty(results);
        }

        [Fact]
        public async Task FindMostSimilarAsync_ReturnsTopMatch()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            float[] targetVector = TestHelpers.CreateVector(1.0f, DefaultDimension);
            Guid targetId = Guid.NewGuid();

            await store.AddAsync(targetId, targetVector);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateVector(-1.0f, DefaultDimension));

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(targetVector, 1);

            Assert.Single(results);
            Assert.Equal(targetId, results[0].Id);
            TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
        }

        [Fact]
        public async Task FindMostSimilarAsync_RespectsCount()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            for (int i = 0; i < 10; i++)
            {
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
            }

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 3);

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public async Task FindMostSimilarAsync_RequestingMoreThanStored_ReturnsAll()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 100);

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task FindMostSimilarAsync_NullQuery_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                store.FindMostSimilarAsync(null!, 10));
        }

        [Fact]
        public async Task FindMostSimilarAsync_ZeroCount_ReturnsEmpty()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                TestHelpers.CreateRandomVector(DefaultDimension), 0);

            Assert.Empty(results);
        }

        [Fact]
        public async Task FindMostSimilarAsync_ZeroVector_ReturnsEmpty()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            float[] zeroVector = new float[DefaultDimension];
            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(zeroVector, 10);

            Assert.Empty(results);
        }

        #endregion

        #region Sorted Results

        [Fact]
        public async Task FindMostSimilarAsync_ReturnsSortedDescending()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);

            // Add vectors with known similarity relationships
            float[] query = TestHelpers.CreateVector(1.0f, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateVector(1.0f, DefaultDimension));   // identical = 1.0
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateVector(-1.0f, DefaultDimension));  // opposite = -1.0
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 1)); // somewhere in between

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(query, 3);

            Assert.Equal(3, results.Count);
            for (int i = 1; i < results.Count; i++)
            {
                Assert.True(results[i - 1].Score >= results[i].Score,
                    $"Results not sorted: index {i - 1} has score {results[i - 1].Score}, index {i} has score {results[i].Score}");
            }
        }

        [Fact]
        public async Task FindMostSimilarAsync_ResultsContainStoreName()
        {
            string storeName = "my-embeddings";
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(storeName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(
                TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 1);

            Assert.Single(results);
            Assert.Equal(storeName, results[0].StoreName);
        }

        #endregion

        #region Generic Key Types

        [Fact]
        public async Task Store_WithIntKey_WorksCorrectly()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
            float[] vector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);

            await store.AddAsync(42, vector);
            IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(vector, 1);

            Assert.Single(results);
            Assert.Equal(42, results[0].Id);
        }

        [Fact]
        public async Task Store_WithLongKey_WorksCorrectly()
        {
            using CosineVectorStore<long> store = new CosineVectorStore<long>(DefaultName, DefaultDimension);
            float[] vector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);

            await store.AddAsync(123456789L, vector);
            IReadOnlyList<SearchResult<long>> results = await store.FindMostSimilarAsync(vector, 1);

            Assert.Single(results);
            Assert.Equal(123456789L, results[0].Id);
        }

        [Fact]
        public async Task Store_RemoveWithIntKey_WorksCorrectly()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
            await store.AddAsync(1, TestHelpers.CreateRandomVector(DefaultDimension, seed: 1));
            await store.AddAsync(2, TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));

            bool removed = await store.RemoveAsync(1);

            Assert.True(removed);
            Assert.Equal(1, store.Count);
        }

        #endregion

        #region Dimension Variations

        [Fact]
        public async Task Store_SmallDimension64_WorksCorrectly()
        {
            await VerifyDimensionWorks(64);
        }

        [Fact]
        public async Task Store_MediumDimension256_WorksCorrectly()
        {
            await VerifyDimensionWorks(256);
        }

        [Fact]
        public async Task Store_Dimension768_WorksCorrectly()
        {
            await VerifyDimensionWorks(768);
        }

        [Fact]
        public async Task Store_Dimension1024_WorksCorrectly()
        {
            await VerifyDimensionWorks(1024);
        }

        [Fact]
        public async Task Store_Dimension1536_WorksCorrectly()
        {
            await VerifyDimensionWorks(1536);
        }

        [Fact]
        public async Task Store_LargeDimension2048_WorksCorrectly()
        {
            await VerifyDimensionWorks(2048);
        }

        [Fact]
        public async Task Store_NonSIMDAlignedDimension_WorksCorrectly()
        {
            await VerifyDimensionWorks(13);
        }

        [Fact]
        public async Task Store_NonSIMDAlignedDimension127_WorksCorrectly()
        {
            await VerifyDimensionWorks(127);
        }

        #endregion

        #region Thread Safety

        [Fact]
        public async Task ConcurrentAddRemove_IsThreadSafe()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
            int addCount = 100;

            // Add vectors concurrently
            Task[] addTasks = new Task[addCount];
            for (int i = 0; i < addCount; i++)
            {
                int id = i;
                addTasks[i] = store.AddAsync(id, TestHelpers.CreateRandomVector(DefaultDimension, seed: id));
            }

            await Task.WhenAll(addTasks);
            Assert.Equal(addCount, store.Count);

            // Remove half concurrently
            Task<bool>[] removeTasks = new Task<bool>[addCount / 2];
            for (int i = 0; i < addCount / 2; i++)
            {
                removeTasks[i] = store.RemoveAsync(i);
            }

            bool[] removeResults = await Task.WhenAll(removeTasks);
            Assert.True(removeResults.All(r => r));
            Assert.Equal(addCount / 2, store.Count);
        }

        [Fact]
        public async Task ConcurrentSearch_IsThreadSafe()
        {
            using CosineVectorStore<int> store = await TestHelpers.CreatePopulatedIntStoreAsync(
                DefaultName, DefaultDimension, 100);

            // Search concurrently
            Task<IReadOnlyList<SearchResult<int>>>[] searchTasks =
                new Task<IReadOnlyList<SearchResult<int>>>[20];

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

        #endregion

        #region Cosine Similarity Correctness

        [Fact]
        public async Task FindMostSimilarAsync_IdenticalVector_ReturnsScoreOfOne()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            float[] vector = TestHelpers.CreateRandomVector(DefaultDimension, seed: 42);
            Guid id = Guid.NewGuid();
            await store.AddAsync(id, vector);

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(vector, 1);

            Assert.Single(results);
            Assert.Equal(id, results[0].Id);
            TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
        }

        [Fact]
        public async Task FindMostSimilarAsync_OppositeVector_ReturnsNegativeScore()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            float[] vector = TestHelpers.CreateVector(1.0f, DefaultDimension);
            float[] opposite = TestHelpers.CreateVector(-1.0f, DefaultDimension);

            await store.AddAsync(Guid.NewGuid(), opposite);

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(vector, 1);

            Assert.Single(results);
            TestHelpers.AssertApproximatelyEqual(-1.0f, results[0].Score, 0.0001f);
        }

        #endregion

        #region Filter

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_OnlyReturnsAllowedIds()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
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

        [Fact]
        public async Task FindMostSimilarAsync_WithNullFilter_SameAsNoFilter()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
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

        [Fact]
        public async Task FindMostSimilarAsync_WithFilterExcludingAll_ReturnsEmpty()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
            for (int i = 0; i < 10; i++)
            {
                await store.AddAsync(i, TestHelpers.CreateRandomVector(DefaultDimension, seed: i));
            }

            IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(
                TestHelpers.CreateRandomVector(DefaultDimension, seed: 99), 10, _ => false);

            Assert.Empty(results);
        }

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_CountAppliesAfterFiltering()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
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

        [Fact]
        public async Task FindMostSimilarAsync_WithFilter_WorksInParallelPath()
        {
            int vectorCount = CosineVectorStore<int>.ParallelThreshold + 1;
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
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

        #endregion

        #region Helpers

        private static async Task VerifyDimensionWorks(int dimension)
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>("test", dimension);
            float[] vector = TestHelpers.CreateRandomVector(dimension, seed: 42);
            Guid id = Guid.NewGuid();

            await store.AddAsync(id, vector);

            // Add some other vectors
            for (int i = 0; i < 5; i++)
            {
                await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(dimension, seed: i));
            }

            IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(vector, 1);

            Assert.Single(results);
            Assert.Equal(id, results[0].Id);
            TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
        }

        #endregion
    }
}
