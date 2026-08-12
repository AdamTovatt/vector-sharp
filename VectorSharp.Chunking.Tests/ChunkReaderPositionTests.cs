using System.Text;

namespace VectorSharp.Chunking.Tests
{
    /// <summary>
    /// What <see cref="ChunkReader.ReadChunksAsync"/> adds over <see cref="ChunkReader.ReadAllAsync"/>:
    /// each chunk's position in the input and its token count. Everything the two enumerations
    /// share — which readers Create accepts, the single-pass rule, cancellation — is in
    /// <see cref="ChunkReaderTests"/>, since it is not about position.
    /// </summary>
    public class ChunkReaderPositionTests
    {
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
        public async Task ReadChunksAsync_NoChunks_YieldsNullFromFirstOrDefaultRatherThanAnEmptyChunk()
        {
            // Why TextChunk is a class: as a struct this would hand back a chunk whose Text is
            // null despite being non-nullable, and whose EndOffset throws. Asserted through
            // behaviour so that changing the type back reddens a test rather than passing quietly.
            using StringReader stringReader = new StringReader("");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords);

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.Null(chunks.FirstOrDefault());
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
        public async Task ReadChunksAsync_SegmentCutAtTheLimit_OffsetsStayCorrect()
        {
            // Neither line contains a break point, so each arrives over the limit and is cut. The
            // offsets have to describe the pieces, not the segments they were cut from — a cut
            // that left every piece carrying its segment's offset would still reconstruct the
            // input and still point at the wrong place in it.
            string original = "one two three four five six seven eight nine ten\neleven twelve thirteen fourteen\n";
            using StringReader stringReader = new StringReader(original);
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 3, BreakStrings = ["\n"], StopSignals = [] });

            List<TextChunk> chunks = await ReadAllTextChunks(reader);

            Assert.True(chunks.Count > 2);
            AssertLosslessWithOffsets(original, chunks);
            Assert.All(chunks, chunk => Assert.True(chunk.TokenCount <= 3));
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
            StringBuilder builder = new StringBuilder();
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
            // Four, so that the longest line fits and no line is cut. What offsets do across a cut
            // is ChunkReaderTokenBoundTests' subject; this one is about the unit they are in.
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 4, BreakStrings = ["\n"], StopSignals = [] });

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

    }
}
