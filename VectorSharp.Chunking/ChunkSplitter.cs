namespace VectorSharp.Chunking
{
    /// <summary>
    /// One piece of a chunk: where it starts and ends in the text it was cut from, and the number
    /// of tokens counted for exactly that piece.
    /// </summary>
    /// <remarks>
    /// The three travel together for the same reason a chunk under construction does — a position
    /// and a count kept apart drift apart, and a count that belongs to a different span of text is
    /// worse than no count at all.
    /// </remarks>
    internal readonly struct Cut
    {
        internal Cut(int start, int end, int tokenCount)
        {
            Start = start;
            End = end;
            TokenCount = tokenCount;
        }

        internal int Start { get; }

        internal int End { get; }

        internal int TokenCount { get; }

        internal int Length => End - Start;
    }

    /// <summary>
    /// Cuts text into consecutive pieces that stay within a token limit.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ChunkReader"/>, which assembles segments into chunks: this decides
    /// where text may be divided, which is a different question answered with a different tool —
    /// a search against the caller's token counter rather than a scan for break strings. Keeping
    /// it apart also means the search can be exercised directly instead of only through a reader.
    ///
    /// The pieces cover the text exactly, in order and with no gaps, which is what carries the
    /// round-trip guarantee and the offsets through a cut.
    /// </remarks>
    internal sealed class ChunkSplitter
    {
        private readonly Func<string, int> _countTokens;
        private readonly int _maxTokensPerChunk;

        internal ChunkSplitter(Func<string, int> countTokens, int maxTokensPerChunk)
        {
            _countTokens = countTokens;
            _maxTokensPerChunk = maxTokensPerChunk;
        }

        /// <summary>
        /// Cuts <paramref name="text"/> into pieces within the token limit.
        /// </summary>
        /// <param name="text">The text to cut.</param>
        /// <param name="knownTokenCount">The token count already measured for the whole of
        /// <paramref name="text"/>, so that text needing no cutting costs no further counting.</param>
        /// <param name="cancellationToken">Observed once per piece. Cutting a long stretch of text
        /// with no break point in it can take many pieces and many calls into the caller's
        /// tokenizer, and a caller who has stopped waiting should not have to wait for all of them.</param>
        /// <remarks>
        /// Text already within the limit comes back as one piece without being searched. For an
        /// ordinary counter that is only a saving — the search would arrive at the same answer,
        /// after a handful of needless calls into the caller's tokenizer.
        ///
        /// What it also does is answer from the measurement of the whole text rather than from the
        /// smallest piece upward. A counter that reports over the limit for one character and
        /// under it for the whole chunk would otherwise be taken at its word about the character:
        /// the search works upward and stops at the first piece that does not fit, so it would cut
        /// a chunk that fits perfectly into single characters.
        /// </remarks>
        internal IEnumerable<Cut> Split(string text, int knownTokenCount, CancellationToken cancellationToken = default)
        {
            if (knownTokenCount <= _maxTokensPerChunk)
            {
                yield return new Cut(0, text.Length, knownTokenCount);
                yield break;
            }

            // Walks an index rather than re-slicing what is left. Trimming the front off a string
            // copies everything behind it, which for a long stretch of text is the same quadratic
            // cost the search is written to avoid, just moved somewhere less visible.
            int start = 0;
            while (start < text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Cut cut = NextCut(text, start);
                yield return cut;

                start = cut.End;
            }
        }

        /// <summary>
        /// Finds how much of <paramref name="text"/> can be taken from <paramref name="start"/>
        /// while staying within the token limit, and the token count of exactly that much.
        /// </summary>
        /// <remarks>
        /// The token counter belongs to the caller, so nothing here can predict what it will say.
        /// Every length this returns is one that was measured, never one interpolated between two
        /// probes — which is what makes the result safe against a tokenizer that is not perfectly
        /// monotonic in the length of its input. Byte-pair encoders are not: "the" can cost fewer
        /// tokens than "th". Such a counter only makes a cut shorter than it needed to be.
        ///
        /// What the search does assume is that a counter which reports over the limit for a short
        /// piece of text will not report under it for a longer one, since it stops probing upward
        /// at the first piece that does not fit. A counter that violates that badly enough — one
        /// that charges more for one character than the whole limit allows — is the one case where
        /// a piece comes back over the limit, because the alternative is cutting a character in
        /// half.
        ///
        /// The search runs outward from the smallest possible piece, doubling until one does not
        /// fit, and then narrows inside that range. Searching the whole remaining text for every
        /// piece would be quadratic in the length of the input, because each probe has to copy the
        /// text it measures; this keeps every probe within twice the piece it finds.
        /// </remarks>
        /// <returns>A piece of at least one character, never ending inside a surrogate pair.</returns>
        internal Cut NextCut(string text, int start)
        {
            int smallestEnd = SmallestPieceEnd(text, start);
            int smallestTokens = CountFrom(text, start, smallestEnd);

            if (smallestTokens > _maxTokensPerChunk)
            {
                // The smallest piece that can be taken is already over the limit. It goes out
                // anyway: cutting further would divide one character into halves that are not
                // characters, which hands the caller's tokenizer and their embedding API a lone
                // surrogate. See the remarks above for when this is reachable.
                return new Cut(start, smallestEnd, smallestTokens);
            }

            int bestEnd = smallestEnd;
            int bestTokens = smallestTokens;

            if (bestEnd == text.Length)
                return new Cut(start, bestEnd, bestTokens);

            // Outward: double the piece until one does not fit, so the range to narrow is bounded
            // by the answer rather than by the length of the input.
            int probeEnd = bestEnd;
            while (true)
            {
                probeEnd = NextLegalEndAtOrAbove(text, start + (int)Math.Min((long)(probeEnd - start) * 2, text.Length - start));

                // Upward, never downward, so that stepping off a surrogate pair cannot leave the
                // probe where it already was and stall the doubling.
                if (probeEnd <= bestEnd)
                    probeEnd = NextLegalEndAtOrAbove(text, Math.Min(bestEnd + 1, text.Length));

                int count = CountFrom(text, start, probeEnd);
                if (count > _maxTokensPerChunk)
                    break;

                bestEnd = probeEnd;
                bestTokens = count;

                if (bestEnd == text.Length)
                    return new Cut(start, bestEnd, bestTokens);
            }

            // Inward: bestEnd fits, probeEnd does not, so the answer is between them.
            int low = bestEnd + 1;
            int high = probeEnd - 1;

            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int candidate = NextLegalEndAtOrBelow(text, middle);

                // Stepping down off a surrogate pair can land below the range. Step up instead —
                // clamping back to the bottom of the range would put the cut back inside the pair,
                // which is the one place it must never be.
                if (candidate < low)
                    candidate = NextLegalEndAtOrAbove(text, middle);

                if (candidate > high)
                {
                    // Every position left in the range is inside a surrogate pair, so there is no
                    // longer piece to be found.
                    break;
                }

                int count = CountFrom(text, start, candidate);

                if (count <= _maxTokensPerChunk)
                {
                    bestEnd = candidate;
                    bestTokens = count;
                    low = candidate + 1;
                }
                else
                {
                    high = candidate - 1;
                }
            }

            return new Cut(start, bestEnd, bestTokens);
        }

        private int CountFrom(string text, int start, int end)
        {
            return _countTokens(text.Substring(start, end - start));
        }

        /// <summary>
        /// Where the smallest piece starting at <paramref name="start"/> ends: one character
        /// later, or two when the first is the leading half of a surrogate pair.
        /// </summary>
        internal static int SmallestPieceEnd(string text, int start)
        {
            return start + 2 <= text.Length && char.IsHighSurrogate(text[start]) && char.IsLowSurrogate(text[start + 1]) ? start + 2 : start + 1;
        }

        /// <summary>
        /// Whether ending a piece at this position would fall between the two halves of one
        /// character.
        /// </summary>
        /// <remarks>
        /// Cutting there hands out two pieces that are not text: a lone surrogate is not a
        /// character, and it is what would reach the caller's tokenizer and their embedding API.
        /// This protects code points and nothing wider — a cut can still separate a base character
        /// from a combining mark that follows it, and can land in the middle of a word.
        /// </remarks>
        internal static bool IsInsideSurrogatePair(string text, int position)
        {
            return position > 0 && position < text.Length && char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position]);
        }

        internal static int NextLegalEndAtOrBelow(string text, int position)
        {
            return IsInsideSurrogatePair(text, position) ? position - 1 : position;
        }

        internal static int NextLegalEndAtOrAbove(string text, int position)
        {
            return IsInsideSurrogatePair(text, position) ? position + 1 : position;
        }
    }
}
