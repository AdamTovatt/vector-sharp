namespace VectorSharp.Storage.Tests
{
    [TestClass]
    public class SearchResultTests
    {
        [TestMethod]
        public void Constructor_SetsAllProperties()
        {
            Guid id = Guid.NewGuid();
            SearchResult<Guid> result = new SearchResult<Guid> { Id = id, Score = 0.95f, StoreName = "test-store" };

            Assert.AreEqual(id, result.Id);
            Assert.AreEqual(0.95f, result.Score);
            Assert.AreEqual("test-store", result.StoreName);
        }

        [TestMethod]
        public void SearchResult_WithIntKey_SetsAllProperties()
        {
            SearchResult<int> result = new SearchResult<int> { Id = 42, Score = 0.8f, StoreName = "int-store" };

            Assert.AreEqual(42, result.Id);
            Assert.AreEqual(0.8f, result.Score);
            Assert.AreEqual("int-store", result.StoreName);
        }

        [TestMethod]
        public void SearchResult_WithLongKey_SetsAllProperties()
        {
            SearchResult<long> result = new SearchResult<long> { Id = 123456789L, Score = 0.5f, StoreName = "long-store" };

            Assert.AreEqual(123456789L, result.Id);
            Assert.AreEqual(0.5f, result.Score);
            Assert.AreEqual("long-store", result.StoreName);
        }
    }
}
