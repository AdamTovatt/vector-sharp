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

        // Both defaults are quoted as fact in the XML docs, the README and the package
        // description, so they are pinned here as literals rather than left to drift.
        [Fact]
        public void DefaultMaxTextsPerBatch_IsSixtyFour()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions();

            Assert.Equal(64, options.MaxTextsPerBatch);
        }

        [Fact]
        public void DefaultMaxCharactersPerBatch_IsOneHundredThousand()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions();

            Assert.Equal(100000, options.MaxCharactersPerBatch);
        }

        [Fact]
        public void MaxTextsPerBatch_CustomValue_IsRetained()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { MaxTextsPerBatch = 8 };

            Assert.Equal(8, options.MaxTextsPerBatch);
        }

        [Fact]
        public void MaxCharactersPerBatch_CustomValue_IsRetained()
        {
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { MaxCharactersPerBatch = 2048 };

            Assert.Equal(2048, options.MaxCharactersPerBatch);
        }

        [Fact]
        public void Constructor_ZeroMaxTextsPerBatch_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { MaxTextsPerBatch = 0 }));
        }

        [Fact]
        public void Constructor_ZeroMaxCharactersPerBatch_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { MaxCharactersPerBatch = 0 }));
        }
    }
}
