namespace VectorSharp.Storage.Tests
{
    public class CosineVectorStorePersistenceTests
    {
        private const int DefaultDimension = 128;
        private const string DefaultName = "test-store";

        [Fact]
        public async Task SaveAndLoad_RoundTrip_PreservesVectors()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            Guid id1 = Guid.NewGuid();
            Guid id2 = Guid.NewGuid();
            float[] values1 = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);
            float[] values2 = TestHelpers.CreateRandomVector(DefaultDimension, seed: 2);

            await store.AddAsync(id1, values1);
            await store.AddAsync(id2, values2);

            using MemoryStream stream = new MemoryStream();
            await store.SaveAsync(stream);

            stream.Position = 0;
            using CosineVectorStore<Guid> loadedStore = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await loadedStore.LoadAsync(stream);

            Assert.Equal(2, loadedStore.Count);

            // Verify search still works
            IReadOnlyList<SearchResult<Guid>> results = await loadedStore.FindMostSimilarAsync(values1, 1);
            Assert.Single(results);
            Assert.Equal(id1, results[0].Id);
            TestHelpers.AssertApproximatelyEqual(1.0f, results[0].Score, 0.0001f);
        }

        [Fact]
        public async Task SaveAndLoad_SameQueryResults()
        {
            using CosineVectorStore<Guid> store = await TestHelpers.CreatePopulatedStoreAsync(DefaultName, DefaultDimension, 50);
            float[] query = TestHelpers.CreateRandomVector(DefaultDimension, seed: 999);

            IReadOnlyList<SearchResult<Guid>> originalResults = await store.FindMostSimilarAsync(query, 5);

            using MemoryStream stream = new MemoryStream();
            await store.SaveAsync(stream);

            stream.Position = 0;
            using CosineVectorStore<Guid> loadedStore = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await loadedStore.LoadAsync(stream);

            IReadOnlyList<SearchResult<Guid>> loadedResults = await loadedStore.FindMostSimilarAsync(query, 5);

            Assert.Equal(originalResults.Count, loadedResults.Count);
            for (int i = 0; i < originalResults.Count; i++)
            {
                Assert.Equal(originalResults[i].Id, loadedResults[i].Id);
                TestHelpers.AssertApproximatelyEqual(originalResults[i].Score, loadedResults[i].Score, 0.0001f);
            }
        }

        [Fact]
        public async Task Load_ClearsExistingVectors()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension));

            using MemoryStream stream = new MemoryStream();
            await store.SaveAsync(stream);

            // Add more vectors after saving
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 2));
            await store.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(DefaultDimension, seed: 3));
            Assert.Equal(3, store.Count);

            // Load should replace existing vectors
            stream.Position = 0;
            await store.LoadAsync(stream);
            Assert.Equal(1, store.Count);
        }

        [Fact]
        public async Task Load_DimensionMismatch_Throws()
        {
            using CosineVectorStore<Guid> store128 = new CosineVectorStore<Guid>(DefaultName, 128);
            await store128.AddAsync(Guid.NewGuid(), TestHelpers.CreateRandomVector(128));

            using MemoryStream stream = new MemoryStream();
            await store128.SaveAsync(stream);

            stream.Position = 0;
            using CosineVectorStore<Guid> store256 = new CosineVectorStore<Guid>(DefaultName, 256);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store256.LoadAsync(stream));
        }

        [Fact]
        public async Task SaveAsync_NullStream_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                store.SaveAsync(null!));
        }

        [Fact]
        public async Task LoadAsync_NullStream_Throws()
        {
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                store.LoadAsync(null!));
        }

        [Fact]
        public async Task SaveAndLoad_WithIntKeys()
        {
            using CosineVectorStore<int> store = new CosineVectorStore<int>(DefaultName, DefaultDimension);
            float[] values = TestHelpers.CreateRandomVector(DefaultDimension, seed: 1);
            await store.AddAsync(42, values);

            using MemoryStream stream = new MemoryStream();
            await store.SaveAsync(stream);

            stream.Position = 0;
            using CosineVectorStore<int> loadedStore = new CosineVectorStore<int>(DefaultName, DefaultDimension);
            await loadedStore.LoadAsync(stream);

            Assert.Equal(1, loadedStore.Count);
            IReadOnlyList<SearchResult<int>> results = await loadedStore.FindMostSimilarAsync(values, 1);
            Assert.Equal(42, results[0].Id);
        }

        [Fact]
        public async Task SaveAndLoad_LargeDataset()
        {
            int vectorCount = 500;
            using CosineVectorStore<Guid> store = await TestHelpers.CreatePopulatedStoreAsync(
                DefaultName, DefaultDimension, vectorCount);

            using MemoryStream stream = new MemoryStream();
            await store.SaveAsync(stream);

            stream.Position = 0;
            using CosineVectorStore<Guid> loadedStore = new CosineVectorStore<Guid>(DefaultName, DefaultDimension);
            await loadedStore.LoadAsync(stream);

            Assert.Equal(vectorCount, loadedStore.Count);
        }
    }
}
