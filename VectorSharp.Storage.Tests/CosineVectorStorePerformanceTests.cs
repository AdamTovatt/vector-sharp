using System.Diagnostics;

namespace VectorSharp.Storage.Tests
{
    public class CosineVectorStorePerformanceTests
    {
        #region Constants

        private const int LargeDatasetSize = 10_000;
        private const int VectorDimension = 768;
        private const int SearchQueryCount = 100;

        #endregion

        #region Test Helpers

        private static float[] CreateRandomVector(int dimension = VectorDimension, float scale = 1f)
        {
            Random rng = new Random();
            float[] vec = new float[dimension];
            for (int i = 0; i < dimension; i++)
                vec[i] = (float)(rng.NextDouble() * scale);
            return vec;
        }

        private static List<(Guid Id, float[] Values)> CreateLargeDataset(int count)
        {
            List<(Guid, float[])> vectors = new List<(Guid, float[])>(count);
            for (int i = 0; i < count; i++)
            {
                vectors.Add((Guid.NewGuid(), CreateRandomVector()));
            }

            return vectors;
        }

        private static List<float[]> CreateSearchQueries(int count)
        {
            List<float[]> queries = new List<float[]>(count);
            for (int i = 0; i < count; i++)
            {
                queries.Add(CreateRandomVector());
            }

            return queries;
        }

        #endregion

        [Fact]
        public async Task PerformanceTest_LargeDatasetOperations()
        {
            Console.WriteLine($"=== Performance Test with {LargeDatasetSize:N0} vectors ===");
            Console.WriteLine($"Vector dimension: {VectorDimension}");
            Console.WriteLine();

            // Create large dataset
            Console.WriteLine("Creating large dataset...");
            List<(Guid Id, float[] Values)> vectors = CreateLargeDataset(LargeDatasetSize);
            Console.WriteLine($"Created {LargeDatasetSize:N0} vectors");

            // Test insertion performance with memory tracking
            Console.WriteLine("\n--- Insertion Performance ---");
            using CosineVectorStore<Guid> store = new CosineVectorStore<Guid>("perf-test", VectorDimension);

            long memoryBeforeInsertion = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory before insertion: {memoryBeforeInsertion / 1024 / 1024:N0} MB");

            Stopwatch insertionTimer = Stopwatch.StartNew();
            foreach ((Guid id, float[] values) in vectors)
            {
                await store.AddAsync(id, values);
            }

            insertionTimer.Stop();

            long memoryAfterInsertion = GC.GetTotalMemory(true);
            long insertionMemoryDelta = memoryAfterInsertion - memoryBeforeInsertion;

            long insertionTimeMs = insertionTimer.ElapsedMilliseconds;
            double insertionTimePerVectorNs = (double)insertionTimer.ElapsedTicks * 1_000_000 / TimeSpan.TicksPerSecond / LargeDatasetSize;

            Console.WriteLine($"Memory after insertion: {memoryAfterInsertion / 1024 / 1024:N0} MB");
            Console.WriteLine($"Memory delta for insertion: {insertionMemoryDelta / 1024 / 1024:N0} MB");
            Console.WriteLine($"Memory per vector: {insertionMemoryDelta / (double)LargeDatasetSize / 1024:F2} KB");
            Console.WriteLine($"Total insertion time: {insertionTimeMs:N0} ms");
            Console.WriteLine($"Average time per vector: {insertionTimePerVectorNs:F2} ns");
            Console.WriteLine($"Insertion rate: {LargeDatasetSize / (insertionTimeMs / 1000.0):F0} vectors/second");

            // Test save performance
            Console.WriteLine("\n--- Save Performance ---");
            string tempFilePath = Path.GetTempFileName();

            Stopwatch saveTimer = Stopwatch.StartNew();
            using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await store.SaveAsync(fileStream);
            }

            saveTimer.Stop();

            long saveTimeMs = saveTimer.ElapsedMilliseconds;
            Console.WriteLine($"Save time: {saveTimeMs:N0} ms");
            Console.WriteLine($"Save rate: {LargeDatasetSize / (saveTimeMs / 1000.0):F0} vectors/second");

            // Test load performance with memory tracking
            Console.WriteLine("\n--- Load Performance ---");
            using CosineVectorStore<Guid> loadedStore = new CosineVectorStore<Guid>("loaded", VectorDimension);

            long memoryBeforeLoad = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory before loading: {memoryBeforeLoad / 1024 / 1024:N0} MB");

