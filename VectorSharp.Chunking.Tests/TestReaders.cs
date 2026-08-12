using System.Text;

namespace VectorSharp.Chunking.Tests
{
    /// <summary>
    /// Shared fixtures for the chunking tests: readers over in-memory text, a configured
    /// <see cref="ChunkReader"/>, and a drain for each of its two enumerations.
    /// Imported as a global static in the test project, so the members are callable unqualified
    /// the way the per-file copies they replaced were.
    /// </summary>
    internal static class TestReaders
    {
        /// <summary>
        /// A short document exercising every Markdown break string: headings, paragraphs and a
        /// list. Shared because both chunk-reader test files read the same sample.
        /// </summary>
        internal const string MarkdownSample = "# Title\n\nFirst paragraph with text. More sentences here.\n\n## Section\n\n- item one\n- item two\n\nFinal paragraph.\n";

        internal static ChunkReaderOptions MarkdownOptions(int maxTokens) => new ChunkReaderOptions
        {
            MaxTokensPerChunk = maxTokens,
            BreakStrings = BreakStrings.Markdown,
            StopSignals = StopSignals.Markdown
        };

        /// <summary>
        /// Wraps text in a <see cref="StreamReader"/>, which keeps a real stream and its decoding
        /// in the picture. Tests specifically about which reader type is accepted use the
        /// <see cref="StringReader"/> directly instead.
        /// </summary>
        internal static StreamReader ReaderFrom(string text)
        {
            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return new StreamReader(stream);
        }

        /// <param name="countTokens">The token counter to chunk by. Defaults to counting words,
        /// which is what all but the token-bound tests want; those pass a counter that reaches the
        /// limit at a position they can state exactly.</param>
        internal static ChunkReader CreateReader(string input, IReadOnlyList<string> breakStrings, IReadOnlyList<string> stopSignals, int maxTokens = 10, Func<string, int>? countTokens = null)
        {
            StreamReader streamReader = ReaderFrom(input);
            return ChunkReader.Create(streamReader, countTokens ?? TokenCounter.CountWords, new ChunkReaderOptions
            {
                MaxTokensPerChunk = maxTokens,
                BreakStrings = breakStrings,
                StopSignals = stopSignals
            });
        }

        internal static async Task<List<string>> ReadAllChunks(ChunkReader reader)
        {
            List<string> chunks = new List<string>();
            await foreach (string chunk in reader.ReadAllAsync())
            {
                chunks.Add(chunk);
            }
            return chunks;
        }

        internal static async Task<List<TextChunk>> ReadAllTextChunks(ChunkReader reader)
        {
            List<TextChunk> chunks = new List<TextChunk>();
            await foreach (TextChunk chunk in reader.ReadChunksAsync())
            {
                chunks.Add(chunk);
            }
            return chunks;
        }
    }
}
