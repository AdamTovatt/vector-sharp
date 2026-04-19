namespace VectorSharp.Chunking.Tests
{
    public class SegmentReaderTests
    {
        private static StreamReader ReaderFrom(string text)
        {
            MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            return new StreamReader(stream);
        }

        private static async Task<List<string>> ReadAllSegments(SegmentReader reader)
        {
            List<string> segments = new List<string>();
            string? segment;
            while ((segment = await reader.ReadNextAsync()) != null)
            {
                segments.Add(segment);
            }
            return segments;
        }

        [Fact]
        public async Task ReadNextAsync_EmptyStream_ReturnsNull()
        {
            using StreamReader streamReader = ReaderFrom("");
            SegmentReader reader = new SegmentReader(streamReader, ["\n"]);

            string? result = await reader.ReadNextAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task ReadNextAsync_NoBreakPoints_ReturnsEntireContent()
        {
            using StreamReader streamReader = ReaderFrom("hello world");
            SegmentReader reader = new SegmentReader(streamReader, ["\n"]);

            string? result = await reader.ReadNextAsync();

            Assert.Equal("hello world", result);
        }

        [Fact]
        public async Task ReadNextAsync_SingleBreakPoint_SplitsCorrectly()
        {
            using StreamReader streamReader = ReaderFrom("hello\nworld");
            SegmentReader reader = new SegmentReader(streamReader, ["\n"]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Equal(2, segments.Count);
            Assert.Equal("hello\n", segments[0]);
            Assert.Equal("world", segments[1]);
        }

        [Fact]
        public async Task ReadNextAsync_MultipleBreakPoints_SplitsAtEach()
        {
            using StreamReader streamReader = ReaderFrom("one. two. three");
            SegmentReader reader = new SegmentReader(streamReader, [". "]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Equal(3, segments.Count);
            Assert.Equal("one. ", segments[0]);
            Assert.Equal("two. ", segments[1]);
            Assert.Equal("three", segments[2]);
        }

        [Fact]
        public async Task ReadNextAsync_LongerBreakStringPreferred()
        {
            using StreamReader streamReader = ReaderFrom("hello\n\nworld");
            SegmentReader reader = new SegmentReader(streamReader, ["\n", "\n\n"]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Equal(2, segments.Count);
            Assert.Equal("hello\n\n", segments[0]);
            Assert.Equal("world", segments[1]);
        }

        [Fact]
        public async Task ReadNextAsync_DoubleNewlineOverSingle()
        {
            using StreamReader streamReader = ReaderFrom("first paragraph\n\nsecond paragraph\nstill second");
            SegmentReader reader = new SegmentReader(streamReader, ["\n", "\n\n"]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Equal(3, segments.Count);
            Assert.Equal("first paragraph\n\n", segments[0]);
            Assert.Equal("second paragraph\n", segments[1]);
            Assert.Equal("still second", segments[2]);
        }

        [Fact]
        public async Task ReadNextAsync_CustomBreakStrings_Work()
        {
            using StreamReader streamReader = ReaderFrom("a---b---c");
            SegmentReader reader = new SegmentReader(streamReader, ["---"]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Equal(3, segments.Count);
            Assert.Equal("a---", segments[0]);
            Assert.Equal("b---", segments[1]);
            Assert.Equal("c", segments[2]);
        }

        [Fact]
        public async Task ReadNextAsync_BreakStringAtEnd_ReturnsContent()
        {
            using StreamReader streamReader = ReaderFrom("hello\n");
            SegmentReader reader = new SegmentReader(streamReader, ["\n"]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Single(segments);
            Assert.Equal("hello\n", segments[0]);
        }

        [Fact]
        public async Task ReadNextAsync_ConsecutiveBreakStrings_EachIsOwnSegment()
        {
            using StreamReader streamReader = ReaderFrom("a. . b");
            SegmentReader reader = new SegmentReader(streamReader, [". "]);

            List<string> segments = await ReadAllSegments(reader);

            Assert.Equal(3, segments.Count);
            Assert.Equal("a. ", segments[0]);
            Assert.Equal(". ", segments[1]);
            Assert.Equal("b", segments[2]);
        }

        [Fact]
        public async Task ReadNextAsync_RoundTrip_PreservesOriginalContent()
        {
            string original = "# Heading\n\nFirst paragraph with some text. Second sentence here.\n\n## Second heading\n\n- item one\n- item two\n";
            using StreamReader streamReader = ReaderFrom(original);
            SegmentReader reader = new SegmentReader(streamReader, BreakStrings.Markdown);

            List<string> segments = await ReadAllSegments(reader);
            string reconstructed = string.Join("", segments);

            Assert.Equal(original, reconstructed);
        }

        [Fact]
        public async Task ReadNextAsync_CancelledToken_ThrowsOperationCancelled()
        {
            using StreamReader streamReader = ReaderFrom("hello world this is a long text");
            SegmentReader reader = new SegmentReader(streamReader, [". "]);
            CancellationToken cancelled = new CancellationToken(true);

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await reader.ReadNextAsync(cancelled));
        }
    }
}
