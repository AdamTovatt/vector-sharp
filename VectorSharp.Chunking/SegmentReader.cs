using System.Text;

namespace VectorSharp.Chunking
{
    /// <summary>
    /// Reads text from a reader and splits it into segments at break string boundaries.
    /// Uses character-by-character reading with a lookahead buffer for memory efficiency.
    /// </summary>
    internal sealed class SegmentReader
    {
        private readonly TextReader _contentReader;
        private readonly string[] _breakStrings;
        private readonly Queue<char> _unreadBuffer;
        private readonly char[] _readBuffer = new char[1];
        private long _returnedCharacters;

        /// <summary>
        /// Gets the offset in the input of the segment returned by the most recent
        /// <see cref="ReadNextAsync"/> call that returned one.
        /// Characters pushed back into the lookahead buffer are not counted until the segment
        /// that contains them is returned, so this is the true position of the segment text.
        /// </summary>
        internal long LastSegmentStartOffset { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SegmentReader"/> class.
        /// </summary>
        /// <param name="contentReader">The reader to read content from.</param>
        /// <param name="breakStrings">The strings that indicate break points. Will be sorted longest-first internally.</param>
        internal SegmentReader(TextReader contentReader, IReadOnlyList<string> breakStrings)
        {
            _contentReader = contentReader;
            _breakStrings = breakStrings.ToArray();
            Array.Sort(_breakStrings, (a, b) => b.Length.CompareTo(a.Length));
            _unreadBuffer = new Queue<char>();
        }

        /// <summary>
        /// Reads the next text segment from the stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>The next text segment, or null if no more content is available.</returns>
        internal async Task<string?> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            StringBuilder segmentBuilder = new StringBuilder();
            string? currentMatch = null;
            int matchEndPosition = -1;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                char currentChar;

                if (_unreadBuffer.Count > 0)
                {
                    currentChar = _unreadBuffer.Dequeue();
                }
                else
                {
                    Memory<char> buffer = new Memory<char>(_readBuffer);
                    if (await _contentReader.ReadAsync(buffer, cancellationToken) == 0)
                        break;
                    currentChar = _readBuffer[0];
                }

                segmentBuilder.Append(currentChar);

                string? longestMatch = FindLongestBreakString(segmentBuilder);

                if (longestMatch != null)
                {
                    currentMatch = longestMatch;
                    matchEndPosition = segmentBuilder.Length;
                }
                else if (currentMatch != null)
                {
                    string result = segmentBuilder.ToString(0, matchEndPosition);

                    string extraChars = segmentBuilder.ToString(matchEndPosition, segmentBuilder.Length - matchEndPosition);
                    foreach (char c in extraChars)
                    {
                        _unreadBuffer.Enqueue(c);
                    }

                    return TrackPosition(result);
                }
            }

            if (currentMatch != null)
            {
                string result = segmentBuilder.ToString(0, matchEndPosition);

                string extraChars = segmentBuilder.ToString(matchEndPosition, segmentBuilder.Length - matchEndPosition);
                foreach (char c in extraChars)
                {
                    _unreadBuffer.Enqueue(c);
                }

                return TrackPosition(result);
            }

            return segmentBuilder.Length > 0 ? TrackPosition(segmentBuilder.ToString()) : null;
        }

        /// <summary>
        /// Records where the segment about to be returned starts and advances the cursor past it.
        /// Every return path of <see cref="ReadNextAsync"/> that produces a segment goes through
        /// here, which is what keeps <see cref="LastSegmentStartOffset"/> in step with the input.
        /// </summary>
        private string TrackPosition(string segment)
        {
            LastSegmentStartOffset = _returnedCharacters;
            _returnedCharacters += segment.Length;
            return segment;
        }

        private string? FindLongestBreakString(StringBuilder content)
        {
            foreach (string breakString in _breakStrings)
            {
                if (EndsWith(content, breakString))
                {
                    return breakString;
                }
            }

            return null;
        }

        private bool EndsWith(StringBuilder content, string value)
        {
            if (value.Length > content.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (content[content.Length - value.Length + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
