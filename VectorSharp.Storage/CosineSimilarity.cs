using System.Numerics;
using System.Runtime.CompilerServices;

namespace VectorSharp.Storage
{
    /// <summary>
    /// Internal static helper for SIMD-optimized cosine similarity computation.
    /// Designed to work with pre-computed magnitudes, so the hot loop only computes
    /// the dot product between two vectors.
    /// </summary>
    internal static class CosineSimilarity
    {
        /// <summary>
        /// Calculates the magnitude (L2 norm) of a vector.
        /// </summary>
        /// <param name="vector">The vector to compute the magnitude of.</param>
        /// <returns>The magnitude of the vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float CalculateMagnitude(ReadOnlySpan<float> vector)
        {
            float sum = 0.0f;

            if (Vector.IsHardwareAccelerated && vector.Length >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorCount = vector.Length / vectorSize;

                for (int i = 0; i < vectorCount; i++)
                {
                    int offset = i * vectorSize;
                    Vector<float> vec = new Vector<float>(vector.Slice(offset, vectorSize));
                    sum += Vector.Dot(vec, vec);
                }

                for (int i = vectorCount * vectorSize; i < vector.Length; i++)
                {
                    sum += vector[i] * vector[i];
                }
            }
            else
            {
                for (int i = 0; i < vector.Length; i++)
                {
                    sum += vector[i] * vector[i];
                }
            }

            return MathF.Sqrt(sum);
        }

        /// <summary>
        /// Calculates the cosine similarity between a query vector and a stored vector
        /// using pre-computed magnitudes.
        /// </summary>
        /// <param name="queryVector">The query vector.</param>
        /// <param name="queryMagnitude">The pre-computed magnitude of the query vector.</param>
        /// <param name="storedVector">The stored vector to compare against.</param>
        /// <param name="storedMagnitude">The pre-computed magnitude of the stored vector.</param>
        /// <returns>The cosine similarity between the two vectors, or 0 if either magnitude is zero.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float Calculate(
            ReadOnlySpan<float> queryVector,
            float queryMagnitude,
            ReadOnlySpan<float> storedVector,
            float storedMagnitude)
        {
            if (queryMagnitude == 0 || storedMagnitude == 0)
                return 0.0f;

            float dotProduct = CalculateDotProduct(queryVector, storedVector);
            return dotProduct / (queryMagnitude * storedMagnitude);
        }

        /// <summary>
        /// Calculates the dot product of two vectors using SIMD when available.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CalculateDotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            float dotProduct = 0.0f;

            if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorCount = a.Length / vectorSize;

                for (int i = 0; i < vectorCount; i++)
                {
                    int offset = i * vectorSize;
                    Vector<float> vecA = new Vector<float>(a.Slice(offset, vectorSize));
                    Vector<float> vecB = new Vector<float>(b.Slice(offset, vectorSize));
                    dotProduct += Vector.Dot(vecA, vecB);
                }

                for (int i = vectorCount * vectorSize; i < a.Length; i++)
                {
                    dotProduct += a[i] * b[i];
                }
            }
            else
            {
                for (int i = 0; i < a.Length; i++)
                {
                    dotProduct += a[i] * b[i];
                }
            }

            return dotProduct;
        }
    }
}
