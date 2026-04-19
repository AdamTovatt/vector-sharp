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

        /// <summary>
        /// Stop signals for generic plain text. Empty by default — plain text has no
        /// language-specific structural markers to force chunk boundaries on.
        /// </summary>
        public static readonly IReadOnlyList<string> PlainText = Array.AsReadOnly(Array.Empty<string>());

        /// <summary>
        /// Stop signals for JavaScript, TypeScript, JSX, and TSX source code.
        /// Ensures JSDoc comment blocks always start a new chunk, which naturally aligns
        /// chunks with documented functions, classes, and exports.
        /// </summary>
        public static readonly IReadOnlyList<string> JavaScript = Array.AsReadOnly(new string[]
        {
            "/**"
        });

        /// <summary>
        /// Stop signals for HTML content. Ensures top-level heading tags always start a new
        /// chunk, which naturally aligns chunks with document sections.
        /// </summary>
        public static readonly IReadOnlyList<string> Html = Array.AsReadOnly(new string[]
        {
            "<h1",
            "<h2",
            "<h3"
        });

        /// <summary>
        /// Stop signals for CSS source code. Ensures at-rules (@media, @keyframes, @import,
        /// @supports) always start a new chunk rather than appearing at the tail of the
        /// previous rule block.
        /// </summary>
        public static readonly IReadOnlyList<string> Css = Array.AsReadOnly(new string[]
        {
            "@media",
            "@keyframes",
            "@import",
            "@supports"
        });

        /// <summary>
        /// Stop signals for Python source code. Ensures function and class definitions
        /// always start a new chunk, which naturally aligns chunks with API boundaries.
        /// Since stop signals are matched against the trimmed start of a segment, these
        /// apply to both top-level and indented (method-level) definitions.
        /// </summary>
        public static readonly IReadOnlyList<string> Python = Array.AsReadOnly(new string[]
        {
            "def ",
            "async def ",
            "class "
        });
    }
}
