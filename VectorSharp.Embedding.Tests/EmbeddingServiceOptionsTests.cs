namespace VectorSharp.Embedding.Tests
{
    [TestClass]
    public class EmbeddingServiceOptionsTests
    {
        [TestMethod]
        public void DefaultConcurrency_IsOne()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions();

            Assert.AreEqual(1, options.Concurrency);
        }

        [TestMethod]
        public void DefaultChannelCapacity_IsOneThousand()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions();

            Assert.AreEqual(1000, options.ChannelCapacity);
        }

        [TestMethod]
        public void Concurrency_CustomValue_IsRetained()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { Concurrency = 4 };

            Assert.AreEqual(4, options.Concurrency);
        }

        [TestMethod]
        public void ChannelCapacity_CustomValue_IsRetained()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { ChannelCapacity = 500 };

            Assert.AreEqual(500, options.ChannelCapacity);
        }
    }
}
