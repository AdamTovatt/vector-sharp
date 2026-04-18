namespace VectorSharp.Storage
{
    /// <summary>
    /// Internal representation of a stored vector with a pre-computed magnitude.
    /// </summary>
    /// <typeparam name="TKey">The type of the vector identifier.</typeparam>
    internal readonly struct StoredVector<TKey>
        where TKey : unmanaged, IEquatable<TKey>
    {
        /// <summary>
        /// The unique identifier of the vector.
        /// </summary>
        public readonly TKey Id;

        /// <summary>
        /// The vector values as a float array.
        /// </summary>
        public readonly float[] Values;

        /// <summary>
        /// The pre-computed magnitude (L2 norm) of the vector.
        /// </summary>
        public readonly float Magnitude;

        /// <summary>
        /// Initializes a new instance of the <see cref="StoredVector{TKey}"/> struct.
        /// </summary>
        /// <param name="id">The unique identifier of the vector.</param>
        /// <param name="values">The vector values.</param>
        /// <param name="magnitude">The pre-computed magnitude of the vector.</param>
        public StoredVector(TKey id, float[] values, float magnitude)
        {
            Id = id;
            Values = values;
            Magnitude = magnitude;
        }

        /// <summary>
        /// Gets a read-only span over the vector values for high-performance operations.
        /// </summary>
        /// <returns>A read-only span over the vector values.</returns>
        public ReadOnlySpan<float> GetSpan() => Values.AsSpan();
    }
}
