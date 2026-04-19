namespace VectorSharp.Chunking.Tests
{
    public class ChunkReaderOptionsTests
    {
        [Fact]
        public void DefaultMaxTokensPerChunk_IsThreeHundred()
        {
            ChunkReaderOptions options = new ChunkReaderOptions();
            Assert.Equal(300, options.MaxTokensPerChunk);
        }

        [Fact]
        public void DefaultBreakStrings_IsMarkdown()
        {
            ChunkReaderOptions options = new ChunkReaderOptions();
            Assert.Same(BreakStrings.Markdown, options.BreakStrings);
        }

        [Fact]
        public void DefaultStopSignals_IsMarkdown()
        {
            ChunkReaderOptions options = new ChunkReaderOptions();
            Assert.Same(StopSignals.Markdown, options.StopSignals);
        }

        [Fact]
        public void MaxTokensPerChunk_CustomValue_IsRetained()
        {
            ChunkReaderOptions options = new ChunkReaderOptions { MaxTokensPerChunk = 500 };
            Assert.Equal(500, options.MaxTokensPerChunk);
        }

        [Fact]
        public void BreakStrings_CustomValue_IsRetained()
        {
            ChunkReaderOptions options = new ChunkReaderOptions { BreakStrings = BreakStrings.CSharp };
            Assert.Same(BreakStrings.CSharp, options.BreakStrings);
        }

        [Fact]
        public void StopSignals_CustomValue_IsRetained()
        {
            ChunkReaderOptions options = new ChunkReaderOptions { StopSignals = StopSignals.CSharp };
            Assert.Same(StopSignals.CSharp, options.StopSignals);
        }
    }
}
