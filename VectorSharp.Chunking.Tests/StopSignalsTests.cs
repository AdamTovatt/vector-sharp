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

        [Fact]
        public void PlainText_IsEmpty()
        {
            Assert.Empty(StopSignals.PlainText);
        }

        [Fact]
        public void JavaScript_HasExpectedCount()
        {
            Assert.Single(StopSignals.JavaScript);
        }

        [Fact]
        public void JavaScript_AllNonEmpty()
        {
            Assert.All(StopSignals.JavaScript, signal => Assert.False(string.IsNullOrEmpty(signal)));
        }

        [Fact]
        public void Html_HasExpectedCount()
        {
            Assert.Equal(3, StopSignals.Html.Count);
        }

        [Fact]
        public void Html_AllNonEmpty()
        {
            Assert.All(StopSignals.Html, signal => Assert.False(string.IsNullOrEmpty(signal)));
        }

        [Fact]
        public void Css_HasExpectedCount()
        {
            Assert.Equal(4, StopSignals.Css.Count);
        }

        [Fact]
        public void Css_AllNonEmpty()
        {
            Assert.All(StopSignals.Css, signal => Assert.False(string.IsNullOrEmpty(signal)));
        }

        [Fact]
        public void Python_HasExpectedCount()
        {
            Assert.Equal(3, StopSignals.Python.Count);
        }

        [Fact]
        public void Python_AllNonEmpty()
        {
            Assert.All(StopSignals.Python, signal => Assert.False(string.IsNullOrEmpty(signal)));
        }
    }
}
