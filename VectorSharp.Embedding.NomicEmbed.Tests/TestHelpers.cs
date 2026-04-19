namespace VectorSharp.Embedding.NomicEmbed.Tests
{
    internal static class TestHelpers
    {
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
