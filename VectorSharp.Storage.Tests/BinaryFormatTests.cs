using System.Runtime.CompilerServices;

namespace VectorSharp.Storage.Tests
{
    [TestClass]
    public class BinaryFormatTests
    {
        [TestMethod]
        public void WriteHeader_ReadHeader_RoundTrip()
        {
            using MemoryStream stream = new MemoryStream();

            BinaryFormat.WriteHeader(stream, 768, 16, 100);

            stream.Position = 0;
            (int dimension, int keySize, int recordCount) = BinaryFormat.ReadHeader(stream);

            Assert.AreEqual(768, dimension);
            Assert.AreEqual(16, keySize);
            Assert.AreEqual(100, recordCount);
        }

        [TestMethod]
        public void WriteRecord_ReadRecord_RoundTrip_Guid()
        {
            using MemoryStream stream = new MemoryStream();
            Guid key = Guid.NewGuid();
            float magnitude = 1.5f;
            float[] values = new float[] { 0.1f, 0.2f, 0.3f };

            BinaryFormat.WriteRecord(stream, key, magnitude, values.AsSpan());

            stream.Position = 0;
            (Guid readKey, float readMagnitude, float[] readValues) = BinaryFormat.ReadRecord<Guid>(stream, 3);

            Assert.AreEqual(key, readKey);
            Assert.AreEqual(magnitude, readMagnitude, 0.0001f);
            CollectionAssert.AreEqual(values, readValues);
        }

        [TestMethod]
        public void WriteRecord_ReadRecord_RoundTrip_Int()
        {
            using MemoryStream stream = new MemoryStream();
            int key = 42;
            float magnitude = 2.5f;
            float[] values = new float[] { 1.0f, 2.0f };

            BinaryFormat.WriteRecord(stream, key, magnitude, values.AsSpan());

            stream.Position = 0;
            (int readKey, float readMagnitude, float[] readValues) = BinaryFormat.ReadRecord<int>(stream, 2);

            Assert.AreEqual(key, readKey);
            Assert.AreEqual(magnitude, readMagnitude, 0.0001f);
            CollectionAssert.AreEqual(values, readValues);
        }

        [TestMethod]
        public void WriteRecord_ReadRecord_RoundTrip_Long()
        {
            using MemoryStream stream = new MemoryStream();
            long key = 123456789L;
            float magnitude = 3.14f;
            float[] values = new float[] { -1.0f, 0.0f, 1.0f, 2.0f };

            BinaryFormat.WriteRecord(stream, key, magnitude, values.AsSpan());

            stream.Position = 0;
            (long readKey, float readMagnitude, float[] readValues) = BinaryFormat.ReadRecord<long>(stream, 4);

            Assert.AreEqual(key, readKey);
            Assert.AreEqual(magnitude, readMagnitude, 0.0001f);
            CollectionAssert.AreEqual(values, readValues);
        }

        [TestMethod]
        public void HeaderSize_IsCorrect()
        {
            Assert.AreEqual(20, BinaryFormat.HeaderSize);
        }

        [TestMethod]
        public void RecordSize_CalculationIsCorrect()
        {
            int guidKeySize = Unsafe.SizeOf<Guid>(); // 16
            int intKeySize = Unsafe.SizeOf<int>();    // 4

            // Guid key, 768 dimensions: 16 + 4 + 768*4 = 3092
            Assert.AreEqual(16 + 4 + 768 * 4, BinaryFormat.CalculateRecordSize(guidKeySize, 768));

            // Int key, 3 dimensions: 4 + 4 + 3*4 = 20
            Assert.AreEqual(4 + 4 + 3 * 4, BinaryFormat.CalculateRecordSize(intKeySize, 3));
        }

        [TestMethod]
        public void ReadHeader_InvalidMagicNumber_Throws()
        {
            using MemoryStream stream = new MemoryStream(new byte[20]);

            Assert.ThrowsExactly<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }

        [TestMethod]
        public void ReadHeader_TooShort_Throws()
        {
            using MemoryStream stream = new MemoryStream(new byte[5]);

            Assert.ThrowsExactly<InvalidOperationException>(() => BinaryFormat.ReadHeader(stream));
        }
    }
}
