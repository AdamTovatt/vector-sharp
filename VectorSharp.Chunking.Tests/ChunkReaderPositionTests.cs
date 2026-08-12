namespace VectorSharp.Chunking.Tests
{
    public class ChunkReaderPositionTests
    {
        private const string MarkdownSample = "# Title\n\nFirst paragraph with text. More sentences here.\n\n## Section\n\n- item one\n- item two\n\nFinal paragraph.\n";

        private static ChunkReaderOptions MarkdownOptions(int maxTokens) => new ChunkReaderOptions
        {
            MaxTokensPerChunk = maxTokens,
            BreakStrings = BreakStrings.Markdown,
            StopSignals = StopSignals.Markdown
        };

        /// <summary>
        /// Asserts the two properties that make offsets trustworthy: the chunks concatenate back
        /// into the input exactly, and every chunk's offsets point at that chunk's own text in
        /// the original. The <see cref="TextChunk.EndOffset"/> checks are stated as the contract
        /// the type documents — it meets the next chunk's start, and the last one meets the end
        /// of the input — rather than by repeating the property's own definition.
        /// </summary>
        private static void AssertLosslessWithOffsets(string original, List<TextChunk> chunks)
        {
            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));

            long expectedOffset = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                TextChunk chunk = chunks[i];

                Assert.Equal(expectedOffset, chunk.StartOffset);
                Assert.Equal(chunk.Text, original.Substring((int)chunk.StartOffset, chunk.Text.Length));

                if (i + 1 < chunks.Count)
                    Assert.Equal(chunks[i + 1].StartOffset, chunk.EndOffset);

                expectedOffset += chunk.Text.Length;
            }

            Assert.Equal(original.Length, expectedOffset);

            if (chunks.Count > 0)
                Assert.Equal(original.Length, chunks[^1].EndOffset);
        }

        #region Create (reader types)

        [Fact]
        public async Task Create_StringReader_ReadsChunks()
        {
            using StringReader stringReader = new StringReader("hello world");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 10, BreakStrings = ["\n"], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Single(chunks);
            Assert.Equal("hello world", chunks[0].Text);
        }

        [Fact]
        public async Task Create_StringReader_MatchesStreamReaderOutput()
        {
            using StringReader stringReader = new StringReader(MarkdownSample);
            List<TextChunk> fromString = await ReadAllTextChunks(
                ChunkReader.Create(stringReader, TokenCounter.CountWords, MarkdownOptions(10)));

            using StreamReader streamReader = ReaderFrom(MarkdownSample);
            List<TextChunk> fromStream = await ReadAllTextChunks(
                ChunkReader.Create(streamReader, TokenCounter.CountWords, MarkdownOptions(10)));

            Assert.Equal(fromStream.Select(chunk => chunk.Text), fromString.Select(chunk => chunk.Text));
            Assert.Equal(fromStream.Select(chunk => chunk.StartOffset), fromString.Select(chunk => chunk.StartOffset));
        }

        #endregion

        #region ReadChunksAsync (basic behavior)

        [Fact]
        public async Task ReadChunksAsync_EmptyInput_YieldsNoChunks()
        {
            using StringReader stringReader = new StringReader("");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords);

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task ReadChunksAsync_FirstChunk_StartsAtZero()
        {
            using StringReader stringReader = new StringReader("one. two. three. four. five. six.");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 2, BreakStrings = [". "], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.NotEmpty(chunks);
            Assert.Equal(0, chunks[0].StartOffset);
        }

        [Fact]
        public async Task ReadChunksAsync_YieldsSameTextAsReadAllAsync()
        {
            string original = "# Main Title\n\nIntro paragraph with multiple sentences. This is the second one! Is this a question?\n\n## Sub Section\n\n1. First item\n- Second item\n+ Third item\n\n```\ncode block\n```\n\n### Deeper\n\nFinal text.\n";

            using StringReader chunkSource = new StringReader(original);
            List<TextChunk> chunks = await ReadAllTextChunks(
                ChunkReader.Create(chunkSource, TokenCounter.CountWords, MarkdownOptions(5)));

            using StringReader stringSource = new StringReader(original);
            List<string> strings = await ReadAllChunks(
                ChunkReader.Create(stringSource, TokenCounter.CountWords, MarkdownOptions(5)));

            Assert.Equal(strings, chunks.Select(chunk => chunk.Text));
        }

        #endregion

        #region ReadChunksAsync (single pass)

        [Fact]
        public async Task ReadChunksAsync_SecondEnumeration_Throws()
        {
            using StringReader stringReader = new StringReader("one. two. three. four. five. six.");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 2, BreakStrings = [". "], StopSignals = [] });

            await ReadAllTextChunks(reader);

            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAllTextChunks(reader));
        }

        [Fact]
        public async Task ReadChunksAsync_AbandonedThenReEnumerated_Throws()
        {
            // The abandoned pass has already consumed a segment it never yielded. Continuing
            // would drop that text and mislabel everything after it, so the second pass is
            // refused rather than left to produce chunks that reconstruct nothing.
            using StringReader stringReader = new StringReader("aa bb cc\ndd ee ff\ngg hh ii\njj kk ll\n");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = ["\n"], StopSignals = [] });

            await foreach (TextChunk chunk in reader.ReadChunksAsync())
            {
                break;
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAllTextChunks(reader));
        }

        [Fact]
        public async Task ReadAllAsync_AfterReadChunksAsync_Throws()
        {
            using StringReader stringReader = new StringReader("one. two. three. four.");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 2, BreakStrings = [". "], StopSignals = [] });

            await ReadAllTextChunks(reader);

            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAllChunks(reader));
        }

        #endregion

        #region ReadChunksAsync (lossless reconstruction and offsets)

        [Fact]
        public async Task ReadChunksAsync_MarkdownContent_OffsetsAreRunningSumOfLengths()
        {
            using StringReader stringReader = new StringReader(MarkdownSample);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords, MarkdownOptions(5));

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(5, chunks.Count);
            AssertLosslessWithOffsets(MarkdownSample, chunks);
        }

        [Fact]
        public async Task ReadChunksAsync_CSharpContent_OffsetsAreRunningSumOfLengths()
        {
            string original = "namespace Foo\n{\n    public class Bar\n    {\n        /// <summary>\n        /// Does something.\n        /// </summary>\n        public void Method()\n        {\n            Console.WriteLine(\"hello\");\n        }\n    }\n}\n";
            using StringReader stringReader = new StringReader(original);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions
                {
                    MaxTokensPerChunk = 5,
                    BreakStrings = BreakStrings.CSharp,
                    StopSignals = StopSignals.CSharp
                });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(5, chunks.Count);
            AssertLosslessWithOffsets(original, chunks);
        }

        [Fact]
        public async Task ReadChunksAsync_OversizedSegment_OffsetsStayCorrect()
        {
            // Neither line contains a break point, so each is emitted whole and over the limit,
            // which is the branch that yields before reading its replacement segment.
            string original = "one two three four five six seven eight nine ten\neleven twelve thirteen fourteen\n";
            using StringReader stringReader = new StringReader(original);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = ["\n"], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(2, chunks.Count);
            AssertLosslessWithOffsets(original, chunks);

            // The type documents that an unbreakable segment comes back over the limit rather
            // than being cut, so the counts here are above MaxTokensPerChunk by design.
            Assert.Equal(10, chunks[0].TokenCount);
            Assert.Equal(4, chunks[1].TokenCount);
        }

        [Fact]
        public async Task ReadChunksAsync_StopSignals_OffsetsStayCorrect()
        {
            string original = "intro text\n# Heading\nmore text\n# Second Heading\ntail text";
            using StringReader stringReader = new StringReader(original);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 100, BreakStrings = ["\n"], StopSignals = ["# "] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(3, chunks.Count);
            AssertLosslessWithOffsets(original, chunks);
        }

        [Fact]
        public async Task ReadChunksAsync_LongInput_OffsetsStayCorrectAcrossManyChunks()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < 200; i++)
            {
                builder.Append($"Sentence number {i} of the document. ");
            }
            string original = builder.ToString();

            using StringReader stringReader = new StringReader(original);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 12, BreakStrings = [". "], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(100, chunks.Count);
            AssertLosslessWithOffsets(original, chunks);
        }

        [Fact]
        public async Task ReadChunksAsync_NonAsciiInput_OffsetsAreUtf16CodeUnits()
        {
            // Accented letters, an astral-plane emoji (one surrogate pair, two code units) and a
            // ZWJ sequence: the offsets are documented as UTF-16 code units, so they have to
            // track string.Length rather than characters a reader would count by eye.
            string original = "café ☕ naïve\n👨‍👩‍👧 family emoji 🎉\nlast line here\n";
            using StringReader stringReader = new StringReader(original);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = ["\n"], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(3, chunks.Count);
            AssertLosslessWithOffsets(original, chunks);

            // The second chunk starts after "café ☕ naïve\n", which is 13 characters but the
            // emoji in it costs nothing extra — the pair in the second line is what would show
            // up as a discrepancy if offsets counted anything other than code units.
            Assert.Equal(13, chunks[1].StartOffset);
            Assert.Equal(original.IndexOf("last line here", StringComparison.Ordinal), chunks[2].StartOffset);
        }

        #endregion

        #region ReadChunksAsync (token counts)

        [Fact]
        public async Task ReadChunksAsync_TokenCount_IsTheCountOfTheChunkText()
        {
            using StringReader stringReader = new StringReader("one two three. four five six. seven eight nine.");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = [". "], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Equal(3, chunks.Count);
            Assert.Equal(3, chunks[0].TokenCount);
            Assert.Equal(3, chunks[1].TokenCount);
            Assert.Equal(3, chunks[2].TokenCount);
        }

        [Fact]
        public async Task ReadChunksAsync_TokenCount_MatchesTheSuppliedCounter()
        {
            using StringReader stringReader = new StringReader(MarkdownSample);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords, MarkdownOptions(10));

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.NotEmpty(chunks);
            foreach (TextChunk chunk in chunks)
            {
                Assert.Equal(TokenCounter.CountWords(chunk.Text), chunk.TokenCount);
            }
        }

        [Fact]
        public async Task ReadChunksAsync_MergedChunks_CountTheTokenCounterOnceEachTime()
        {
            // The counter can be an actual tokenizer, so a chunk that grows by one segment must
            // carry the count it already computed forward rather than recounting the same text.
            int calls = 0;
            int CountingCounter(string text)
            {
                calls++;
                return TokenCounter.CountWords(text);
            }

            using StringReader stringReader = new StringReader("a. b. c. d. e. f. g. h. i. j.");
            ChunkReader reader = ChunkReader.Create(stringReader, CountingCounter,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = [". "], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            // The input holds 10 segments, and each one is counted exactly once: either when it
            // starts a chunk or when it is merged into the chunk being built. Nothing is counted
            // twice, so the total lands on the segment count rather than above it.
            Assert.Equal(4, chunks.Count);
            Assert.Equal(10, calls);
        }

        #endregion

        #region ReadChunksAsync (cancellation)

        [Fact]
        public async Task ReadChunksAsync_CancelledToken_ThrowsOperationCancelled()
        {
            using StringReader stringReader = new StringReader("hello world this is some text\nmore text here\n");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 2, BreakStrings = ["\n"], StopSignals = [] });
            CancellationToken cancelled = new CancellationToken(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (TextChunk chunk in reader.ReadChunksAsync(cancelled))
                {
                    // Should not reach here
                }
            });
        }

        #endregion
    }
}
