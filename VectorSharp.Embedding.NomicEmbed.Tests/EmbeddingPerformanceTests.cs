using System.Diagnostics;
using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

namespace VectorSharp.Embedding.NomicEmbed.Tests
{
    [Trait("Category", "RequiresModel")]
    public class EmbeddingPerformanceTests
    {
        private const int WarmupCount = 5;
        private const int EmbeddingCount = 100;

        private static string GetModelDirectory()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(EmbeddingPerformanceTests).Assembly.Location)!;
            string modelsDir = Path.Combine(assemblyDir, "Models");

            Skip.IfNot(
                Directory.Exists(modelsDir) && File.Exists(Path.Combine(modelsDir, "model_int8.onnx")),
                "Model files not found. Run tools/download-nomic-model.sh first.");

            return modelsDir;
        }

        private static string[] CreateSampleTexts(int count)
        {
            string[] texts = new string[count];
            for (int i = 0; i < count; i++)
            {
                texts[i] = $"This is sample document number {i} for performance testing of the embedding service.";
            }

            return texts;
        }

        [SkippableFact]
        public async Task PerformanceTest_SingleWorkerThroughput()
        {
            string modelsDir = GetModelDirectory();

            Console.WriteLine($"=== Single Worker Embedding Throughput ===");
            Console.WriteLine($"Embeddings: {EmbeddingCount}, Warmup: {WarmupCount}");
            Console.WriteLine();

            await using EmbeddingService service = new EmbeddingService(
                () => NomicEmbedProvider.Create(modelsDir),
                new EmbeddingServiceOptions { Concurrency = 1 });

            string[] texts = CreateSampleTexts(EmbeddingCount);

            // Warmup
            for (int i = 0; i < WarmupCount; i++)
            {
                await service.EmbedAsync($"warmup text {i}");
            }

            // Timed run
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < EmbeddingCount; i++)
            {
                float[] result = await service.EmbedAsync(texts[i]);
                Assert.Equal(768, result.Length);
            }

            timer.Stop();

            double totalSeconds = timer.Elapsed.TotalSeconds;
            double embeddingsPerSecond = EmbeddingCount / totalSeconds;
            double msPerEmbedding = timer.Elapsed.TotalMilliseconds / EmbeddingCount;

            Console.WriteLine($"Total time: {timer.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"Per embedding: {msPerEmbedding:F2} ms");
            Console.WriteLine($"Throughput: {embeddingsPerSecond:F1} embeddings/second");

            Assert.True(timer.ElapsedMilliseconds > 0);
        }

        [SkippableTheory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public async Task PerformanceTest_ThroughputByConcurrency(int concurrency)
        {
            string modelsDir = GetModelDirectory();

            Console.WriteLine($"=== Embedding Throughput (Concurrency={concurrency}) ===");
            Console.WriteLine($"Embeddings: {EmbeddingCount}, Warmup: {WarmupCount}");
            Console.WriteLine();

            await using EmbeddingService service = new EmbeddingService(
                () => NomicEmbedProvider.Create(modelsDir),
                new EmbeddingServiceOptions { Concurrency = concurrency });

            string[] texts = CreateSampleTexts(EmbeddingCount);

            // Warmup
            Task<float[]>[] warmupTasks = new Task<float[]>[WarmupCount];
            for (int i = 0; i < WarmupCount; i++)
            {
                warmupTasks[i] = service.EmbedAsync($"warmup text {i}");
            }

            await Task.WhenAll(warmupTasks);

            // Timed run — submit all at once, let workers process concurrently
            Stopwatch timer = Stopwatch.StartNew();
            float[][] results = await service.EmbedBatchAsync(texts);
            timer.Stop();

            double totalSeconds = timer.Elapsed.TotalSeconds;
            double embeddingsPerSecond = EmbeddingCount / totalSeconds;
            double msPerEmbedding = timer.Elapsed.TotalMilliseconds / EmbeddingCount;

            Console.WriteLine($"Total time: {timer.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"Per embedding: {msPerEmbedding:F2} ms");
            Console.WriteLine($"Throughput: {embeddingsPerSecond:F1} embeddings/second");

            Assert.Equal(EmbeddingCount, results.Length);
            foreach (float[] result in results)
            {
                Assert.Equal(768, result.Length);
            }
        }

        [SkippableTheory]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(4, 0)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(4, 2)]
        [InlineData(6, 2)]
        [InlineData(6, 3)]
        [InlineData(9, 2)]
        public async Task PerformanceTest_ThreadsVsConcurrency(int concurrency, int intraOpThreads)
        {
            string modelsDir = GetModelDirectory();
            NomicEmbedOptions? nomicOptions = intraOpThreads > 0
                ? new NomicEmbedOptions { IntraOpNumThreads = intraOpThreads }
                : null;

            string threadsLabel = intraOpThreads > 0 ? $"{intraOpThreads}" : "default";
            Console.WriteLine($"=== Threads={threadsLabel}, Concurrency={concurrency} ===");

            long memoryBefore = GC.GetTotalMemory(true);

            await using EmbeddingService service = new EmbeddingService(
                () => NomicEmbedProvider.Create(modelsDir, nomicOptions),
                new EmbeddingServiceOptions { Concurrency = concurrency });

            long memoryAfterInit = GC.GetTotalMemory(true);
            long memoryDeltaMb = (memoryAfterInit - memoryBefore) / 1024 / 1024;
            Console.WriteLine($"Memory for {concurrency} session(s): {memoryDeltaMb} MB ({memoryDeltaMb / concurrency} MB/session)");

            string[] texts = CreateSampleTexts(EmbeddingCount);

            // Warmup
            Task<float[]>[] warmupTasks = new Task<float[]>[WarmupCount];
            for (int i = 0; i < WarmupCount; i++)
            {
                warmupTasks[i] = service.EmbedAsync($"warmup text {i}");
            }

            await Task.WhenAll(warmupTasks);

            // Timed run
            Stopwatch timer = Stopwatch.StartNew();
            float[][] results = await service.EmbedBatchAsync(texts);
            timer.Stop();

            double embeddingsPerSecond = EmbeddingCount / timer.Elapsed.TotalSeconds;
            Console.WriteLine($"Total: {timer.ElapsedMilliseconds:N0} ms, Throughput: {embeddingsPerSecond:F1} embeddings/sec");

            Assert.Equal(EmbeddingCount, results.Length);
        }

        [SkippableFact]
        public async Task PerformanceTest_MaxThroughput()
        {
            string modelsDir = GetModelDirectory();
            int concurrency = 9;
            int intraOpThreads = 2;
            int count = 5000;

            Console.WriteLine($"=== Max Throughput Test ===");
            Console.WriteLine($"Concurrency={concurrency}, IntraOpThreads={intraOpThreads}, Embeddings={count}");
            Console.WriteLine();

            NomicEmbedOptions nomicOptions = new NomicEmbedOptions { IntraOpNumThreads = intraOpThreads };

            long memoryBefore = GC.GetTotalMemory(true);

            await using EmbeddingService service = new EmbeddingService(
                () => NomicEmbedProvider.Create(modelsDir, nomicOptions),
                new EmbeddingServiceOptions { Concurrency = concurrency });

            long memoryAfterInit = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory for {concurrency} sessions: {(memoryAfterInit - memoryBefore) / 1024 / 1024} MB");

            string[] texts = CreateSampleTexts(count);

            // Warmup
            Task<float[]>[] warmupTasks = new Task<float[]>[WarmupCount];
            for (int i = 0; i < WarmupCount; i++)
            {
                warmupTasks[i] = service.EmbedAsync($"warmup text {i}");
            }

            await Task.WhenAll(warmupTasks);

            // Timed run
            Stopwatch timer = Stopwatch.StartNew();
            float[][] results = await service.EmbedBatchAsync(texts);
            timer.Stop();

            double embeddingsPerSecond = count / timer.Elapsed.TotalSeconds;
            Console.WriteLine($"Total: {timer.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"Per embedding: {timer.Elapsed.TotalMilliseconds / count:F2} ms");
            Console.WriteLine($"Throughput: {embeddingsPerSecond:F1} embeddings/sec");

            Assert.Equal(count, results.Length);
        }

        [SkippableTheory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task PerformanceTest_SweetSpotThroughput(int intraOpThreads)
        {
            string modelsDir = GetModelDirectory();
            int concurrency = 2;
            int count = 5000;

            string threadsLabel = intraOpThreads > 0 ? $"{intraOpThreads}" : "default";
            Console.WriteLine($"=== Sweet Spot Throughput Test ===");
            Console.WriteLine($"Concurrency={concurrency}, IntraOpThreads={threadsLabel}, Embeddings={count}");
            Console.WriteLine();

            NomicEmbedOptions? nomicOptions = intraOpThreads > 0
                ? new NomicEmbedOptions { IntraOpNumThreads = intraOpThreads }
                : null;

            long memoryBefore = GC.GetTotalMemory(true);

            await using EmbeddingService service = new EmbeddingService(
                () => NomicEmbedProvider.Create(modelsDir, nomicOptions),
                new EmbeddingServiceOptions { Concurrency = concurrency });

            long memoryAfterInit = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory for {concurrency} sessions: {(memoryAfterInit - memoryBefore) / 1024 / 1024} MB");

            string[] texts = CreateSampleTexts(count);

            // Warmup
            Task<float[]>[] warmupTasks = new Task<float[]>[WarmupCount];
            for (int i = 0; i < WarmupCount; i++)
            {
                warmupTasks[i] = service.EmbedAsync($"warmup text {i}");
            }

            await Task.WhenAll(warmupTasks);

            // Timed run
            Stopwatch timer = Stopwatch.StartNew();
            float[][] results = await service.EmbedBatchAsync(texts);
            timer.Stop();

            double embeddingsPerSecond = count / timer.Elapsed.TotalSeconds;
            Console.WriteLine($"Total: {timer.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"Per embedding: {timer.Elapsed.TotalMilliseconds / count:F2} ms");
            Console.WriteLine($"Throughput: {embeddingsPerSecond:F1} embeddings/sec");

            Assert.Equal(count, results.Length);
        }

        [SkippableFact]
        public async Task PerformanceTest_ConcurrencyScaling()
        {
            string modelsDir = GetModelDirectory();
            int[] concurrencyLevels = new int[] { 1, 2, 4 };

            Console.WriteLine($"=== Concurrency Scaling Comparison ===");
            Console.WriteLine($"Embeddings: {EmbeddingCount}, Warmup: {WarmupCount}");
            Console.WriteLine();

            Dictionary<int, double> results = new Dictionary<int, double>();

            foreach (int concurrency in concurrencyLevels)
            {
                await using EmbeddingService service = new EmbeddingService(
                    () => NomicEmbedProvider.Create(modelsDir),
                    new EmbeddingServiceOptions { Concurrency = concurrency });

                string[] texts = CreateSampleTexts(EmbeddingCount);

                // Warmup
                Task<float[]>[] warmupTasks = new Task<float[]>[WarmupCount];
                for (int i = 0; i < WarmupCount; i++)
                {
                    warmupTasks[i] = service.EmbedAsync($"warmup text {i}");
                }

                await Task.WhenAll(warmupTasks);

                // Timed run
                Stopwatch timer = Stopwatch.StartNew();
                await service.EmbedBatchAsync(texts);
                timer.Stop();

                double embeddingsPerSecond = EmbeddingCount / timer.Elapsed.TotalSeconds;
                results[concurrency] = embeddingsPerSecond;

                Console.WriteLine($"Concurrency {concurrency}: {timer.ElapsedMilliseconds:N0} ms total, {embeddingsPerSecond:F1} embeddings/sec");
            }

            // Scaling summary
            double baseline = results[1];
            Console.WriteLine();
            Console.WriteLine("--- Scaling Summary ---");
            foreach (int concurrency in concurrencyLevels)
            {
                double speedup = results[concurrency] / baseline;
                Console.WriteLine($"Concurrency {concurrency}: {speedup:F2}x vs single worker");
            }

            Assert.True(results[1] > 0);
        }
    }
}
