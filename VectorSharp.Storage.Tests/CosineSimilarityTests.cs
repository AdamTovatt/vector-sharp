namespace VectorSharp.Storage.Tests
{
    public class CosineSimilarityTests
    {
        [Fact]
        public void CalculateMagnitude_UnitVector_ReturnsOne()
        {
            // A unit vector along the first axis
            float[] vector = new float[3];
            vector[0] = 1.0f;

            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            TestHelpers.AssertApproximatelyEqual(1.0f, magnitude, 0.0001f);
        }

        [Fact]
        public void CalculateMagnitude_ZeroVector_ReturnsZero()
        {
            float[] vector = new float[10];

            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            Assert.Equal(0.0f, magnitude);
        }

        [Fact]
        public void CalculateMagnitude_KnownVector_ReturnsExpected()
        {
            // [3, 4] -> magnitude = 5
            float[] vector = new float[] { 3.0f, 4.0f };

            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            TestHelpers.AssertApproximatelyEqual(5.0f, magnitude, 0.0001f);
        }

        [Fact]
        public void Calculate_IdenticalVectors_ReturnsOne()
        {
            float[] vector = TestHelpers.CreateRandomVector(128, seed: 42);
            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            float similarity = CosineSimilarity.Calculate(vector.AsSpan(), magnitude, vector.AsSpan(), magnitude);

            TestHelpers.AssertApproximatelyEqual(1.0f, similarity, 0.0001f);
        }

        [Fact]
        public void Calculate_OrthogonalVectors_ReturnsZero()
        {
            float[] a = new float[] { 1.0f, 0.0f, 0.0f };
            float[] b = new float[] { 0.0f, 1.0f, 0.0f };
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            TestHelpers.AssertApproximatelyEqual(0.0f, similarity, 0.0001f);
        }

        [Fact]
        public void Calculate_OppositeVectors_ReturnsNegativeOne()
        {
            float[] a = new float[] { 1.0f, 2.0f, 3.0f };
            float[] b = new float[] { -1.0f, -2.0f, -3.0f };
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            TestHelpers.AssertApproximatelyEqual(-1.0f, similarity, 0.0001f);
        }

        [Fact]
        public void Calculate_ZeroMagnitude_ReturnsZero()
        {
            float[] a = new float[] { 1.0f, 2.0f, 3.0f };
            float[] b = new float[] { 0.0f, 0.0f, 0.0f };

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), 3.74f, b.AsSpan(), 0.0f);

            Assert.Equal(0.0f, similarity);
        }

        [Fact]
        public void Calculate_768Dimensions_ReturnsCorrectResult()
        {
            float[] a = TestHelpers.CreateRandomVector(768, seed: 1);
            float[] b = TestHelpers.CreateRandomVector(768, seed: 2);
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            // Should be a valid cosine similarity in [-1, 1]
            Assert.True(similarity >= -1.0f && similarity <= 1.0f,
                $"Expected similarity in [-1, 1], got {similarity}");
        }

        [Fact]
        public void Calculate_NonSIMDAlignedDimension_WorksCorrectly()
        {
            // Use a dimension that's not aligned to SIMD width
            float[] a = TestHelpers.CreateRandomVector(13, seed: 1);
            float[] b = TestHelpers.CreateRandomVector(13, seed: 2);
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            Assert.True(similarity >= -1.0f && similarity <= 1.0f,
                $"Expected similarity in [-1, 1], got {similarity}");

            // Also verify identical vectors still return 1.0
            float selfSimilarity = CosineSimilarity.Calculate(a.AsSpan(), magA, a.AsSpan(), magA);
            TestHelpers.AssertApproximatelyEqual(1.0f, selfSimilarity, 0.0001f);
        }
    }
}
