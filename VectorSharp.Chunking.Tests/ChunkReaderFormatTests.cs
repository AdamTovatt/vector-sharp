namespace VectorSharp.Chunking.Tests
{
    public class ChunkReaderFormatTests
    {
        private static StreamReader ReaderFrom(string text)
        {
            MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            return new StreamReader(stream);
        }

        private static async Task<List<string>> ReadAllChunks(ChunkReader reader)
        {
            List<string> chunks = new List<string>();
            await foreach (string chunk in reader.ReadAllAsync())
            {
                chunks.Add(chunk);
            }
            return chunks;
        }

        private static ChunkReader CreateReader(string input, IReadOnlyList<string> breakStrings, IReadOnlyList<string> stopSignals, int maxTokens = 10)
        {
            StreamReader streamReader = ReaderFrom(input);
            return ChunkReader.Create(streamReader, TokenCounter.CountWords, new ChunkReaderOptions
            {
                MaxTokensPerChunk = maxTokens,
                BreakStrings = breakStrings,
                StopSignals = stopSignals
            });
        }

        #region PlainText

        [Fact]
        public async Task PlainText_RoundTripPreservesOriginal()
        {
            string original = "First paragraph with a sentence. And another sentence.\n\nSecond paragraph here. Final sentence!\n";
            ChunkReader reader = CreateReader(original, BreakStrings.PlainText, StopSignals.PlainText, maxTokens: 5);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task PlainText_ProducesMultipleChunksForLongInput()
        {
            string original = "Sentence one. Sentence two. Sentence three. Sentence four. Sentence five. Sentence six.";
            ChunkReader reader = CreateReader(original, BreakStrings.PlainText, StopSignals.PlainText, maxTokens: 3);

            List<string> chunks = await ReadAllChunks(reader);

            Assert.True(chunks.Count > 1);
            Assert.Equal(original, string.Join("", chunks));
        }

        #endregion

        #region JavaScript / TypeScript / JSX / TSX

        [Fact]
        public async Task JavaScript_RoundTripPreservesOriginal()
        {
            string original = "export function add(a, b) {\n    return a + b;\n}\n\nexport function sub(a, b) {\n    return a - b;\n}\n";
            ChunkReader reader = CreateReader(original, BreakStrings.JavaScript, StopSignals.JavaScript, maxTokens: 10);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task TypeScript_RoundTripPreservesOriginal()
        {
            string original = "interface User {\n    id: number;\n    name: string;\n}\n\nfunction greet(user: User): string {\n    return `hello ${user.name}`;\n}\n";
            ChunkReader reader = CreateReader(original, BreakStrings.JavaScript, StopSignals.JavaScript, maxTokens: 10);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task Tsx_RoundTripPreservesOriginal()
        {
            string original = "export function Button({ label }: { label: string }) {\n    return <button>{label}</button>;\n}\n";
            ChunkReader reader = CreateReader(original, BreakStrings.JavaScript, StopSignals.JavaScript, maxTokens: 10);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task JavaScript_JsDocStopSignal_BreaksBeforeJsDoc()
        {
            string original = "function one() { return 1; }\n\n/**\n * Returns two.\n */\nfunction two() { return 2; }\n";
            ChunkReader reader = CreateReader(original, BreakStrings.JavaScript, StopSignals.JavaScript, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool jsDocStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("/**"));
            Assert.True(jsDocStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        #endregion

        #region HTML

        [Fact]
        public async Task Html_RoundTripPreservesOriginal()
        {
            string original = "<!DOCTYPE html>\n<html>\n<body>\n<h1>Title</h1>\n<p>First paragraph with text. Second sentence.</p>\n<h2>Section</h2>\n<p>More content.</p>\n</body>\n</html>\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Html, StopSignals.Html, maxTokens: 10);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task Html_HeadingStopSignal_BreaksBeforeHeading()
        {
            string original = "<p>Intro paragraph with some text.</p>\n<h1>Main Title</h1>\n<p>Body content.</p>\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Html, StopSignals.Html, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool headingStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("<h1"));
            Assert.True(headingStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        #endregion

        #region CSS

        [Fact]
        public async Task Css_RoundTripPreservesOriginal()
        {
            string original = ".container {\n    display: flex;\n    padding: 10px;\n}\n\n.item {\n    color: red;\n    margin: 5px;\n}\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Css, StopSignals.Css, maxTokens: 10);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task Css_MediaStopSignal_BreaksBeforeMediaRule()
        {
            string original = ".base {\n    color: black;\n}\n\n@media (max-width: 600px) {\n    .base {\n        color: red;\n    }\n}\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Css, StopSignals.Css, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool mediaStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("@media"));
            Assert.True(mediaStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        [Fact]
        public async Task Css_KeyframesStopSignal_BreaksBeforeKeyframes()
        {
            string original = ".box {\n    opacity: 1;\n}\n\n@keyframes fade {\n    from { opacity: 0; }\n    to { opacity: 1; }\n}\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Css, StopSignals.Css, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool keyframesStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("@keyframes"));
            Assert.True(keyframesStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        #endregion

        #region Python

        [Fact]
        public async Task Python_RoundTripPreservesOriginal()
        {
            string original = "def add(a, b):\n    return a + b\n\n\ndef sub(a, b):\n    return a - b\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Python, StopSignals.Python, maxTokens: 10);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task Python_DefStopSignal_BreaksBeforeFunction()
        {
            string original = "x = 1\ny = 2\nz = 3\n\ndef helper():\n    return 42\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Python, StopSignals.Python, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool defStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("def "));
            Assert.True(defStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        [Fact]
        public async Task Python_ClassStopSignal_BreaksBeforeClass()
        {
            string original = "import os\nimport sys\n\nclass MyClass:\n    def method(self):\n        return None\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Python, StopSignals.Python, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool classStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("class "));
            Assert.True(classStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        [Fact]
        public async Task Python_AsyncDefStopSignal_BreaksBeforeAsyncFunction()
        {
            string original = "x = 1\n\nasync def fetch():\n    return await something()\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Python, StopSignals.Python, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            bool asyncDefStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("async def "));
            Assert.True(asyncDefStartsChunk);
            Assert.Equal(original, string.Join("", chunks));
        }

        [Fact]
        public async Task Python_IndentedDefStopSignal_BreaksBeforeMethod()
        {
            string original = "class Foo:\n    def __init__(self):\n        self.x = 1\n\n    def other(self):\n        return self.x\n";
            ChunkReader reader = CreateReader(original, BreakStrings.Python, StopSignals.Python, maxTokens: 100);

            List<string> chunks = await ReadAllChunks(reader);

            int defStartingChunks = chunks.Count(chunk => chunk.TrimStart().StartsWith("def "));
            Assert.True(defStartingChunks >= 1);
            Assert.Equal(original, string.Join("", chunks));
        }

        #endregion
    }
}
