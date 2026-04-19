namespace VectorSharp.Storage.Tests
{
    internal static class TestHelpers
    {
        /// <summary>
        /// Creates a vector filled with a uniform value.
        /// </summary>
        internal static float[] CreateVector(float value, int dimension)
        {
            float[] vector = new float[dimension];
            Array.Fill(vector, value);
            return vector;
        }

        /// <summary>
        /// Creates a random vector with values scaled by the given factor.
        /// </summary>
        internal static float[] CreateRandomVector(int dimension, float scale = 1.0f, int? seed = null)
        {
            Random random = seed.HasValue ? new Random(seed.Value) : new Random();
            float[] vector = new float[dimension];
            for (int i = 0; i < dimension; i++)
            {
                vector[i] = (float)(random.NextDouble() * 2 - 1) * scale;
            }

            return vector;
        }

        /// <summary>
        /// Creates an in-memory store populated with random vectors.
        /// </summary>
        internal static async Task<CosineVectorStore<Guid>> CreatePopulatedStoreAsync(
            string name, int dimension, int vectorCount, int seed = 42)
        {
            CosineVectorStore<Guid> store = new CosineVectorStore<Guid>(name, dimension);
            Random random = new Random(seed);

            for (int i = 0; i < vectorCount; i++)
            {
                float[] values = new float[dimension];
                for (int j = 0; j < dimension; j++)
                {
                    values[j] = (float)(random.NextDouble() * 2 - 1);
                }

                await store.AddAsync(Guid.NewGuid(), values);
            }

            return store;
        }

        /// <summary>
        /// Creates an in-memory store populated with random vectors using int keys.
        /// </summary>
        internal static async Task<CosineVectorStore<int>> CreatePopulatedIntStoreAsync(
            string name, int dimension, int vectorCount, int seed = 42)
        {
            CosineVectorStore<int> store = new CosineVectorStore<int>(name, dimension);
            Random random = new Random(seed);

            for (int i = 0; i < vectorCount; i++)
            {
                float[] values = new float[dimension];
                for (int j = 0; j < dimension; j++)
                {
                    values[j] = (float)(random.NextDouble() * 2 - 1);
                }

                await store.AddAsync(i, values);
            }

            return store;
        }

        /// <summary>
        /// Asserts two floats are equal within an absolute tolerance.
        /// </summary>
        internal static void AssertApproximatelyEqual(float expected, float actual, float tolerance, string? message = null)
        {
            float diff = MathF.Abs(expected - actual);
            Assert.True(diff <= tolerance,
                message ?? $"Expected {expected} but got {actual} (diff {diff} exceeds tolerance {tolerance})");
        }
    }
}
