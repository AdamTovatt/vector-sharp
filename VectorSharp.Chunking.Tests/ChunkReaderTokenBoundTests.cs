namespace VectorSharp.Chunking.Tests
{
    /// <summary>
    /// <see cref="ChunkReaderOptions.MaxTokensPerChunk"/> as a bound rather than a target: text
    /// running past the limit with no break point in it is cut, and the two guarantees that make
    /// offsets usable have to survive the cut.
    /// </summary>
    public class ChunkReaderTokenBoundTests
    {
        /// <summary>
        /// Text with no break point anywhere in it, long enough to need cutting many times over.
        /// Joined with spaces: the break strings below are newlines only, so this is one segment
        /// however long it gets, while still being many tokens to a word-counting tokenizer.
        /// </summary>
        private static string UnbreakableText(int words)
        {
            return string.Join(" ", Enumerable.Range(0, words).Select(i => $"word{i}"));
        }

        private static ChunkReader ReaderOver(string input, int maxTokens, Func<string, int>? countTokens = null)
        {
            return CreateReader(input, breakStrings: ["\n"], stopSignals: [], maxTokens, countTokens);
        }

        #region The bound holds

        [Fact]
        public async Task ReadChunksAsync_TextWithNoBreakPoint_EveryChunkIsWithinTheLimit()
        {
            string original = UnbreakableText(500);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 10));

            // Pinned rather than "more than one": one chunk per character also satisfies "more
            // than one", and that is exactly the degenerate output a broken search produces.
            Assert.Equal(50, chunks.Count);
            Assert.All(chunks, chunk => Assert.True(
                TokenCounter.CountWords(chunk.Text) <= 10,
                $"chunk at {chunk.StartOffset} counts {TokenCounter.CountWords(chunk.Text)} tokens against a limit of 10."));
        }

        [Fact]
        public async Task ReadChunksAsync_TextWithNoBreakPoint_CutsAtTheLimitNotBelowIt()
        {
            // A bound that held by cutting after every character would satisfy the assertion above
            // and be useless. With one token per character and a limit of 10, every chunk but the
            // last has to be exactly 10 characters.
            string original = new string('x', 95);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 10, TokenCounter.CountCharacters));

            Assert.Equal(10, chunks.Count);
            Assert.All(chunks.Take(9), chunk => Assert.Equal(10, chunk.Text.Length));
            Assert.Equal(5, chunks[^1].Text.Length);
        }

        [Fact]
        public async Task ReadChunksAsync_TokenCountIsCountedForThePieceNotDividedOutOfTheWhole()
        {
            string original = new string('x', 95);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 10, TokenCounter.CountCharacters));

            Assert.All(chunks, chunk => Assert.Equal(TokenCounter.CountCharacters(chunk.Text), chunk.TokenCount));
        }

        [Fact]
        public async Task ReadChunksAsync_SegmentExactlyAtTheLimit_IsNotCut()
        {
            // The boundary between "full" and "over". A segment exactly at the limit is already
            // correct, and cutting it would change output for text that never needed it.
            string original = new string('x', 10);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 10, TokenCounter.CountCharacters));

            Assert.Single(chunks);
            Assert.Equal(original, chunks[0].Text);
        }

        [Fact]
        public async Task ReadChunksAsync_SegmentsWithinTheLimit_AreNotTouchedByTheCuttingPath()
        {
            // Text whose segments already fit chunks by accumulation alone. Cutting must not reach
            // it: the search stops at the token limit, so text put through it needlessly comes
            // back in pieces the size of the limit instead of the size its break strings implied.
            string original = "alpha beta\ngamma delta\nepsilon zeta\n";

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 100));

            Assert.Single(chunks);
            Assert.Equal(original, chunks[0].Text);
        }

        #endregion

        #region What the cut must not break

        [Fact]
        public async Task ReadChunksAsync_CutText_StillReconstructsExactly()
        {
            string original = UnbreakableText(500);

            // Seven is simply well under the word count of the text, so the segment is cut many
            // times rather than once — a guarantee that held only for a single cut would not be
            // one.
            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 7));

            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
        }

        [Fact]
        public async Task ReadChunksAsync_CutText_OffsetsPointAtEachPiecesOwnText()
        {
            string original = UnbreakableText(500);

            // Seven is simply well under the word count of the text, so the segment is cut many
            // times rather than once — a guarantee that held only for a single cut would not be
            // one.
            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 7));

            long expectedOffset = 0;
            foreach (TextChunk chunk in chunks)
            {
                Assert.Equal(expectedOffset, chunk.StartOffset);
                Assert.Equal(chunk.Text, original.Substring((int)chunk.StartOffset, chunk.Text.Length));
                expectedOffset = chunk.EndOffset;
            }

            Assert.Equal(original.Length, expectedOffset);
        }

        [Fact]
        public async Task ReadChunksAsync_CutTextFollowedByMore_OffsetsStayCorrectAcrossTheJoin()
        {
            // A cut segment, then ordinary segments after it. The reader's own cursor has to stay
            // in step with text that was consumed as one segment and emitted as several.
            //
            // Nine, so that the two tails together are under the limit and would merge into one
            // chunk if the reader carried on normally after the cut. A cursor left pointing at the
            // end of the segment rather than the end of the last piece shows up as an offset gap
            // right at that join.
            string original = UnbreakableText(200) + "\nshort tail\nanother tail\n";

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 9));

            long expectedOffset = 0;
            foreach (TextChunk chunk in chunks)
            {
                Assert.Equal(expectedOffset, chunk.StartOffset);
                expectedOffset = chunk.EndOffset;
            }

            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
            Assert.Equal(original.Length, expectedOffset);
        }

        [Fact]
        public async Task ReadChunksAsync_CutText_NeverYieldsAnEmptyChunk()
        {
            string original = UnbreakableText(300);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 5));

            Assert.All(chunks, chunk => Assert.NotEmpty(chunk.Text));
        }

        [Theory]
        [InlineData(5, 100)]
        [InlineData(6, 67)]
        [InlineData(7, 67)]
        public async Task ReadChunksAsync_TextWithSurrogatePairs_NeverCutsAPairInHalf(int maxTokens, int expectedChunks)
        {
            // Each emoji is two UTF-16 code units that are one character. A cut landing between
            // them produces two chunks that are not text, and a lone surrogate is what reaches the
            // caller's tokenizer and their embedding API.
            //
            // The odd limits are the ones that matter, and are why this is a theory. With one
            // token per code unit, an odd limit puts the largest prefix that fits exactly halfway
            // through a pair, so the cut has to be pulled back deliberately. An even limit lands
            // between pairs on its own and would hold however the search was written.
            string original = string.Concat(Enumerable.Repeat("\U0001F600", 200));

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens, TokenCounter.CountCharacters));

            // Pinned per case: at an odd limit every chunk is one code unit shorter than the limit
            // allows, because the cut has to step back off the pair, so the counts differ between
            // 5 and 6 by more than the limits do.
            Assert.Equal(expectedChunks, chunks.Count);
            Assert.All(chunks, chunk =>
            {
                Assert.False(char.IsLowSurrogate(chunk.Text[0]),
                    $"a chunk of {chunk.Text.Length} code units starts with the second half of a surrogate pair.");
                Assert.False(char.IsHighSurrogate(chunk.Text[^1]),
                    $"a chunk of {chunk.Text.Length} code units ends with the first half of a surrogate pair.");
            });
            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
        }

        [Fact]
        public async Task ReadChunksAsync_CancelledWhileCuttingOneSegment_StopsWithoutFinishingIt()
        {
            // Cutting one long segment can be hundreds of pieces and hundreds of calls into the
            // caller's tokenizer, all inside a single pass of the reader's own loop. A token
            // checked only by that loop is not checked again until the whole segment is done, so a
            // caller who has stopped waiting waits for all of it.
            string original = new string('x', 200000);
            using CancellationTokenSource cancellationSource = new CancellationTokenSource();
            int received = 0;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (TextChunk chunk in ReaderOver(original, maxTokens: 5, TokenCounter.CountCharacters).ReadChunksAsync(cancellationSource.Token))
                {
                    received++;

                    if (received == 3)
                        await cancellationSource.CancelAsync();
                }
            });

            // Three delivered, then it stops. Without a check inside the cut it would run to the
            // end of the segment, which is 40000 pieces.
            Assert.Equal(3, received);
        }

        #endregion

        #region Alongside the reader's other rules

        [Fact]
        public async Task ReadChunksAsync_SegmentThatIsBothAStopSignalAndOverTheLimit_IsCutAndStillStartsItsOwnChunk()
        {
            // A stop signal forces a new chunk before the limit is ever consulted, so an oversized
            // one reaches the cut by a different route than an ordinary segment does.
            string oversizedHeading = "# " + UnbreakableText(30);
            string original = $"intro\n{oversizedHeading}\ntail\n";

            List<TextChunk> chunks = await ReadAllTextChunks(
                CreateReader(original, breakStrings: ["\n"], stopSignals: ["#"], maxTokens: 5));

            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
            Assert.All(chunks, chunk => Assert.True(chunk.TokenCount <= 5));

            long expectedOffset = 0;
            foreach (TextChunk chunk in chunks)
            {
                Assert.Equal(expectedOffset, chunk.StartOffset);
                expectedOffset = chunk.EndOffset;
            }

            // The heading still begins a chunk of its own rather than being appended to "intro".
            Assert.Contains(chunks, chunk => chunk.Text.StartsWith("# ", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ReadChunksAsync_DefaultMarkdownOptions_CutsAnOversizedLineInAFencedBlock()
        {
            // The configuration a real caller uses. Every other test here reduces the break
            // strings to a newline, which is not what the package ships with, and a fenced code
            // block is where unbroken text actually turns up in Markdown.
            string original = $"# Title\n\nSome intro text.\n\n```\n{UnbreakableText(60)}\n```\n\nClosing text.\n";

            List<TextChunk> chunks = await ReadAllTextChunks(
                ChunkReader.Create(new StringReader(original), TokenCounter.CountWords, MarkdownOptions(8)));

            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
            Assert.All(chunks, chunk => Assert.True(
                chunk.TokenCount <= 8,
                $"chunk at {chunk.StartOffset} counts {chunk.TokenCount} tokens against a limit of 8."));

            long expectedOffset = 0;
            foreach (TextChunk chunk in chunks)
            {
                Assert.Equal(expectedOffset, chunk.StartOffset);
                expectedOffset = chunk.EndOffset;
            }
        }

        [Fact]
        public async Task ReadChunksAsync_SegmentsAccumulatingToExactlyTheLimit_AreNotCut()
        {
            // The same "already fits" guard the single-segment case reaches, arrived at by
            // accumulation instead: five segments of two characters each land on the limit
            // together, and the chunk they form must come back whole.
            string original = "ab\ncd\ncf\ngh\nij\n";

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 15, TokenCounter.CountCharacters));

            Assert.Single(chunks);
            Assert.Equal(original, chunks[0].Text);
            Assert.Equal(15, chunks[0].TokenCount);
        }

        [Fact]
        public async Task ReadChunksAsync_SubwordCounter_CutsMidWordExactlyAsTheReadmeSays()
        {
            // The README warns that a cut lands at the token limit rather than at a boundary, and
            // prints this as the example. Pinned here so the warning cannot quietly stop being
            // true — a reader who tries the sample and gets something else trusts neither.
            //
            // Four characters to a token approximates a subword tokenizer closely enough to show
            // the behaviour: a word counter cannot, because adding a character to a word never
            // raises its count.
            Func<string, int> subwordCounter = text => (text.Length + 3) / 4;
            string original = "the quick brown fox jumps";

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 3, subwordCounter));

            Assert.Equal(["the quick br", "own fox jump", "s"], chunks.Select(chunk => chunk.Text));
        }

        #endregion

        #region Degenerate counters

        [Fact]
        public async Task ReadChunksAsync_SingleCharacterAlreadyOverTheLimit_EmitsItAloneRatherThanLooping()
        {
            // The one case the docs admit can come back over the limit. What matters is that it
            // terminates, reconstructs, and cuts as small as it possibly can rather than giving up
            // and emitting the whole segment.
            string original = new string('x', 20);

            List<TextChunk> chunks = await ReadAllTextChunks(
                ReaderOver(original, maxTokens: 1, text => text.Length * 5));

            Assert.Equal(20, chunks.Count);
            Assert.All(chunks, chunk => Assert.Equal(1, chunk.Text.Length));
            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
        }

        [Fact]
        public async Task ReadChunksAsync_SurrogatePairAlreadyOverTheLimit_EmitsThePairWholeNotItsHalves()
        {
            string original = string.Concat(Enumerable.Repeat("\U0001F600", 8));

            List<TextChunk> chunks = await ReadAllTextChunks(
                ReaderOver(original, maxTokens: 1, text => text.Length * 5));

            Assert.Equal(8, chunks.Count);
            Assert.All(chunks, chunk => Assert.Equal("\U0001F600", chunk.Text));
        }

        [Fact]
        public async Task ReadChunksAsync_CounterThatSometimesShrinksAsTextGrows_StillNeverExceedsTheLimit()
        {
            // Real tokenizers are not perfectly monotonic in the length of their input: a
            // byte-pair encoder can charge fewer tokens for "the" than for "th", because the
            // longer string merges into one token. The search must not infer a count for a length
            // it never measured, or a dip like that becomes an over-limit chunk.
            //
            // This counter charges one token per character and then refunds four whenever the
            // length is a multiple of ten, so counts dip as the text grows, exactly where a
            // doubling search lands.
            Func<string, int> dippingCounter = text => text.Length % 10 == 0 ? text.Length - 4 : text.Length;
            string original = new string('x', 500);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 25, dippingCounter));

            Assert.True(chunks.Count > 1);
            Assert.All(chunks, chunk => Assert.True(
                dippingCounter(chunk.Text) <= 25,
                $"a chunk of {chunk.Text.Length} characters counts {dippingCounter(chunk.Text)} tokens against a limit of 25."));
            Assert.All(chunks, chunk => Assert.Equal(dippingCounter(chunk.Text), chunk.TokenCount));
            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));
        }

        [Fact]
        public async Task ReadChunksAsync_ChunkThatFitsWholeButNotInPieces_IsNotCutUp()
        {
            // The splitter answers "does this fit?" from the count already measured for the whole
            // chunk, not by searching upward from one character. It matters for a counter that
            // charges more for a short piece than for the whole: here the text counts exactly the
            // limit, while any piece of it counts far over. Searching would take the piece at its
            // word and hand back one chunk per character, all of them over the limit, for text
            // that was correct as it stood.
            Func<string, int> wholeIsCheaperCounter = text => text.Length == 20 ? 10 : 99;
            string original = new string('x', 20);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 10, wholeIsCheaperCounter));

            Assert.Single(chunks);
            Assert.Equal(original, chunks[0].Text);
            Assert.Equal(10, chunks[0].TokenCount);
        }

        [Fact]
        public async Task ReadChunksAsync_CounterThatChargesMoreForOneCharacterThanTheWholeLimit_TerminatesAndReconstructs()
        {
            // The documented exception, stated as what it is rather than as a bound that holds.
            // The search stops probing upward at the first piece that does not fit, so a counter
            // that reports over the limit for one character and under it for forty is not
            // something it will discover — it emits single characters, over the limit, because the
            // only smaller cut would divide a character in half. What has to hold even here is
            // that it terminates and loses nothing.
            Func<string, int> perverseCounter = text => text.Length % 2 == 0 ? 1 : 99;
            string original = new string('x', 41);

            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 50, perverseCounter));

            Assert.Equal(41, chunks.Count);
            Assert.All(chunks, chunk => Assert.Equal(1, chunk.Text.Length));
            Assert.All(chunks, chunk => Assert.Equal(perverseCounter(chunk.Text), chunk.TokenCount));
            Assert.Equal(original, string.Join("", chunks.Select(chunk => chunk.Text)));

            long expectedOffset = 0;
            foreach (TextChunk chunk in chunks)
            {
                Assert.Equal(expectedOffset, chunk.StartOffset);
                expectedOffset = chunk.EndOffset;
            }
        }

        #endregion

        #region Cost

        [Fact]
        public async Task ReadChunksAsync_LargeUnbreakableInput_DoesNotProbeQuadratically()
        {
            // The cut is found by measuring candidate prefixes, and every measurement copies the
            // prefix it measures. Searching the whole remaining text for each piece would make one
            // large input quadratic in its length, which trades an over-sized chunk for a stall.
            // Bounding the characters handed to the counter is what rules that out.
            string original = new string('x', 20000);
            long charactersMeasured = 0;

            List<TextChunk> chunks = await ReadAllTextChunks(
                ReaderOver(original, maxTokens: 100, text =>
                {
                    charactersMeasured += text.Length;
                    return text.Length;
                }));

            Assert.Equal(200, chunks.Count);

            // Stated as a multiple of the input rather than a number, because what is being ruled
            // out is a shape: bounded probing stays proportional to the input, unbounded probing
            // grows with its square. Searching the whole remainder for each piece measures around
            // 113 times the input here and more as it grows; bounding it measures around 10 times,
            // whatever the length. The multiple below sits well clear of both, so it fails on the
            // shape rather than on a change of a few probes either way.
            Assert.True(charactersMeasured < original.Length * 30L,
                $"the token counter was handed {charactersMeasured} characters for a {original.Length} character input.");
        }

        [Fact]
        public async Task ReadChunksAsync_LargeUnbreakableInput_DoesNotCopyTheRemainderPerPiece()
        {
            // The other half of the same cost, and invisible to the probe count above: cutting a
            // piece off the front by re-slicing what is left copies everything behind it, so the
            // work is quadratic in the chunk even though every probe is small. Measured as
            // allocation because that is what re-slicing spends.
            //
            // The enumeration below never actually suspends — a StringReader completes
            // synchronously — so it stays on this thread and the counter sees all of it.
            string original = new string('x', 20000);

            long before = GC.GetAllocatedBytesForCurrentThread();
            List<TextChunk> chunks = await ReadAllTextChunks(ReaderOver(original, maxTokens: 100, TokenCounter.CountCharacters));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(200, chunks.Count);

            // Two bytes per character. Measured, the pieces and probes together come to about 14
            // times the input; re-slicing takes it to about 114 times, and further as the input
            // grows. The multiple below sits between them with room on both sides, so it fails on
            // the shape rather than on a change of a few allocations either way.
            Assert.True(allocated < original.Length * 2L * 40,
                $"cutting a {original.Length} character chunk allocated {allocated} bytes.");
        }

        #endregion
    }
}
