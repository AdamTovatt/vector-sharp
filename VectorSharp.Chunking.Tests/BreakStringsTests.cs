namespace VectorSharp.Chunking.Tests
{
    public class BreakStringsTests
    {
        [Fact]
        public void Markdown_HasExpectedCount()
        {
            Assert.Equal(16, BreakStrings.Markdown.Count);
        }

        [Fact]
        public void Markdown_AllNonEmpty()
        {
            Assert.All(BreakStrings.Markdown, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }

        [Fact]
        public void CSharp_HasExpectedCount()
        {
            Assert.Equal(5, BreakStrings.CSharp.Count);
        }

        [Fact]
        public void CSharp_AllNonEmpty()
        {
            Assert.All(BreakStrings.CSharp, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }
    }
}
