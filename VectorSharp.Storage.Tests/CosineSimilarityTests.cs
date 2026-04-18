namespace VectorSharp.Storage.Tests
{
    [TestClass]
    public class CosineSimilarityTests
    {
        [TestMethod]
        public void CalculateMagnitude_UnitVector_ReturnsOne()
        {
            // A unit vector along the first axis
            float[] vector = new float[3];
            vector[0] = 1.0f;

            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            Assert.AreEqual(1.0f, magnitude, 0.0001f);
        }

        [TestMethod]
        public void CalculateMagnitude_ZeroVector_ReturnsZero()
        {
            float[] vector = new float[10];

            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            Assert.AreEqual(0.0f, magnitude);
        }

        [TestMethod]
        public void CalculateMagnitude_KnownVector_ReturnsExpected()
        {
            // [3, 4] -> magnitude = 5
            float[] vector = new float[] { 3.0f, 4.0f };

            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            Assert.AreEqual(5.0f, magnitude, 0.0001f);
        }

        [TestMethod]
        public void Calculate_IdenticalVectors_ReturnsOne()
        {
            float[] vector = TestHelpers.CreateRandomVector(128, seed: 42);
            float magnitude = CosineSimilarity.CalculateMagnitude(vector.AsSpan());

            float similarity = CosineSimilarity.Calculate(vector.AsSpan(), magnitude, vector.AsSpan(), magnitude);

            Assert.AreEqual(1.0f, similarity, 0.0001f);
        }

        [TestMethod]
        public void Calculate_OrthogonalVectors_ReturnsZero()
        {
            float[] a = new float[] { 1.0f, 0.0f, 0.0f };
            float[] b = new float[] { 0.0f, 1.0f, 0.0f };
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            Assert.AreEqual(0.0f, similarity, 0.0001f);
        }

        [TestMethod]
        public void Calculate_OppositeVectors_ReturnsNegativeOne()
        {
            float[] a = new float[] { 1.0f, 2.0f, 3.0f };
            float[] b = new float[] { -1.0f, -2.0f, -3.0f };
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            Assert.AreEqual(-1.0f, similarity, 0.0001f);
        }

        [TestMethod]
        public void Calculate_ZeroMagnitude_ReturnsZero()
        {
            float[] a = new float[] { 1.0f, 2.0f, 3.0f };
            float[] b = new float[] { 0.0f, 0.0f, 0.0f };

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), 3.74f, b.AsSpan(), 0.0f);

            Assert.AreEqual(0.0f, similarity);
        }

        [TestMethod]
        public void Calculate_768Dimensions_ReturnsCorrectResult()
        {
            float[] a = TestHelpers.CreateRandomVector(768, seed: 1);
            float[] b = TestHelpers.CreateRandomVector(768, seed: 2);
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            // Should be a valid cosine similarity in [-1, 1]
            Assert.IsTrue(similarity >= -1.0f && similarity <= 1.0f,
                $"Expected similarity in [-1, 1], got {similarity}");
        }

        [TestMethod]
        public void Calculate_NonSIMDAlignedDimension_WorksCorrectly()
        {
            // Use a dimension that's not aligned to SIMD width
            float[] a = TestHelpers.CreateRandomVector(13, seed: 1);
            float[] b = TestHelpers.CreateRandomVector(13, seed: 2);
            float magA = CosineSimilarity.CalculateMagnitude(a.AsSpan());
            float magB = CosineSimilarity.CalculateMagnitude(b.AsSpan());

            float similarity = CosineSimilarity.Calculate(a.AsSpan(), magA, b.AsSpan(), magB);

            Assert.IsTrue(similarity >= -1.0f && similarity <= 1.0f,
                $"Expected similarity in [-1, 1], got {similarity}");

            // Also verify identical vectors still return 1.0
            float selfSimilarity = CosineSimilarity.Calculate(a.AsSpan(), magA, a.AsSpan(), magA);
            Assert.AreEqual(1.0f, selfSimilarity, 0.0001f);
        }
    }
}
