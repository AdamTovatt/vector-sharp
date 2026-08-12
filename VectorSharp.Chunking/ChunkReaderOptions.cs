namespace VectorSharp.Chunking
{
    /// <summary>
    /// Configuration options for <see cref="ChunkReader"/>.
    /// </summary>
    public sealed class ChunkReaderOptions
    {
        /// <summary>
        /// Gets the maximum number of tokens allowed per chunk. Default is 300.
        /// </summary>
        /// <remarks>
        /// A hard bound, not a target: text with no break point in it for long enough to pass this
        /// is cut at the limit rather than emitted whole. Set it to what the model behind your
        /// embedding provider actually accepts — a chunk over a hosted model's context window is
        /// rejected or silently truncated, and a truncated chunk is a piece of the document that
        /// is no longer searchable with nothing in the result to say so.
        ///
        /// A cut lands where the token counter says the limit falls, which for a subword or
        /// character counter is in the middle of a word. Break strings, not this, are what put a
        /// boundary somewhere meaningful.
        ///
        /// The bound rests on a counter that does not report over the limit for a short piece of
        /// text and under it for a longer one. Every real tokenizer satisfies that; one that does
        /// not can produce a chunk over the limit, because the alternative is cutting a single
        /// character in half.
        /// </remarks>
        public int MaxTokensPerChunk { get; init; } = 300;

        /// <summary>
        /// Gets the break strings used to identify segment boundaries in the text.
        /// Defaults to <see cref="Chunking.BreakStrings.Markdown"/>.
        /// </summary>
        public IReadOnlyList<string> BreakStrings { get; init; } = Chunking.BreakStrings.Markdown;

        /// <summary>
        /// Gets the stop signals that force a new chunk to begin.
        /// When a segment starts with any of these strings, it will always appear
        /// at the start of a new chunk rather than being appended to the current one.
        /// Defaults to <see cref="Chunking.StopSignals.Markdown"/>.
        /// </summary>
        public IReadOnlyList<string> StopSignals { get; init; } = Chunking.StopSignals.Markdown;
    }
}