            Stopwatch loadTimer = Stopwatch.StartNew();
            using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Open))
            {
                await loadedStore.LoadAsync(fileStream);
            }

            loadTimer.Stop();

            long memoryAfterLoad = GC.GetTotalMemory(true);
            long loadMemoryDelta = memoryAfterLoad - memoryBeforeLoad;

            long loadTimeMs = loadTimer.ElapsedMilliseconds;
            Console.WriteLine($"Memory after loading: {memoryAfterLoad / 1024 / 1024:N0} MB");
            Console.WriteLine($"Memory delta for loading: {loadMemoryDelta / 1024 / 1024:N0} MB");
            Console.WriteLine($"Load time: {loadTimeMs:N0} ms");
            Console.WriteLine($"Load rate: {LargeDatasetSize / (loadTimeMs / 1000.0):F0} vectors/second");

            // Test search performance - in-memory store
            Console.WriteLine("\n--- In-Memory Search Performance ---");
            List<float[]> searchQueries = CreateSearchQueries(SearchQueryCount);

            Stopwatch searchTimer = Stopwatch.StartNew();
            foreach (float[] query in searchQueries)
            {
                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(query, 10);
                Assert.True(results.Count > 0);
            }

            searchTimer.Stop();

            long searchTimeMs = searchTimer.ElapsedMilliseconds;
            double searchTimePerQueryMs = (double)searchTimeMs / SearchQueryCount;
            Console.WriteLine($"Total search time for {SearchQueryCount:N0} queries: {searchTimeMs:N0} ms");
            Console.WriteLine($"Average time per search query: {searchTimePerQueryMs:F2} ms");
            Console.WriteLine($"Search rate: {SearchQueryCount / (searchTimeMs / 1000.0):F0} queries/second");

            // Test search performance - loaded store
            Console.WriteLine("\n--- Loaded Store Search Performance ---");
            Stopwatch loadedSearchTimer = Stopwatch.StartNew();
            foreach (float[] query in searchQueries)
            {
                IReadOnlyList<SearchResult<Guid>> results = await loadedStore.FindMostSimilarAsync(query, 10);
                Assert.True(results.Count > 0);
            }

            loadedSearchTimer.Stop();

            long loadedSearchTimeMs = loadedSearchTimer.ElapsedMilliseconds;
            double loadedSearchTimePerQueryMs = (double)loadedSearchTimeMs / SearchQueryCount;
            Console.WriteLine($"Total search time for {SearchQueryCount:N0} queries: {loadedSearchTimeMs:N0} ms");
            Console.WriteLine($"Average time per search query: {loadedSearchTimePerQueryMs:F2} ms");

            // Performance comparison
            Console.WriteLine("\n--- Performance Comparison ---");
            Console.WriteLine($"Original store memory: {insertionMemoryDelta / 1024 / 1024:N0} MB");
            Console.WriteLine($"Loaded store memory: {loadMemoryDelta / 1024 / 1024:N0} MB");
            Console.WriteLine($"Original search: {searchTimePerQueryMs:F2} ms/query");
            Console.WriteLine($"Loaded search: {loadedSearchTimePerQueryMs:F2} ms/query");

            // Cleanup
            try { File.Delete(tempFilePath); } catch { }

            Console.WriteLine("\n=== Performance Test Complete ===");

            Assert.Equal(LargeDatasetSize, store.Count);
            Assert.True(insertionTimeMs > 0);
            Assert.True(saveTimeMs > 0);
            Assert.True(loadTimeMs > 0);
            Assert.True(searchTimeMs > 0);
        }

        [Fact]
        public async Task PerformanceTest_MemoryUsage()
        {
            Console.WriteLine($"=== Memory Usage Test with {LargeDatasetSize:N0} vectors ===");

            long initialMemory = GC.GetTotalMemory(true);
            Console.WriteLine($"Initial memory: {initialMemory / 1024 / 1024:N0} MB");

            CosineVectorStore<Guid>? store = new CosineVectorStore<Guid>("mem-test", VectorDimension);
            List<(Guid Id, float[] Values)>? vectors = CreateLargeDataset(LargeDatasetSize);

            foreach ((Guid id, float[] values) in vectors)
            {
                await store.AddAsync(id, values);
            }

            long populatedMemory = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory after populating store: {populatedMemory / 1024 / 1024:N0} MB");
            Console.WriteLine($"Memory used by store: {(populatedMemory - initialMemory) / 1024 / 1024:N0} MB");

            // Perform searches to check memory stability
            List<float[]>? searchQueries = CreateSearchQueries(100);
            foreach (float[] query in searchQueries)
            {
                IReadOnlyList<SearchResult<Guid>> results = await store.FindMostSimilarAsync(query, 10);
                Assert.True(results.Count > 0);
            }

            long afterSearchMemory = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory after searches: {afterSearchMemory / 1024 / 1024:N0} MB");

            // Cleanup and verify memory reclamation
            store.Dispose();
            store = null;
            vectors = null;
            searchQueries = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long finalMemory = GC.GetTotalMemory(true);
            Console.WriteLine($"Memory after cleanup: {finalMemory / 1024 / 1024:N0} MB");
            Console.WriteLine($"Memory reclaimed: {(afterSearchMemory - finalMemory) / 1024 / 1024:N0} MB");

            Console.WriteLine("=== Memory Usage Test Complete ===");

            Assert.True(populatedMemory > initialMemory);
        }

        [Fact]
        public async Task PerformanceTest_ConcurrentOperations()
        {
            Console.WriteLine($"=== Concurrent Operations Test with {LargeDatasetSize:N0} vectors ===");
            Console.WriteLine($"Vector dimension: {VectorDimension}");
            Console.WriteLine($"Search queries: {SearchQueryCount:N0}");
            Console.WriteLine();

            List<(Guid Id, float[] Values)> vectors = CreateLargeDataset(LargeDatasetSize);
            List<float[]> searchQueries = CreateSearchQueries(SearchQueryCount);

            // Concurrent insertion
            Console.WriteLine("--- Concurrent Insertion Performance ---");
            using CosineVectorStore<Guid> concurrentStore = new CosineVectorStore<Guid>("concurrent", VectorDimension);

            long memoryBeforeConcurrent = GC.GetTotalMemory(true);

            Stopwatch concurrentInsertionTimer = Stopwatch.StartNew();

            List<Task> insertionTasks = new List<Task>();
            foreach ((Guid id, float[] values) in vectors)
            {
                insertionTasks.Add(concurrentStore.AddAsync(id, values));
            }

            await Task.WhenAll(insertionTasks);
            concurrentInsertionTimer.Stop();

            long memoryAfterConcurrent = GC.GetTotalMemory(true);
            long concurrentMemoryDelta = memoryAfterConcurrent - memoryBeforeConcurrent;

            long concurrentInsertionTimeMs = concurrentInsertionTimer.ElapsedMilliseconds;
            Console.WriteLine($"Memory delta: {concurrentMemoryDelta / 1024 / 1024:N0} MB");
            Console.WriteLine($"Concurrent insertion time: {concurrentInsertionTimeMs:N0} ms");
            Console.WriteLine($"Rate: {LargeDatasetSize / (concurrentInsertionTimeMs / 1000.0):F0} vectors/second");

            // Concurrent search
            Console.WriteLine("\n--- Concurrent Search Performance ---");
            Stopwatch concurrentSearchTimer = Stopwatch.StartNew();

            List<Task> concurrentSearchTasks = new List<Task>();
            foreach (float[] query in searchQueries)
            {
                concurrentSearchTasks.Add(Task.Run(async () =>
                {
                    IReadOnlyList<SearchResult<Guid>> results = await concurrentStore.FindMostSimilarAsync(query, 10);
                    Assert.True(results.Count > 0);
                }));
            }

            await Task.WhenAll(concurrentSearchTasks);
            concurrentSearchTimer.Stop();

            long concurrentSearchTimeMs = concurrentSearchTimer.ElapsedMilliseconds;
            Console.WriteLine($"Concurrent search time: {concurrentSearchTimeMs:N0} ms");
            Console.WriteLine($"Per query: {(double)concurrentSearchTimeMs / SearchQueryCount:F2} ms");

            // Sequential comparison
            Console.WriteLine("\n--- Sequential Comparison ---");
            using CosineVectorStore<Guid> sequentialStore = new CosineVectorStore<Guid>("sequential", VectorDimension);

            Stopwatch sequentialInsertionTimer = Stopwatch.StartNew();
            foreach ((Guid id, float[] values) in vectors)
            {
                await sequentialStore.AddAsync(id, values);
            }

            sequentialInsertionTimer.Stop();
            long sequentialInsertionTimeMs = sequentialInsertionTimer.ElapsedMilliseconds;
            Console.WriteLine($"Sequential insertion time: {sequentialInsertionTimeMs:N0} ms");

            Stopwatch sequentialSearchTimer = Stopwatch.StartNew();
            foreach (float[] query in searchQueries)
            {
                IReadOnlyList<SearchResult<Guid>> results = await sequentialStore.FindMostSimilarAsync(query, 10);
                Assert.True(results.Count > 0);
            }

            sequentialSearchTimer.Stop();
            long sequentialSearchTimeMs = sequentialSearchTimer.ElapsedMilliseconds;
            Console.WriteLine($"Sequential search time: {sequentialSearchTimeMs:N0} ms");
            Console.WriteLine($"Per query: {(double)sequentialSearchTimeMs / SearchQueryCount:F2} ms");

            // Comparison
            Console.WriteLine("\n--- Performance Comparison ---");
            double insertionSpeedup = (double)sequentialInsertionTimeMs / concurrentInsertionTimeMs;
            double searchSpeedup = (double)sequentialSearchTimeMs / concurrentSearchTimeMs;
            Console.WriteLine($"Insertion speedup (concurrent vs sequential): {insertionSpeedup:F2}x");
            Console.WriteLine($"Search speedup (concurrent vs sequential): {searchSpeedup:F2}x");
            Console.WriteLine($"Per-core insertion efficiency: {insertionSpeedup / Environment.ProcessorCount:F2}x");
            Console.WriteLine($"Per-core search efficiency: {searchSpeedup / Environment.ProcessorCount:F2}x");

            Console.WriteLine("\n=== Concurrent Operations Test Complete ===");

            Assert.True(concurrentInsertionTimeMs > 0);
            Assert.True(concurrentSearchTimeMs > 0);
            Assert.True(sequentialInsertionTimeMs > 0);
            Assert.True(sequentialSearchTimeMs > 0);
        }

        [Fact]
        public async Task PerformanceTest_DiskVsInMemory()
        {
            Console.WriteLine($"=== Disk vs In-Memory Performance Test ===");
            Console.WriteLine($"Vectors: {LargeDatasetSize:N0}, Dimension: {VectorDimension}");
            Console.WriteLine();

            List<(Guid Id, float[] Values)> vectors = CreateLargeDataset(LargeDatasetSize);
            List<float[]> searchQueries = CreateSearchQueries(SearchQueryCount);
            string filePath = Path.Combine(Path.GetTempPath(), $"vectorsharp_perf_{Guid.NewGuid()}.dat");

            try
            {
                // Populate in-memory store
                using CosineVectorStore<Guid> memStore = new CosineVectorStore<Guid>("in-memory", VectorDimension);
                foreach ((Guid id, float[] values) in vectors)
                {
                    await memStore.AddAsync(id, values);
                }

                // Save to file for disk store
                using (FileStream fs = File.Create(filePath))
                {
                    await memStore.SaveAsync(fs);
                }

                // Open disk store
                using DiskVectorStore<Guid> diskStore = new DiskVectorStore<Guid>("disk", filePath, VectorDimension);
                Assert.Equal(LargeDatasetSize, diskStore.Count);

                // In-memory search
                Console.WriteLine("--- In-Memory Search ---");
                Stopwatch memTimer = Stopwatch.StartNew();
                foreach (float[] query in searchQueries)
                {
                    IReadOnlyList<SearchResult<Guid>> results = await memStore.FindMostSimilarAsync(query, 10);
                    Assert.True(results.Count > 0);
                }

                memTimer.Stop();
                double memTimePerQuery = (double)memTimer.ElapsedMilliseconds / SearchQueryCount;
                Console.WriteLine($"Total: {memTimer.ElapsedMilliseconds:N0} ms");
                Console.WriteLine($"Per query: {memTimePerQuery:F2} ms");

                // Disk search
                Console.WriteLine("\n--- Disk-Backed Search ---");
                Stopwatch diskTimer = Stopwatch.StartNew();
                foreach (float[] query in searchQueries)
                {
                    IReadOnlyList<SearchResult<Guid>> results = await diskStore.FindMostSimilarAsync(query, 10);
                    Assert.True(results.Count > 0);
                }

                diskTimer.Stop();
                double diskTimePerQuery = (double)diskTimer.ElapsedMilliseconds / SearchQueryCount;
                Console.WriteLine($"Total: {diskTimer.ElapsedMilliseconds:N0} ms");
                Console.WriteLine($"Per query: {diskTimePerQuery:F2} ms");

                // Comparison
                Console.WriteLine("\n--- Comparison ---");
                double slowdown = diskTimePerQuery / memTimePerQuery;
                Console.WriteLine($"In-memory: {memTimePerQuery:F2} ms/query");
                Console.WriteLine($"Disk-backed: {diskTimePerQuery:F2} ms/query");
                Console.WriteLine($"Disk is {slowdown:F1}x slower than in-memory");

                // Verify both stores return the same top result
                float[] verifyQuery = searchQueries[0];
                IReadOnlyList<SearchResult<Guid>> memResults = await memStore.FindMostSimilarAsync(verifyQuery, 1);
                IReadOnlyList<SearchResult<Guid>> diskResults = await diskStore.FindMostSimilarAsync(verifyQuery, 1);
                Assert.Equal(memResults[0].Id, diskResults[0].Id);
                TestHelpers.AssertApproximatelyEqual(memResults[0].Score, diskResults[0].Score, 0.0001f);

                Console.WriteLine("\n=== Disk vs In-Memory Test Complete ===");
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
    }
}
