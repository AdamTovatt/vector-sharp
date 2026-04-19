using System.Runtime.CompilerServices;

namespace VectorSharp.Chunking
{
    /// <summary>
    /// A streaming text chunker that splits text into token-bounded chunks suitable for embedding.
    /// Reads from a <see cref="StreamReader"/> and yields chunks as <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public sealed class ChunkReader
    {
        private readonly SegmentReader _segmentReader;
        private readonly Func<string, int> _countTokens;
        private readonly ChunkReaderOptions _options;

        private ChunkReader(SegmentReader segmentReader, Func<string, int> countTokens, ChunkReaderOptions options)
        {
            _segmentReader = segmentReader;
            _countTokens = countTokens;
            _options = options;
        }

        /// <summary>
        /// Creates a new <see cref="ChunkReader"/> instance.
        /// </summary>
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
        /// Reads all chunks from the stream asynchronously.
        /// Concatenating all yielded chunks reproduces the original input exactly.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>An async enumerable of text chunks.</returns>
        public async IAsyncEnumerable<string> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string? firstSegment = await _segmentReader.ReadNextAsync(cancellationToken);
            if (firstSegment == null)
                yield break;

            string currentChunk = firstSegment;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int currentTokens = _countTokens(currentChunk);
                if (currentTokens >= _options.MaxTokensPerChunk)
                {
                    yield return currentChunk;
                    currentChunk = string.Empty;

                    string? nextAfterOversize = await _segmentReader.ReadNextAsync(cancellationToken);
                    if (nextAfterOversize == null)
                        yield break;

                    currentChunk = nextAfterOversize;
                    continue;
                }

                string? nextSegment = await _segmentReader.ReadNextAsync(cancellationToken);
                if (nextSegment == null)
                {
                    if (!string.IsNullOrEmpty(currentChunk))
                        yield return currentChunk;
                    yield break;
                }

                if (StartsWithStopSignal(nextSegment))
                {
                    if (!string.IsNullOrEmpty(currentChunk))
                        yield return currentChunk;
                    currentChunk = nextSegment;
                    continue;
                }

                string potentialChunk = currentChunk + nextSegment;
                int potentialTokens = _countTokens(potentialChunk);

                if (potentialTokens <= _options.MaxTokensPerChunk)
                {
                    currentChunk = potentialChunk;
                }
                else
                {
                    yield return currentChunk;
                    currentChunk = nextSegment;
                }
            }
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
    }
}
