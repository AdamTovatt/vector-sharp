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

        [Fact]
        public void PlainText_HasExpectedCount()
        {
            Assert.Equal(5, BreakStrings.PlainText.Count);
        }

        [Fact]
        public void PlainText_AllNonEmpty()
        {
            Assert.All(BreakStrings.PlainText, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }

        [Fact]
        public void JavaScript_HasExpectedCount()
        {
            Assert.Equal(5, BreakStrings.JavaScript.Count);
        }

        [Fact]
        public void JavaScript_AllNonEmpty()
        {
            Assert.All(BreakStrings.JavaScript, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }

        [Fact]
        public void Html_HasExpectedCount()
        {
            Assert.Equal(3, BreakStrings.Html.Count);
        }

        [Fact]
        public void Html_AllNonEmpty()
        {
            Assert.All(BreakStrings.Html, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }

        [Fact]
        public void Css_HasExpectedCount()
        {
            Assert.Equal(4, BreakStrings.Css.Count);
        }

        [Fact]
        public void Css_AllNonEmpty()
        {
            Assert.All(BreakStrings.Css, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }

        [Fact]
        public void Python_HasExpectedCount()
        {
            Assert.Equal(3, BreakStrings.Python.Count);
        }

        [Fact]
        public void Python_AllNonEmpty()
        {
            Assert.All(BreakStrings.Python, breakString => Assert.False(string.IsNullOrEmpty(breakString)));
        }
    }
}
