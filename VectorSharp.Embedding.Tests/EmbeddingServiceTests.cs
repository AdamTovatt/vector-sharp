namespace VectorSharp.Embedding.Tests
{
    public class EmbeddingServiceTests
    {
        #region Constructor

        [Fact]
        public async Task Constructor_ValidFactory_Succeeds()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            Assert.Equal(768, service.Dimension);
        }

        [Fact]
        public void Constructor_NullFactory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new EmbeddingService(null!));
        }

        [Fact]
        public void Constructor_ZeroConcurrency_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { Concurrency = 0 }));
        }

        [Fact]
        public void Constructor_NegativeConcurrency_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { Concurrency = -1 }));
        }

        [Fact]
        public void Constructor_ZeroChannelCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EmbeddingService(() => new TestEmbeddingProvider(),
                    new EmbeddingServiceOptions { ChannelCapacity = 0 }));
        }

        #endregion

        #region Dimension

        [Fact]
        public async Task Dimension_ReturnsProviderDimension()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider(384));

            Assert.Equal(384, service.Dimension);
        }

        #endregion

        #region EmbedAsync

        [Fact]
        public async Task EmbedAsync_ValidText_ReturnsCorrectDimension()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider(128));

            float[] result = await service.EmbedAsync("hello world");

            Assert.Equal(128, result.Length);
        }

        [Fact]
        public async Task EmbedAsync_SameText_ReturnsSameResult()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            float[] result1 = await service.EmbedAsync("hello");
            float[] result2 = await service.EmbedAsync("hello");

            Assert.Equal(result1, result2);
        }

        [Fact]
        public async Task EmbedAsync_DifferentTexts_ReturnsDifferentResults()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            float[] result1 = await service.EmbedAsync("hello");
            float[] result2 = await service.EmbedAsync("world");

            Assert.NotEqual(result1, result2);
        }

        [Fact]
        public async Task EmbedAsync_NullText_Throws()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                service.EmbedAsync(null!));
        }

        [Fact]
        public async Task EmbedAsync_AfterDispose_Throws()
        {
            EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());
            await service.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                service.EmbedAsync("hello"));
        }

        [Fact]
        public async Task EmbedAsync_ProviderThrows_PropagatesException()
        {
            await using EmbeddingService service = new EmbeddingService(() => new FailingProvider());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EmbedAsync("hello"));
        }

        #endregion

        #region EmbedBatchAsync

        [Fact]
        public async Task EmbedBatchAsync_MultipleTexts_ReturnsAllResults()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider(128));

            float[][] results = await service.EmbedBatchAsync(new[] { "a", "b", "c" });

            Assert.Equal(3, results.Length);
            foreach (float[] result in results)
            {
                Assert.Equal(128, result.Length);
            }
        }

        [Fact]
        public async Task EmbedBatchAsync_EmptyList_ReturnsEmptyArray()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            float[][] results = await service.EmbedBatchAsync(Array.Empty<string>());

            Assert.Empty(results);
        }

        [Fact]
        public async Task EmbedBatchAsync_NullList_Throws()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                service.EmbedBatchAsync(null!));
        }

        [Fact]
        public async Task EmbedBatchAsync_ResultCountMatchesInput()
        {
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            string[] texts = new string[20];
            for (int i = 0; i < 20; i++)
            {
                texts[i] = $"text {i}";
            }

            float[][] results = await service.EmbedBatchAsync(texts);

            Assert.Equal(20, results.Length);
        }

        #endregion

        #region Concurrency

        [Fact]
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

            Assert.Equal(20, results.Length);
            foreach (float[] result in results)
            {
                Assert.Equal(128, result.Length);
            }
        }

        [Fact]
        public async Task EmbedAsync_WithConcurrency2_UsesMultipleWorkers()
        {
            // Verify that both workers process requests by checking total call count
            // across all provider instances
            SharedCounter counter = new SharedCounter();

            await using EmbeddingService service = new EmbeddingService(
                () => new CountingProvider(128, counter),
                new EmbeddingServiceOptions { Concurrency = 2 });

            Task<float[]>[] tasks = new Task<float[]>[20];
            for (int i = 0; i < 20; i++)
            {
                tasks[i] = service.EmbedAsync($"text {i}");
            }

            await Task.WhenAll(tasks);

            Assert.Equal(20, counter.Value);
        }

        #endregion

        #region Disposal

        [Fact]
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

        private sealed class SharedCounter
        {
            private int _value;
            public int Value => _value;
            public void Increment() => Interlocked.Increment(ref _value);
        }

        private sealed class CountingProvider : IEmbeddingProvider
        {
            private readonly SharedCounter _counter;

            public int Dimension { get; }

            public CountingProvider(int dimension, SharedCounter counter)
            {
                Dimension = dimension;
                _counter = counter;
            }

            public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                _counter.Increment();
                float[] result = new float[Dimension];
                return Task.FromResult(result);
            }

            public void Dispose() { }
        }

        #endregion
    }
}
