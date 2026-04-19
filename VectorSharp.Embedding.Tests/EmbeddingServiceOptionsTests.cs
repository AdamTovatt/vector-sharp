namespace VectorSharp.Embedding.Tests
{
    public class EmbeddingServiceOptionsTests
    {
        [Fact]
        public void DefaultConcurrency_IsOne()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions();

            Assert.Equal(1, options.Concurrency);
        }

        [Fact]
        public void DefaultChannelCapacity_IsOneThousand()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions();

            Assert.Equal(1000, options.ChannelCapacity);
        }

        [Fact]
        public void Concurrency_CustomValue_IsRetained()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { Concurrency = 4 };

            Assert.Equal(4, options.Concurrency);
        }

        [Fact]
        public void ChannelCapacity_CustomValue_IsRetained()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { ChannelCapacity = 500 };

            Assert.Equal(500, options.ChannelCapacity);
        }
    }
}
