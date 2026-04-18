namespace VectorSharp.Embedding.Tests
{
    [TestClass]
    public class EmbeddingServiceTests
    {
        #region Constructor

        [TestMethod]
        public async Task Constructor_ValidFactory_Succeeds()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            Assert.AreEqual(768, service.Dimension);
        }

        [TestMethod]
        public void Constructor_NullFactory_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                new EmbeddingService(null!));
        }

        [TestMethod]
        public void Constructor_ZeroConcurrency_Throws()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { Concurrency = 0 }));
        }

        [TestMethod]
        public void Constructor_NegativeConcurrency_Throws()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { Concurrency = -1 }));
        }

        [TestMethod]
        public void Constructor_ZeroChannelCapacity_Throws()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { ChannelCapacity = 0 }));
        }

        #endregion

        #region Dimension

        [TestMethod]
        public async Task Dimension_ReturnsProviderDimension()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider(384));

            Assert.AreEqual(384, service.Dimension);
        }

        #endregion

        #region EmbedAsync

        [TestMethod]
        public async Task EmbedAsync_ValidText_ReturnsCorrectDimension()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider(128));

            float[] result = await service.EmbedAsync("hello world");

            Assert.AreEqual(128, result.Length);
        }

        [TestMethod]
        public async Task EmbedAsync_SameText_ReturnsSameResult()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            float[] result1 = await service.EmbedAsync("hello");
            float[] result2 = await service.EmbedAsync("hello");

            CollectionAssert.AreEqual(result1, result2);
        }

        [TestMethod]
        public async Task EmbedAsync_DifferentTexts_ReturnsDifferentResults()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            float[] result1 = await service.EmbedAsync("hello");
            float[] result2 = await service.EmbedAsync("world");

            CollectionAssert.AreNotEqual(result1, result2);
        }

        [TestMethod]
        public async Task EmbedAsync_NullText_Throws()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
                service.EmbedAsync(null!));
        }

        [TestMethod]
        public async Task EmbedAsync_AfterDispose_Throws()
        {
            EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());
            await service.DisposeAsync();

            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
                service.EmbedAsync("hello"));
        }

        [TestMethod]
        public async Task EmbedAsync_ProviderThrows_PropagatesException()
        {
            await using EmbeddingService service = new EmbeddingService(() => new FailingProvider());

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.EmbedAsync("hello"));
        }

        #endregion

        #region EmbedBatchAsync

        [TestMethod]
        public async Task EmbedBatchAsync_MultipleTexts_ReturnsAllResults()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider(128));

            float[][] results = await service.EmbedBatchAsync(new[] { "a", "b", "c" });

            Assert.AreEqual(3, results.Length);
            foreach (float[] result in results)
            {
                Assert.AreEqual(128, result.Length);
            }
        }

        [TestMethod]
        public async Task EmbedBatchAsync_EmptyList_ReturnsEmptyArray()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            float[][] results = await service.EmbedBatchAsync(Array.Empty<string>());

            Assert.AreEqual(0, results.Length);
        }

        [TestMethod]
        public async Task EmbedBatchAsync_NullList_Throws()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
                service.EmbedBatchAsync(null!));
        }

        [TestMethod]
        public async Task EmbedBatchAsync_ResultCountMatchesInput()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            string[] texts = new string[20];
            for (int i = 0; i < 20; i++)
            {
                texts[i] = $"text {i}";
            }

            float[][] results = await service.EmbedBatchAsync(texts);

            Assert.AreEqual(20, results.Length);
        }

        #endregion

        #region Concurrency

        [TestMethod]
        public async Task EmbedAsync_MultipleConcurrentRequests_AllComplete()
        {
            await using EmbeddingService service = new EmbeddingService(
                () => new TestEmbeddingProvider(128, delay: TimeSpan.FromMilliseconds(10)),
                new EmbeddingServiceOptions { Concurrency = 2 });

            Task<float[]>[] tasks = new Task<float[]>[20];
            for (int i = 0; i < 20; i++)
            {
                tasks[i] = service.EmbedAsync($"text {i}");
            }

            float[][] results = await Task.WhenAll(tasks);

            Assert.AreEqual(20, results.Length);
            foreach (float[] result in results)
            {
                Assert.AreEqual(128, result.Length);
            }
        }

        [TestMethod]
        public async Task EmbedAsync_WithConcurrency2_ProcessesFasterThanSingle()
        {
            int requestCount = 10;
            TimeSpan delayPerRequest = TimeSpan.FromMilliseconds(50);

            // Single worker
            System.Diagnostics.Stopwatch singleTimer = System.Diagnostics.Stopwatch.StartNew();
            await using (EmbeddingService singleService = new EmbeddingService(
                () => new TestEmbeddingProvider(128, delay: delayPerRequest),
                new EmbeddingServiceOptions { Concurrency = 1 }))
            {
                Task<float[]>[] singleTasks = new Task<float[]>[requestCount];
                for (int i = 0; i < requestCount; i++)
                {
                    singleTasks[i] = singleService.EmbedAsync($"text {i}");
                }

                await Task.WhenAll(singleTasks);
            }

            singleTimer.Stop();

            // Two workers
            System.Diagnostics.Stopwatch dualTimer = System.Diagnostics.Stopwatch.StartNew();
            await using (EmbeddingService dualService = new EmbeddingService(
                () => new TestEmbeddingProvider(128, delay: delayPerRequest),
                new EmbeddingServiceOptions { Concurrency = 2 }))
            {
                Task<float[]>[] dualTasks = new Task<float[]>[requestCount];
                for (int i = 0; i < requestCount; i++)
                {
                    dualTasks[i] = dualService.EmbedAsync($"text {i}");
                }

                await Task.WhenAll(dualTasks);
            }

            dualTimer.Stop();

            // Two workers should be noticeably faster
            Assert.IsTrue(dualTimer.ElapsedMilliseconds < singleTimer.ElapsedMilliseconds,
                $"Dual ({dualTimer.ElapsedMilliseconds}ms) should be faster than single ({singleTimer.ElapsedMilliseconds}ms)");
        }

        #endregion

        #region Disposal

        [TestMethod]
        public async Task DisposeAsync_Idempotent_SecondCallNoOp()
        {
            EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            await service.DisposeAsync();
            await service.DisposeAsync(); // Should not throw
        }

        #endregion

        #region Helpers

        private sealed class FailingProvider : IEmbeddingProvider
        {
            public int Dimension => 768;

            public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Intentional test failure");
            }

            public void Dispose() { }
        }

        #endregion
    }
}
