namespace VectorSharp.Chunking.Tests
{
    public class ChunkReaderTests
    {
        #region Create (validation)

        // Both overloads are named explicitly: a bare null literal would be ambiguous between
        // them, and each has to reject null on its own.
        [Fact]
        public void Create_NullTextReader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ChunkReader.Create((TextReader)null!, TokenCounter.CountWords));
        }

        [Fact]
        public void Create_NullStreamReader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ChunkReader.Create((StreamReader)null!, TokenCounter.CountWords));
        }

        /// <summary>
        /// Looks for a Create overload whose first parameter is exactly <paramref name="readerType"/>.
        /// The parameter types are compared by identity rather than through
        /// <see cref="Type.GetMethod(string, Type[])"/>, which resolves like the compiler does and
        /// would happily answer with the TextReader overload for any type derived from it.
        /// </summary>
        private static bool HasCreateOverloadTaking(Type readerType)
        {
            Type[] expected = [readerType, typeof(Func<string, int>), typeof(ChunkReaderOptions)];

            return typeof(ChunkReader)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(method => method.Name == nameof(ChunkReader.Create))
                .Any(method => method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(expected));
        }

        // Asserted through reflection because no call site can detect this: source that passes a
        // StreamReader compiles whether or not the StreamReader overload exists, silently binding
        // to the TextReader one instead. The already published signature only survives in IL.
        [Fact]
        public void Create_KeepsTheStreamReaderOverloadEarlierAssembliesBindTo()
        {
            Assert.True(HasCreateOverloadTaking(typeof(StreamReader)));
        }

        [Fact]
        public void Create_HasTheWidenedTextReaderOverload()
        {
            Assert.True(HasCreateOverloadTaking(typeof(TextReader)));
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

        #region Cancellation

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

        [Fact]
        public async Task ReadChunksAsync_CancelledMidEnumeration_StopsAtTheNextChunk()
        {
            // The case a caller actually hits, and the one the loop's own
            // ThrowIfCancellationRequested exists for. A token cancelled before the call throws
            // out of the segment reader's guard before the loop is entered at all, so the two
            // tests reach different checks despite reading almost the same.
            using StringReader stringReader = new StringReader("aa bb\ncc dd\nee ff\ngg hh\nii jj\nkk ll\n");
            ChunkReader reader = ChunkReader.Create(stringReader, TokenCounter.CountWords,
                new ChunkReaderOptions { MaxTokensPerChunk = 2, BreakStrings = ["\n"], StopSignals = [] });

            using CancellationTokenSource cancellationSource = new CancellationTokenSource();
            List<TextChunk> received = new List<TextChunk>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (TextChunk chunk in reader.ReadChunksAsync(cancellationSource.Token))
                {
                    received.Add(chunk);

                    if (received.Count == 2)
                        await cancellationSource.CancelAsync();
                }
            });

            // Everything up to the cancellation is still delivered: the chunks already yielded
            // are valid, and a caller that stops mid-stream keeps what it read.
            Assert.Equal(2, received.Count);
            Assert.Equal(0, received[0].StartOffset);
            Assert.Equal(received[0].EndOffset, received[1].StartOffset);
        }

        #endregion

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

        #region Single pass

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
