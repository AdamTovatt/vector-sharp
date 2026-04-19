namespace VectorSharp.Storage.Tests
{
    public class SearchResultTests
    {
        [Fact]
        public void Constructor_SetsAllProperties()
        {
            Guid id = Guid.NewGuid();
            SearchResult<Guid> result = new SearchResult<Guid> { Id = id, Score = 0.95f, StoreName = "test-store" };

            Assert.Equal(id, result.Id);
            Assert.Equal(0.95f, result.Score);
            Assert.Equal("test-store", result.StoreName);
        }

        [Fact]
        public void SearchResult_WithIntKey_SetsAllProperties()
        {
            SearchResult<int> result = new SearchResult<int> { Id = 42, Score = 0.8f, StoreName = "int-store" };

            Assert.Equal(42, result.Id);
            Assert.Equal(0.8f, result.Score);
            Assert.Equal("int-store", result.StoreName);
        }

        [Fact]
        public void SearchResult_WithLongKey_SetsAllProperties()
        {
            SearchResult<long> result = new SearchResult<long> { Id = 123456789L, Score = 0.5f, StoreName = "long-store" };

            Assert.Equal(123456789L, result.Id);
            Assert.Equal(0.5f, result.Score);
            Assert.Equal("long-store", result.StoreName);
        }
    }
}
