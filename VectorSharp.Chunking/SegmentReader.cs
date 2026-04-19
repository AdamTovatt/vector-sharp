using System.Text;

namespace VectorSharp.Chunking
{
    /// <summary>
    /// Reads text from a stream and splits it into segments at break string boundaries.
    /// Uses character-by-character reading with a lookahead buffer for memory efficiency.
    /// </summary>
    internal sealed class SegmentReader
    {
        private readonly StreamReader _contentReader;
        private readonly string[] _breakStrings;
        private readonly Queue<char> _unreadBuffer;
        private readonly char[] _readBuffer = new char[1];

        /// <summary>
        /// Initializes a new instance of the <see cref="SegmentReader"/> class.
        /// </summary>
        /// <param name="contentReader">The stream reader to read content from.</param>
        /// <param name="breakStrings">The strings that indicate break points. Will be sorted longest-first internally.</param>
        internal SegmentReader(StreamReader contentReader, IReadOnlyList<string> breakStrings)
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

                    return result;
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

                return result;
            }

            return segmentBuilder.Length > 0 ? segmentBuilder.ToString() : null;
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
