namespace VectorSharp.Chunking
{
    // A class rather than a struct: `required` is enforced only on object-creation expressions,
    // so a struct here would let `default(TextChunk)` and `List<TextChunk>.FirstOrDefault()`
    // hand out an instance whose non-nullable Text is null and whose EndOffset throws.
    // As a class those same expressions produce a plain null, which the caller can see.

    /// <summary>
    /// A single chunk produced by <see cref="ChunkReader.ReadChunksAsync"/>, together with
    /// its position in the source text and its token count.
    /// </summary>
    public sealed class TextChunk
    {
        /// <summary>
        /// Gets the chunk text. Concatenating this value for every chunk in the order they were
        /// yielded reproduces the original input exactly.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// Gets the character offset of this chunk from the start of the input, which is the
        /// position the chunk's first segment was read from. Because the chunks reproduce the
        /// input exactly, it is also the sum of the lengths of all preceding chunks.
        /// Measured in UTF-16 code units, the same unit as <see cref="string.Length"/>, and
        /// typed as <see cref="long"/> because offsets accumulate over the whole input, which
        /// may run past <see cref="int.MaxValue"/> characters.
        /// </summary>
        public required long StartOffset { get; init; }

        /// <summary>
        /// Gets the number of tokens in <see cref="Text"/>, as counted by the token counter
        /// passed to <see cref="ChunkReader.Create(TextReader, Func{string, int}, ChunkReaderOptions)"/>.
        /// May exceed <see cref="ChunkReaderOptions.MaxTokensPerChunk"/> when a single segment
        /// with no break point inside it is already over the limit.
        /// </summary>
        public required int TokenCount { get; init; }

        /// <summary>
        /// Gets the character offset one past the last character of this chunk, which is the
        /// <see cref="StartOffset"/> of the next chunk.
        /// </summary>
        public long EndOffset => StartOffset + Text.Length;
    }
}
