namespace VectorSharp.Chunking
{
    /// <summary>
    /// Provides predefined stop signal arrays for different text formats.
    /// When a segment starts with a stop signal, it forces a new chunk to begin,
    /// ensuring that structural elements like headings always appear at the start of a chunk.
    /// </summary>
    public static class StopSignals
    {
        /// <summary>
        /// Stop signals for Markdown content. Ensures headings, code blocks, and bold text
        /// always start a new chunk rather than appearing at the tail of a previous one.
        /// </summary>
        public static readonly IReadOnlyList<string> Markdown = Array.AsReadOnly(new string[]
        {
            "# ",
            "## ",
            "### ",
            "#### ",
            "##### ",
            "###### ",
            "```",
            "**"
        });

        /// <summary>
        /// Stop signals for C# source code. Ensures XML doc comment blocks
        /// always start a new chunk, which naturally aligns chunks with public API members.
        /// </summary>
        public static readonly IReadOnlyList<string> CSharp = Array.AsReadOnly(new string[]
        {
            "/// <summary>"
        });
    }
}
