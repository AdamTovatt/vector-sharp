namespace VectorSharp.Chunking.Tests
{
    public class StopSignalsTests
    {
        [Fact]
        public void Markdown_HasExpectedCount()
        {
            Assert.Equal(8, StopSignals.Markdown.Count);
        }

        [Fact]
        public void Markdown_AllNonEmpty()
        {
            Assert.All(StopSignals.Markdown, signal => Assert.False(string.IsNullOrEmpty(signal)));
        }

        [Fact]
        public void CSharp_HasExpectedCount()
        {
            Assert.Single(StopSignals.CSharp);
        }

        [Fact]
        public void CSharp_AllNonEmpty()
        {
            Assert.All(StopSignals.CSharp, signal => Assert.False(string.IsNullOrEmpty(signal)));
        }
    }
}
