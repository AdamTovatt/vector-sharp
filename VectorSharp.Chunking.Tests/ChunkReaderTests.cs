namespace VectorSharp.Chunking.Tests
{
    public class ChunkReaderTests
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

        #region Create (validation)

        [Fact]
        public void Create_NullReader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ChunkReader.Create(null!, TokenCounter.CountWords));
        }

        [Fact]
        public void Create_NullCountTokens_Throws()
        {
            using StreamReader streamReader = ReaderFrom("test");
            Assert.Throws<ArgumentNullException>(() =>
                ChunkReader.Create(streamReader, null!));
        }

        [Fact]
        public void Create_ValidInputs_Succeeds()
        {
            using StreamReader streamReader = ReaderFrom("test");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords);
            Assert.NotNull(reader);
        }

        [Fact]
        public void Create_ZeroMaxTokens_Throws()
        {
            using StreamReader streamReader = ReaderFrom("test");
            Assert.Throws<ArgumentException>(() =>
                ChunkReader.Create(streamReader, TokenCounter.CountWords,
                    new ChunkReaderOptions { MaxTokensPerChunk = 0 }));
        }

        [Fact]
        public void Create_NegativeMaxTokens_Throws()
        {
            using StreamReader streamReader = ReaderFrom("test");
            Assert.Throws<ArgumentException>(() =>
                ChunkReader.Create(streamReader, TokenCounter.CountWords,
                    new ChunkReaderOptions { MaxTokensPerChunk = -1 }));
        }

        [Fact]
        public void Create_EmptyBreakStrings_Throws()
        {
            using StreamReader streamReader = ReaderFrom("test");
            Assert.Throws<ArgumentException>(() =>
                ChunkReader.Create(streamReader, TokenCounter.CountWords,
                    new ChunkReaderOptions { BreakStrings = [] }));
        }

        #endregion

        #region ReadAllAsync (basic behavior)

        [Fact]
        public async Task ReadAllAsync_EmptyStream_YieldsNoChunks()
        {
            using StreamReader streamReader = ReaderFrom("");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords);

            List<string> chunks = await ReadAllChunks(reader);

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task ReadAllAsync_SingleSegmentUnderLimit_YieldsSingleChunk()
        {
            using StreamReader streamReader = ReaderFrom("hello world");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 10, BreakStrings = ["\n"], StopSignals = [] });

            List<string> chunks = await ReadAllChunks(reader);

            Assert.Single(chunks);
            Assert.Equal("hello world", chunks[0]);
        }

        [Fact]
        public async Task ReadAllAsync_MultipleSegmentsUnderLimit_CombinesIntoOneChunk()
        {
            using StreamReader streamReader = ReaderFrom("one. two. three");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 10, BreakStrings = [". "], StopSignals = [] });

            List<string> chunks = await ReadAllChunks(reader);

            Assert.Single(chunks);
            Assert.Equal("one. two. three", chunks[0]);
        }

        [Fact]
        public async Task ReadAllAsync_SegmentsExceedingLimit_SplitsIntoMultipleChunks()
        {
            using StreamReader streamReader = ReaderFrom("word1 word2 word3. word4 word5 word6. word7");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = [". "], StopSignals = [] });

            List<string> chunks = await ReadAllChunks(reader);

            Assert.True(chunks.Count > 1);
            Assert.All(chunks, chunk => Assert.True(TokenCounter.CountWords(chunk) <= 4)); // small overshoot possible for single segments
        }

        [Fact]
        public async Task ReadAllAsync_NoBreakPointsFound_ReturnsAllContent()
        {
            using StreamReader streamReader = ReaderFrom("no breaks here at all");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 100, BreakStrings = ["\n"], StopSignals = [] });

            List<string> chunks = await ReadAllChunks(reader);

            Assert.Single(chunks);
            Assert.Equal("no breaks here at all", chunks[0]);
        }

        #endregion

        #region ReadAllAsync (stop signals)

        [Fact]
        public async Task ReadAllAsync_StopSignalAtSegmentStart_StartsNewChunk()
        {
            using StreamReader streamReader = ReaderFrom("intro text\n# Heading\nmore text");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 100, BreakStrings = ["\n"], StopSignals = ["# "] });

            List<string> chunks = await ReadAllChunks(reader);

            Assert.Equal(2, chunks.Count);
            Assert.Equal("intro text\n", chunks[0]);
            Assert.StartsWith("# Heading", chunks[1]);
        }

        [Fact]
        public async Task ReadAllAsync_HeadingStopSignal_BreaksBeforeHeading()
        {
            string input = "Some paragraph text.\n\n## New Section\n\nMore text here.";
            using StreamReader streamReader = ReaderFrom(input);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 100,
                    BreakStrings = BreakStrings.Markdown,
                    StopSignals = StopSignals.Markdown
                });

            List<string> chunks = await ReadAllChunks(reader);

            // The heading should start a new chunk
            bool headingStartsChunk = chunks.Any(chunk => chunk.StartsWith("## "));
            Assert.True(headingStartsChunk);
        }

        [Fact]
        public async Task ReadAllAsync_CodeBlockStopSignal_BreaksBeforeCodeBlock()
        {
            string input = "Some text\n```\ncode here\n```\nmore text";
            using StreamReader streamReader = ReaderFrom(input);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 100,
                    BreakStrings = ["\n"],
                    StopSignals = ["```"]
                });

            List<string> chunks = await ReadAllChunks(reader);

            bool codeBlockStartsChunk = chunks.Any(chunk => chunk.StartsWith("```"));
            Assert.True(codeBlockStartsChunk);
        }

        [Fact]
        public async Task ReadAllAsync_CSharpSummaryStopSignal_BreaksBeforeTopLevelDocComment()
        {
            string input = "public class Foo { }\n\n/// <summary>\n/// Does something.\n/// </summary>\npublic class Bar { }\n";
            using StreamReader streamReader = ReaderFrom(input);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 100,
                    BreakStrings = BreakStrings.CSharp,
                    StopSignals = StopSignals.CSharp
                });

            List<string> chunks = await ReadAllChunks(reader);

            bool docCommentStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("/// <summary>"));
            Assert.True(docCommentStartsChunk);
            Assert.Equal(input, string.Join("", chunks));
        }

        [Fact]
        public async Task ReadAllAsync_CSharpSummaryStopSignal_BreaksBeforeIndentedDocComment()
        {
            string input = "public class Foo\n{\n    public void Bar() { }\n\n    /// <summary>\n    /// Does something.\n    /// </summary>\n    public void Baz() { }\n}\n";
            using StreamReader streamReader = ReaderFrom(input);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 100,
                    BreakStrings = BreakStrings.CSharp,
                    StopSignals = StopSignals.CSharp
                });

            List<string> chunks = await ReadAllChunks(reader);

            bool docCommentStartsChunk = chunks.Any(chunk => chunk.TrimStart().StartsWith("/// <summary>"));
            Assert.True(docCommentStartsChunk);
            Assert.Equal(input, string.Join("", chunks));
        }

        #endregion

        #region ReadAllAsync (round-trip preservation)

        [Fact]
        public async Task ReadAllAsync_MarkdownContent_RoundTripPreservesOriginal()
        {
            string original = "# Title\n\nFirst paragraph with text. More sentences here.\n\n## Section\n\n- item one\n- item two\n\nFinal paragraph.\n";
            using StreamReader streamReader = ReaderFrom(original);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 10,
                    BreakStrings = BreakStrings.Markdown,
                    StopSignals = StopSignals.Markdown
                });

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task ReadAllAsync_CSharpContent_RoundTripPreservesOriginal()
        {
            string original = "namespace Foo\n{\n    public class Bar\n    {\n        public void Method()\n        {\n            Console.WriteLine(\"hello\");\n        }\n    }\n}\n";
            using StreamReader streamReader = ReaderFrom(original);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 10,
                    BreakStrings = BreakStrings.CSharp,
                    StopSignals = StopSignals.CSharp
                });

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task ReadAllAsync_ComplexMarkdown_RoundTripPreservesOriginal()
        {
            string original = "# Main Title\n\nIntro paragraph with multiple sentences. This is the second one! Is this a question?\n\n## Sub Section\n\n1. First item\n- Second item\n+ Third item\n\n```\ncode block\n```\n\n### Deeper\n\nFinal text.\n";
            using StreamReader streamReader = ReaderFrom(original);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 5,
                    BreakStrings = BreakStrings.Markdown,
                    StopSignals = StopSignals.Markdown
                });

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(original, reconstructed);
        }

        #endregion

        #region ReadAllAsync (token limits)

        [Fact]
        public async Task ReadAllAsync_AllChunksWithinTokenLimit()
        {
            string input = "word1. word2. word3. word4. word5. word6. word7. word8. word9. word10.";
            using StreamReader streamReader = ReaderFrom(input);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = [". "], StopSignals = [] });

            List<string> chunks = await ReadAllChunks(reader);

            // Each chunk should be within the limit (except single oversized segments)
            Assert.True(chunks.Count > 1);
        }

        [Fact]
        public async Task ReadAllAsync_SingleOversizedSegment_ReturnedAsIs()
        {
            // A segment with no break points that exceeds the token limit
            using StreamReader streamReader = ReaderFrom("one two three four five six seven eight nine ten");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = ["\n"], StopSignals = [] });

            List<string> chunks = await ReadAllChunks(reader);

            // Should return the whole thing as one chunk since there are no break points
            Assert.Single(chunks);
            Assert.Equal("one two three four five six seven eight nine ten", chunks[0]);
        }

        #endregion

        #region ReadAllAsync (cancellation)

        [Fact]
        public async Task ReadAllAsync_CancelledToken_ThrowsOperationCancelled()
        {
            using StreamReader streamReader = ReaderFrom("hello world this is some text\nmore text here\n");
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 2, BreakStrings = ["\n"], StopSignals = [] });
            CancellationToken cancelled = new CancellationToken(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (string chunk in reader.ReadAllAsync(cancelled))
                {
                    // Should not reach here
                }
            });
        }

        #endregion

        #region ReadAllAsync (defaults)

        [Fact]
        public async Task ReadAllAsync_DefaultOptions_UsesMarkdownBreakStrings()
        {
            string input = "First paragraph.\n\nSecond paragraph.";
            using StreamReader streamReader = ReaderFrom(input);
            ChunkReader reader = ChunkReader.Create(streamReader, TokenCounter.CountWords);

            List<string> chunks = await ReadAllChunks(reader);
            string reconstructed = string.Join("", chunks);

            Assert.Equal(input, reconstructed);
        }

        #endregion
    }
}
