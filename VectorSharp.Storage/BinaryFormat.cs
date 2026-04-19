using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VectorSharp.Storage
{
    /// <summary>
    /// Internal helper for reading and writing the VectorSharp binary format.
    /// Used by both <see cref="CosineVectorStore{TKey}"/> for persistence
    /// and <see cref="DiskVectorStore{TKey}"/> for disk-backed storage.
    /// </summary>
    internal static class BinaryFormat
    {
        static BinaryFormat()
        {
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("VectorSharp binary format requires a little-endian platform.");
        }

        /// <summary>
        /// Magic number identifying a VectorSharp file ("VS" in little-endian).
        /// </summary>
        internal const ushort MagicNumber = 0x5653;

        /// <summary>
        /// Current binary format version.
        /// </summary>
        internal const ushort CurrentVersion = 1;

        /// <summary>
        /// Record status byte indicating an active (non-deleted) record.
        /// </summary>
        internal const byte RecordStatusActive = 0x00;

        /// <summary>
        /// Record status byte indicating a deleted record.
        /// </summary>
        internal const byte RecordStatusDeleted = 0x01;

        /// <summary>
        /// Size of the file header in bytes.
        /// </summary>
        internal const int HeaderSize = 20;

        /// <summary>
        /// Calculates the size of a single record in bytes.
        /// </summary>
        /// <param name="keySize">The size of the key type in bytes.</param>
        /// <param name="dimension">The vector dimension.</param>
        /// <returns>The total record size in bytes.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CalculateRecordSize(int keySize, int dimension)
        {
            return 1 + keySize + 4 + (dimension * 4); // status + key + magnitude + values
        }

        /// <summary>
        /// Writes the file header to a stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="dimension">The vector dimension.</param>
        /// <param name="keySize">The key type size in bytes.</param>
        /// <param name="recordCount">The number of records.</param>
        internal static void WriteHeader(Stream stream, int dimension, int keySize, int recordCount)
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(header[..2], MagicNumber);
            BinaryPrimitives.WriteUInt16LittleEndian(header[2..4], CurrentVersion);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..8], dimension);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..12], keySize);
            BinaryPrimitives.WriteInt32LittleEndian(header[12..16], recordCount);
            header[16..20].Clear(); // reserved
            stream.Write(header);
        }

        /// <summary>
        /// Reads and validates the file header from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>A tuple containing the dimension, key size, and record count.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the header is invalid.</exception>
        internal static (int Dimension, int KeySize, int RecordCount) ReadHeader(Stream stream)
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            int bytesRead = stream.Read(header);

            if (bytesRead < HeaderSize)
                throw new InvalidOperationException("Invalid VectorSharp file: header too short.");

            ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(header[..2]);
            if (magic != MagicNumber)
                throw new InvalidOperationException("Invalid VectorSharp file: wrong magic number.");

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[2..4]);
            if (version > CurrentVersion)
                throw new InvalidOperationException($"Unsupported VectorSharp format version {version}. Maximum supported version is {CurrentVersion}.");

            int dimension = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
            int keySize = BinaryPrimitives.ReadInt32LittleEndian(header[8..12]);
            int recordCount = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);

            if (dimension <= 0)
                throw new InvalidOperationException($"Invalid VectorSharp file: dimension must be positive, got {dimension}.");
            if (keySize <= 0 || keySize > 1024)
                throw new InvalidOperationException($"Invalid VectorSharp file: key size must be between 1 and 1024, got {keySize}.");
            if (recordCount < 0)
                throw new InvalidOperationException($"Invalid VectorSharp file: record count must be non-negative, got {recordCount}.");

            return (dimension, keySize, recordCount);
        }

        /// <summary>
        /// Writes a single vector record to a stream.
        /// </summary>
        /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="key">The vector identifier.</param>
        /// <param name="magnitude">The pre-computed magnitude.</param>
        /// <param name="values">The vector values.</param>
        internal static void WriteRecord<TKey>(Stream stream, TKey key, float magnitude, ReadOnlySpan<float> values)
            where TKey : unmanaged, IEquatable<TKey>
        {
            // Write status byte (active)
            stream.WriteByte(RecordStatusActive);

            // Write key
            ReadOnlySpan<byte> keyBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref key, 1));
            stream.Write(keyBytes);

            // Write magnitude
            Span<byte> magBytes = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(magBytes, magnitude);
            stream.Write(magBytes);

            // Write vector values
            ReadOnlySpan<byte> valueBytes = MemoryMarshal.AsBytes(values);
            stream.Write(valueBytes);
        }

        /// <summary>
        /// Reads a single vector record from a stream.
        /// </summary>
        /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="dimension">The expected vector dimension.</param>
        /// <returns>A tuple containing the key, magnitude, and vector values.</returns>
        internal static (byte Status, TKey Key, float Magnitude, float[] Values) ReadRecord<TKey>(Stream stream, int dimension)
            where TKey : unmanaged, IEquatable<TKey>
        {
            int keySize = Unsafe.SizeOf<TKey>();
            if (keySize > 1024)
                throw new InvalidOperationException($"Key type is too large for safe stack allocation: {keySize} bytes.");

            // Read status byte
            int statusByte = stream.ReadByte();
            if (statusByte == -1)
                throw new InvalidOperationException("Unexpected end of stream while reading record status.");
            byte status = (byte)statusByte;

            // Read key
            Span<byte> keyBytes = stackalloc byte[keySize];
            stream.ReadExactly(keyBytes);
            TKey key = MemoryMarshal.Read<TKey>(keyBytes);

            // Read magnitude
            Span<byte> magBytes = stackalloc byte[4];
            stream.ReadExactly(magBytes);
            float magnitude = BinaryPrimitives.ReadSingleLittleEndian(magBytes);

            // Read vector values
            float[] values = new float[dimension];
            Span<byte> valueBytes = MemoryMarshal.AsBytes(values.AsSpan());
            stream.ReadExactly(valueBytes);

            return (status, key, magnitude, values);
        }
    }
}
