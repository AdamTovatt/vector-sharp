using System.Runtime.CompilerServices;

namespace VectorSharp.Chunking
{
    /// <summary>
    /// A streaming text chunker that splits text into token-bounded chunks suitable for embedding.
    /// Reads from a <see cref="TextReader"/> and yields chunks as <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <remarks>
    /// An instance is a single forward pass over its reader and is consumed by enumerating it.
    /// Enumerating the same instance a second time throws, because the reader has already moved
    /// past the text the first pass consumed. Create a new instance to chunk the same input again.
    /// </remarks>
    public sealed class ChunkReader
    {
        private readonly SegmentReader _segmentReader;
        private readonly Func<string, int> _countTokens;
        private readonly ChunkReaderOptions _options;
        private int _enumerationStarted;

        private ChunkReader(SegmentReader segmentReader, Func<string, int> countTokens, ChunkReaderOptions options)
        {
            _segmentReader = segmentReader;
            _countTokens = countTokens;
            _options = options;
        }

        /// <summary>
        /// Creates a new <see cref="ChunkReader"/> instance.
        /// </summary>
        /// <param name="reader">The reader to read text from. Any <see cref="TextReader"/> works,
        /// including <see cref="StreamReader"/> for files and streams and
        /// <see cref="StringReader"/> for text already held in memory.</param>
        /// <param name="countTokens">
        /// A function that counts the number of tokens in a string.
        /// Used to enforce <see cref="ChunkReaderOptions.MaxTokensPerChunk"/>.
        /// </param>
        /// <param name="options">
        /// Optional configuration. Defaults to Markdown format with 300 tokens per chunk.
        /// </param>
        /// <returns>A configured <see cref="ChunkReader"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> or <paramref name="countTokens"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when options contain invalid values.</exception>
        public static ChunkReader Create(TextReader reader, Func<string, int> countTokens, ChunkReaderOptions? options = null)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (countTokens == null)
                throw new ArgumentNullException(nameof(countTokens));

            ChunkReaderOptions effectiveOptions = options ?? new ChunkReaderOptions();

            if (effectiveOptions.MaxTokensPerChunk <= 0)
                throw new ArgumentException("MaxTokensPerChunk must be greater than zero.", nameof(options));
            if (effectiveOptions.BreakStrings == null || effectiveOptions.BreakStrings.Count == 0)
                throw new ArgumentException("BreakStrings must contain at least one entry.", nameof(options));

            SegmentReader segmentReader = new SegmentReader(reader, effectiveOptions.BreakStrings);
            return new ChunkReader(segmentReader, countTokens, effectiveOptions);
        }

        /// <summary>
        /// Creates a new <see cref="ChunkReader"/> instance reading from a <see cref="StreamReader"/>.
        /// Behaves exactly like the <see cref="TextReader"/> overload.
        /// </summary>
        /// <remarks>
        /// Not redundant with the <see cref="TextReader"/> overload, and not to be deleted as such.
        /// It exists for binary compatibility with the published chunking-v1.0.0, where Create took
        /// a <see cref="StreamReader"/>: assemblies compiled against that version reference this
        /// exact signature in IL, so removing it leaves them throwing MissingMethodException at run
        /// time even though their source still compiles unchanged against the wider overload.
        /// </remarks>
        /// <param name="reader">The stream reader to read text from.</param>
        /// <param name="countTokens">
        /// A function that counts the number of tokens in a string.
        /// Used to enforce <see cref="ChunkReaderOptions.MaxTokensPerChunk"/>.
        /// </param>
        /// <param name="options">
        /// Optional configuration. Defaults to Markdown format with 300 tokens per chunk.
        /// </param>
        /// <returns>A configured <see cref="ChunkReader"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> or <paramref name="countTokens"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when options contain invalid values.</exception>
        public static ChunkReader Create(StreamReader reader, Func<string, int> countTokens, ChunkReaderOptions? options = null)
        {
            return Create((TextReader)reader, countTokens, options);
        }

