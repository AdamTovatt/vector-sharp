namespace VectorSharp.Chunking
{
    /// <summary>
    /// Provides predefined break string arrays for different text formats.
    /// Break strings define where text can be split into segments. When the chunker
    /// encounters a break string at the end of accumulated text, it treats that position
    /// as a potential split point. When multiple break strings match at the same position,
    /// the longest one wins. Note that break strings sharing a common prefix (e.g. "\n\n"
    /// and "\n\n# ") may not resolve in favor of the longer one if a non-matching character
    /// appears between them — use stop signals to ensure structural elements like headings
    /// always start a new chunk regardless of break string resolution.
    /// </summary>
    public static class BreakStrings
    {
        /// <summary>
        /// Break strings optimized for Markdown content. Prioritizes structural boundaries
        /// (headings, paragraphs) over inline boundaries (sentences).
        /// </summary>
        public static readonly IReadOnlyList<string> Markdown = Array.AsReadOnly(new string[]
        {
            "\n\n# ",
            "\n## ",
            "\n### ",
            "\n#### ",
            "\n##### ",
            "\n###### ",
            "\n\n",
            "\n- ",
            "\n* ",
            "\n+ ",
            "\n1. ",
            "\n```\n",
            "\n",
            ". ",
            "! ",
            "? "
        });

        /// <summary>
        /// Break strings optimized for C# source code. Prioritizes structural boundaries
        /// (blank lines, braces) over statement boundaries. Indentation-agnostic.
        /// </summary>
        public static readonly IReadOnlyList<string> CSharp = Array.AsReadOnly(new string[]
        {
            "\n\n",
            "\n{",
            "\n}",
            "\n",
            "; "
        });
    }
}
