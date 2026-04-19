using System.Runtime.CompilerServices;

namespace VectorSharp.Storage.Tests
{
    public class BinaryFormatTests
    {
        [Fact]
        public void WriteHeader_ReadHeader_RoundTrip()
        {
            using MemoryStream stream = new MemoryStream();

            BinaryFormat.WriteHeader(stream, 768, 16, 100);

            stream.Position = 0;
            (int dimension, int keySize, int recordCount) = BinaryFormat.ReadHeader(stream);

            Assert.Equal(768, dimension);
            Assert.Equal(16, keySize);
            Assert.Equal(100, recordCount);
        }

        [Fact]
        public void WriteRecord_ReadRecord_RoundTrip_Guid()
        {
            using MemoryStream stream = new MemoryStream();
            Guid key = Guid.NewGuid();
            float magnitude = 1.5f;
            float[] values = new float[] { 0.1f, 0.2f, 0.3f };

            BinaryFormat.WriteRecord(stream, key, magnitude, values.AsSpan());

            stream.Position = 0;
            (byte readStatus, Guid readKey, float readMagnitude, float[] readValues) = BinaryFormat.ReadRecord<Guid>(stream, 3);

            Assert.Equal(BinaryFormat.RecordStatusActive, readStatus);

            Assert.Equal(key, readKey);
            TestHelpers.AssertApproximatelyEqual(magnitude, readMagnitude, 0.0001f);
            Assert.Equal(values, readValues);
        }

        [Fact]
        public void WriteRecord_ReadRecord_RoundTrip_Int()
        {
            using MemoryStream stream = new MemoryStream();
            int key = 42;
            float magnitude = 2.5f;
            float[] values = new float[] { 1.0f, 2.0f };

            BinaryFormat.WriteRecord(stream, key, magnitude, values.AsSpan());

            stream.Position = 0;
            (byte readStatus, int readKey, float readMagnitude, float[] readValues) = BinaryFormat.ReadRecord<int>(stream, 2);

            Assert.Equal(BinaryFormat.RecordStatusActive, readStatus);

            Assert.Equal(key, readKey);
            TestHelpers.AssertApproximatelyEqual(magnitude, readMagnitude, 0.0001f);
            Assert.Equal(values, readValues);
        }

        [Fact]
        public void WriteRecord_ReadRecord_RoundTrip_Long()
        {
            using MemoryStream stream = new MemoryStream();
            long key = 123456789L;
            float magnitude = 3.14f;
            float[] values = new float[] { -1.0f, 0.0f, 1.0f, 2.0f };

            BinaryFormat.WriteRecord(stream, key, magnitude, values.AsSpan());

            stream.Position = 0;
            (byte readStatus, long readKey, float readMagnitude, float[] readValues) = BinaryFormat.ReadRecord<long>(stream, 4);

            Assert.Equal(BinaryFormat.RecordStatusActive, readStatus);

            Assert.Equal(key, readKey);
            TestHelpers.AssertApproximatelyEqual(magnitude, readMagnitude, 0.0001f);
            Assert.Equal(values, readValues);
        }

        [Fact]
        public void HeaderSize_IsCorrect()
        {
            Assert.Equal(20, BinaryFormat.HeaderSize);
        }

        [Fact]
        public void RecordSize_CalculationIsCorrect()
        {
            int guidKeySize = Unsafe.SizeOf<Guid>(); // 16
            int intKeySize = Unsafe.SizeOf<int>();    // 4

            // Guid key, 768 dimensions: 1 + 16 + 4 + 768*4 = 3093
            Assert.Equal(1 + 16 + 4 + 768 * 4, BinaryFormat.CalculateRecordSize(guidKeySize, 768));

            // Int key, 3 dimensions: 1 + 4 + 4 + 3*4 = 21
            Assert.Equal(1 + 4 + 4 + 3 * 4, BinaryFormat.CalculateRecordSize(intKeySize, 3));
        }

        [Fact]
        public void ReadHeader_InvalidMagicNumber_Throws()
        {
            using MemoryStream stream = new MemoryStream(new byte[20]);

            Assert.Throws<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }

        [Fact]
        public void ReadHeader_TooShort_Throws()
        {
            using MemoryStream stream = new MemoryStream(new byte[5]);

            Assert.Throws<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }

        [Fact]
        public void ReadHeader_UnsupportedVersion_Throws()
        {
            using MemoryStream stream = new MemoryStream();

            Span<byte> header = stackalloc byte[BinaryFormat.HeaderSize];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[..2], BinaryFormat.MagicNumber);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[2..4], 99); // future version
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 768);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[8..12], 16);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[12..16], 0);
            header[16..20].Clear();
            stream.Write(header);
            stream.Position = 0;

            Assert.Throws<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }

        [Fact]
        public void ReadHeader_NegativeDimension_Throws()
        {
            using MemoryStream stream = new MemoryStream();

            Span<byte> header = stackalloc byte[BinaryFormat.HeaderSize];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[..2], BinaryFormat.MagicNumber);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[2..4], BinaryFormat.CurrentVersion);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[4..8], -1); // negative dimension
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[8..12], 16);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[12..16], 0);
            header[16..20].Clear();
            stream.Write(header);
            stream.Position = 0;

            Assert.Throws<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }

        [Fact]
        public void ReadHeader_NegativeRecordCount_Throws()
        {
            using MemoryStream stream = new MemoryStream();

            Span<byte> header = stackalloc byte[BinaryFormat.HeaderSize];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[..2], BinaryFormat.MagicNumber);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[2..4], BinaryFormat.CurrentVersion);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 768);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[8..12], 16);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[12..16], -5); // negative count
            header[16..20].Clear();
            stream.Write(header);
            stream.Position = 0;

            Assert.Throws<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }
    }
}
