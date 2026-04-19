namespace VectorSharp.Storage.Tests
{
    [TestClass]
    public class VectorSearchTests
    {
        private const int DefaultDimension = 128;

        [TestMethod]
        public async Task SearchAsync_SingleStore_ReturnsSameAsDirectSearch()
        {
            using CosineVectorStore<Guid> store = await TestHelpers.CreatePopulatedStoreAsync("store-a", DefaultDimension, 20);
            float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: 999);

            IReadOnlyList<SearchResult<Guid>> directResults = await store.FindMostSimilarAsync(query, 5);
            IReadOnlyList<SearchResult<Guid>> searchResults = await VectorSearch.SearchAsync(query, 5, store);

            Assert.AreEqual(directResults.Count, searchResults.Count);
            for (int i = 0; i < directResults.Count; i++)
            {
                Assert.AreEqual(directResults[i].Id, searchResults[i].Id);
                Assert.AreEqual(directResults[i].Score, searchResults[i].Score, 0.0001f);
            }
        }

        [TestMethod]
        public async Task SearchAsync_MultipleStores_MergesByScore()
        {
            using CosineVectorStore<Guid> storeA = new CosineVectorStore<Guid>("store-a", DefaultDimension);
            using CosineVectorStore<Guid> storeB = new CosineVectorStore<Guid>("store-b", DefaultDimension);

            // Put the best match in store B
            float[] query = TestHelpers.CreateVector(1.0f, DefaultDimension);
            Guid bestId = Guid.NewGuid();
            await storeB.AddAsync(bestId, TestHelpers.CreateVector(1.0f, DefaultDimension));

            // Put some other vectors in store A
            await storeA.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 1));
            await storeA.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));

            IReadOnlyList<SearchResult<Guid>> results = await VectorSearch.SearchAsync(query, 3, storeA, storeB);

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(bestId, results[0].Id);
            Assert.AreEqual("store-b", results[0].StoreName);
        }

        [TestMethod]
        public async Task SearchAsync_RespectsCount()
        {
            using CosineVectorStore<Guid> storeA = await TestHelpers.CreatePopulatedStoreAsync("store-a", DefaultDimension, 10);
            using CosineVectorStore<Guid> storeB = await TestHelpers.CreatePopulatedStoreAsync("store-b", DefaultDimension, 10, seed: 99);

            float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: 500);
            IReadOnlyList<SearchResult<Guid>> results = await VectorSearch.SearchAsync(query, 5, storeA, storeB);

            Assert.AreEqual(5, results.Count);
        }

        [TestMethod]
        public async Task SearchAsync_ResultsAreSortedDescending()
        {
            using CosineVectorStore<Guid> storeA = await TestHelpers.CreatePopulatedStoreAsync("store-a", DefaultDimension, 20);
            using CosineVectorStore<Guid> storeB = await TestHelpers.CreatePopulatedStoreAsync("store-b", DefaultDimension, 20, seed: 99);

            float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: 500);
            IReadOnlyList<SearchResult<Guid>> results = await VectorSearch.SearchAsync(query, 10, storeA, storeB);

            for (int i = 1; i < results.Count; i++)
            {
                Assert.IsTrue(results[i - 1].Score >= results[i].Score,
                    $"Results not sorted at index {i}: {results[i - 1].Score} vs {results[i].Score}");
            }
        }

        [TestMethod]
        public async Task SearchAsync_ResultsIncludeCorrectStoreName()
        {
            using CosineVectorStore<Guid> storeA = new CosineVectorStore<Guid>("alpha", DefaultDimension);
            using CosineVectorStore<Guid> storeB = new CosineVectorStore<Guid>("beta", DefaultDimension);

            await storeA.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 1));
            await storeB.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));

            float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: 500);
            IReadOnlyList<SearchResult<Guid>> results = await VectorSearch.SearchAsync(query, 10, storeA, storeB);

            Assert.AreEqual(2, results.Count);
            HashSet<string> storeNames = new HashSet<string>(results.Select(r => r.StoreName));
            Assert.IsTrue(storeNames.Contains("alpha"));
            Assert.IsTrue(storeNames.Contains("beta"));
        }

        [TestMethod]
        public async Task SearchAsync_EmptyStores_ReturnsEmpty()
        {
            using CosineVectorStore<Guid> storeA = new CosineVectorStore<Guid>("store-a", DefaultDimension);
            using CosineVectorStore<Guid> storeB = new CosineVectorStore<Guid>("store-b", DefaultDimension);

            float[] query = TestHelpers.CreateRandomVector(DefaultDimension);
            IReadOnlyList<SearchResult<Guid>> results = await VectorSearch.SearchAsync(query, 10, storeA, storeB);

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public async Task SearchAsync_ZeroCount_Throws()
        {
            using CosineVectorStore<Guid> store = await TestHelpers.CreatePopulatedStoreAsync("store", DefaultDimension, 10);

            float[] query = TestHelpers.CreateRandomVector(DefaultDimension);

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                VectorSearch.SearchAsync(query, 0, store));
        }

        [TestMethod]
        public async Task SearchAsync_NullQuery_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>("store", DefaultDimension);

            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
                VectorSearch.SearchAsync<Guid>(null!, 10, store));
        }

        [TestMethod]
        public async Task SearchAsync_NoStores_Throws()
        {
            float[] query = TestHelpers.CreateRandomVector(DefaultDimension);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                VectorSearch.SearchAsync<Guid>(query, 10));
        }

        [TestMethod]
        public async Task SearchAsync_MixedInMemoryAndDisk_WorksTogether()
        {
            string filePath = Path.Combine(Path.GetTempPath(), $"vectorsharp_test_{Guid.NewGuid()}.dat");
            try
            {
                using CosineVectorStore<int> memStore = new CosineVectorStore<int>("memory", DefaultDimension);
                using DiskVectorStore<int> diskStore = new DiskVectorStore<int>("disk", filePath, DefaultDimension);

                float[] bestVector = TestHelpers.CreateVector(1.0f, DefaultDimension);
                await memStore.AddAsync(1, bestVector);
                await diskStore.AddAsync(2, TestHelpers.CreateRandomVector(DefaultDimension, seed: 1));

                IReadOnlyList<SearchResult<int>> results = await VectorSearch.SearchAsync(
                    bestVector, 2, memStore, diskStore);

                Assert.AreEqual(2, results.Count);
                Assert.AreEqual(1, results[0].Id); // best match is in memory store
                Assert.AreEqual("memory", results[0].StoreName);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
    }
}
