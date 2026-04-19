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