        /// <summary>
        /// Reads all chunks from the reader asynchronously.
        /// Concatenating all yielded chunks reproduces the original input exactly.
        /// Consumes the reader: see the remarks on <see cref="ChunkReader"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>An async enumerable of text chunks.</returns>
        /// <exception cref="InvalidOperationException">Thrown when this instance has already been enumerated.</exception>
        public async IAsyncEnumerable<string> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (TextChunk chunk in ReadChunksAsync(cancellationToken))
            {
                yield return chunk.Text;
            }
        }

        /// <summary>
        /// Reads all chunks from the reader asynchronously, each carrying its position in the
        /// input and its token count.
        /// Concatenating <see cref="TextChunk.Text"/> of all yielded chunks reproduces the
        /// original input exactly, which is what makes <see cref="TextChunk.StartOffset"/>
        /// usable for linking a chunk back to its place in the source document.
        /// Consumes the reader: see the remarks on <see cref="ChunkReader"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>An async enumerable of text chunks with position and token information.</returns>
        /// <exception cref="InvalidOperationException">Thrown when this instance has already been enumerated.</exception>
        public async IAsyncEnumerable<TextChunk> ReadChunksAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Guards both public enumerations, since ReadAllAsync runs through this one.
            // A second pass would silently skip the segment the abandoned pass had already
            // consumed but not yet yielded, and produce chunks that reconstruct nothing.
            if (Interlocked.Exchange(ref _enumerationStarted, 1) == 1)
                throw new InvalidOperationException("This ChunkReader has already been enumerated. Create a new one to read the input again.");

            string? firstSegment = await _segmentReader.ReadNextAsync(cancellationToken);
            if (firstSegment == null)
                yield break;

            PendingChunk current = StartChunk(firstSegment);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (current.TokenCount >= _options.MaxTokensPerChunk)
                {
                    yield return current.ToTextChunk();

                    string? nextAfterOversize = await _segmentReader.ReadNextAsync(cancellationToken);
                    if (nextAfterOversize == null)
                        yield break;

                    current = StartChunk(nextAfterOversize);
                    continue;
                }

                string? nextSegment = await _segmentReader.ReadNextAsync(cancellationToken);
                if (nextSegment == null)
                {
                    if (current.Text.Length > 0)
                        yield return current.ToTextChunk();
                    yield break;
                }

                if (StartsWithStopSignal(nextSegment))
                {
                    if (current.Text.Length > 0)
                        yield return current.ToTextChunk();
                    current = StartChunk(nextSegment);
                    continue;
                }

                string combinedText = current.Text + nextSegment;
                int combinedTokens = _countTokens(combinedText);

                if (combinedTokens <= _options.MaxTokensPerChunk)
                {
                    current = current.Extend(combinedText, combinedTokens);
                }
                else
                {
                    yield return current.ToTextChunk();
                    current = StartChunk(nextSegment);
                }
            }
        }

        /// <summary>
        /// Starts a chunk at the segment the segment reader just returned, taking the offset
        /// from the reader's own cursor instead of a running total kept here, so the offset
        /// describes where the text actually came from.
        /// </summary>
        private PendingChunk StartChunk(string segment)
        {
            return new PendingChunk(segment, _countTokens(segment), _segmentReader.LastSegmentStartOffset);
        }

        private bool StartsWithStopSignal(string segment)
        {
            if (string.IsNullOrEmpty(segment) || _options.StopSignals == null || _options.StopSignals.Count == 0)
                return false;

            string trimmed = segment.TrimStart();

            foreach (string stopSignal in _options.StopSignals)
            {
                if (!string.IsNullOrEmpty(stopSignal) && trimmed.StartsWith(stopSignal, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A chunk under construction: its text, the token count of exactly that text, and the
        /// offset it starts at. The three travel as one value so they cannot drift apart, and so
        /// the token count is carried forward when a chunk grows instead of being recounted.
        /// </summary>
        private readonly struct PendingChunk
        {
            internal PendingChunk(string text, int tokenCount, long startOffset)
            {
                Text = text;
                TokenCount = tokenCount;
                StartOffset = startOffset;
            }

            internal string Text { get; }

            internal int TokenCount { get; }

            internal long StartOffset { get; }

            /// <summary>
            /// Returns this chunk with more text appended. The start offset does not move,
            /// and the caller supplies the count it already had to compute for the decision
            /// to extend.
            /// </summary>
            internal PendingChunk Extend(string extendedText, int extendedTokenCount)
            {
                return new PendingChunk(extendedText, extendedTokenCount, StartOffset);
            }

            internal TextChunk ToTextChunk()
            {
                return new TextChunk
                {
                    Text = Text,
                    StartOffset = StartOffset,
                    TokenCount = TokenCount
                };
            }
        }
    }
}
