using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

namespace VectorSharp.Embedding.NomicEmbed.Tests
{
    [Trait("Category", "RequiresModel")]
    public class NomicEmbedProviderTests
    {
        private static string GetModelDirectory()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(NomicEmbedProviderTests).Assembly.Location)!;
            string modelsDir = Path.Combine(assemblyDir, "Models");

            Skip.IfNot(
                Directory.Exists(modelsDir) && File.Exists(Path.Combine(modelsDir, "model_int8.onnx")),
                "Model files not found. Run tools/download-nomic-model.sh first.");

            return modelsDir;
        }

        #region Factory

        [SkippableFact]
        public void Create_CustomPath_Succeeds()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            Assert.Equal(768, provider.Dimension);
        }

        [Fact]
        public void Create_InvalidPath_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                NomicEmbedProvider.Create("/nonexistent/path"));
        }

        #endregion

        #region Embedding

        [SkippableFact]
        public async Task EmbedAsync_ShortText_ReturnsVector768()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result = await provider.EmbedAsync("hello world");

            Assert.Equal(768, result.Length);
        }

        [SkippableFact]
        public async Task EmbedAsync_SameText_ReturnsSameVector()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result1 = await provider.EmbedAsync("deterministic test");
            float[] result2 = await provider.EmbedAsync("deterministic test");

            Assert.Equal(result1, result2);
        }

        [SkippableFact]
        public async Task EmbedAsync_DifferentTexts_ReturnsDifferentVectors()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result1 = await provider.EmbedAsync("cats are great");
            float[] result2 = await provider.EmbedAsync("quantum physics equations");

            Assert.NotEqual(result1, result2);
        }

        [SkippableFact]
        public async Task EmbedAsync_ResultIsL2Normalized()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result = await provider.EmbedAsync("test normalization");

            float magnitude = 0;
            for (int i = 0; i < result.Length; i++)
            {
                magnitude += result[i] * result[i];
            }

            magnitude = MathF.Sqrt(magnitude);

            TestHelpers.AssertApproximatelyEqual(1.0f, magnitude, 0.001f);
        }

        [SkippableFact]
        public async Task EmbedAsync_EmptyString_ReturnsVector768()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result = await provider.EmbedAsync("");

            Assert.Equal(768, result.Length);
        }

        [SkippableFact]
        public async Task EmbedAsync_SimilarTexts_HaveHigherSimilarity()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] catResult = await provider.EmbedAsync("I love cats");
            float[] kittenResult = await provider.EmbedAsync("I love kittens");
            float[] mathResult = await provider.EmbedAsync("differential equations");

            float catKittenSimilarity = CosineSimilarity(catResult, kittenResult);
            float catMathSimilarity = CosineSimilarity(catResult, mathResult);

            Assert.True(catKittenSimilarity > catMathSimilarity,
                $"Cat-kitten similarity ({catKittenSimilarity:F4}) should be higher than cat-math ({catMathSimilarity:F4})");
        }

        #endregion

        #region Task Prefix

        [SkippableFact]
        public async Task EmbedAsync_QueryPurpose_ReturnsDifferentFromDocumentPurpose()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] docResult = await provider.EmbedAsync("test text", EmbeddingPurpose.Document);
            float[] queryResult = await provider.EmbedAsync("test text", EmbeddingPurpose.Query);

            Assert.NotEqual(docResult, queryResult);
        }

        #endregion

        #region Integration with EmbeddingService

        [SkippableFact]
        public async Task EmbeddingService_WithNomicProvider_ProducesValidEmbeddings()
        {
            string modelsDir = GetModelDirectory();

            await using EmbeddingService service = new EmbeddingService(
                () => NomicEmbedProvider.Create(modelsDir));

            Assert.Equal(768, service.Dimension);

            float[] result = await service.EmbedAsync("integration test");
            Assert.Equal(768, result.Length);
        }

        #endregion

        #region Disposal

        [SkippableFact]
        public async Task Dispose_ThenEmbed_Throws()
        {
            string modelsDir = GetModelDirectory();
            NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);
            provider.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                provider.EmbedAsync("hello"));
        }

        #endregion

        #region Reference Validation

        [SkippableFact]
        public async Task EmbedAsync_HelloWorldDocument_MatchesPythonReference()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result = await provider.EmbedAsync("hello world", EmbeddingPurpose.Document);

            // Reference values from Python: onnxruntime with same int8 ONNX model
            // Input: "search_document: hello world", mean pooling + L2 normalize
            // Token IDs: [101, 3945, 1035, 6254, 1024, 7592, 2088, 102]
            float[] expectedFirst10 = new float[]
            {
                -0.007602f, 0.014549f, -0.161419f, 0.017526f, 0.00672f,
                0.045571f, 0.000487f, -0.051821f, -0.015881f, -0.057081f
            };
            float[] expectedLast5 = new float[]
            {
                0.005951f, -0.019406f, -0.03468f, -0.063794f, -0.042423f
            };

            float tolerance = 0.01f;

            for (int i = 0; i < 10; i++)
            {
                TestHelpers.AssertApproximatelyEqual(expectedFirst10[i], result[i], tolerance,
                    $"Mismatch at index {i}: expected {expectedFirst10[i]:F6}, got {result[i]:F6}");
            }

            for (int i = 0; i < 5; i++)
            {
                int idx = 768 - 5 + i;
                TestHelpers.AssertApproximatelyEqual(expectedLast5[i], result[idx], tolerance,
                    $"Mismatch at index {idx}: expected {expectedLast5[i]:F6}, got {result[idx]:F6}");
            }
        }

        [SkippableFact]
        public async Task EmbedAsync_HelloWorldQuery_MatchesPythonReference()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result = await provider.EmbedAsync("hello world", EmbeddingPurpose.Query);

            // Reference values from Python: onnxruntime with same int8 ONNX model
            // Input: "search_query: hello world"
            // Token IDs: [101, 3945, 1035, 23032, 1024, 7592, 2088, 102]
            float[] expectedFirst10 = new float[]
            {
                -0.003791f, 0.027998f, -0.203349f, 0.012004f, -0.029421f,
                0.056996f, 0.020115f, -0.06122f, -0.020299f, -0.058875f
            };
            float[] expectedLast5 = new float[]
            {
                -0.010278f, -0.00181f, -0.033111f, -0.051937f, -0.006729f
            };

            float tolerance = 0.01f;

            for (int i = 0; i < 10; i++)
            {
                TestHelpers.AssertApproximatelyEqual(expectedFirst10[i], result[i], tolerance,
                    $"Mismatch at index {i}: expected {expectedFirst10[i]:F6}, got {result[i]:F6}");
            }

            for (int i = 0; i < 5; i++)
            {
                int idx = 768 - 5 + i;
                TestHelpers.AssertApproximatelyEqual(expectedLast5[i], result[idx], tolerance,
                    $"Mismatch at index {idx}: expected {expectedLast5[i]:F6}, got {result[idx]:F6}");
            }
        }

        [SkippableFact]
        public async Task EmbedAsync_FoxSentence_MatchesPythonReference()
        {
            string modelsDir = GetModelDirectory();
            using NomicEmbedProvider provider = NomicEmbedProvider.Create(modelsDir);

            float[] result = await provider.EmbedAsync("The quick brown fox jumps over the lazy dog", EmbeddingPurpose.Document);

            // Reference values from Python: onnxruntime with same int8 ONNX model
            // Input: "search_document: The quick brown fox jumps over the lazy dog"
            // Token IDs: [101, 3945, 1035, 6254, 1024, 1996, 4248, 2829, 4419, 14523, 2058, 1996, 13971, 3899, 102]
            float[] expectedFirst10 = new float[]
            {
                -0.003585f, 0.05116f, -0.171808f, -0.013482f, 0.015521f,
                0.009127f, -0.02261f, -0.006359f, 0.010174f, -0.082323f
            };
            float[] expectedLast5 = new float[]
            {
                -0.028945f, -0.044729f, -0.041593f, -0.078133f, -0.013426f
            };

            float tolerance = 0.01f;

            for (int i = 0; i < 10; i++)
            {
                TestHelpers.AssertApproximatelyEqual(expectedFirst10[i], result[i], tolerance,
                    $"Mismatch at index {i}: expected {expectedFirst10[i]:F6}, got {result[i]:F6}");
            }

            for (int i = 0; i < 5; i++)
            {
                int idx = 768 - 5 + i;
                TestHelpers.AssertApproximatelyEqual(expectedLast5[i], result[idx], tolerance,
                    $"Mismatch at index {idx}: expected {expectedLast5[i]:F6}, got {result[idx]:F6}");
            }
        }

        #endregion

        #region Helpers

        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
        }

        #endregion
    }
}
