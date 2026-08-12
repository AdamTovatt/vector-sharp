namespace VectorSharp.Embedding.Tests
{
    public class EmbeddingBatchingTests
    {
        private static string[] Texts(int count, int lengthEach = 4)
        {
            string[] texts = new string[count];
            for (int i = 0; i < count; i++)
            {
                texts[i] = new string((char)('a' + (i % 26)), lengthEach);
            }
            return texts;
        }

        #region Batching reaches the provider as one call

        [Fact]
        public async Task EmbedBatchAsync_WithinLimits_ReachesTheProviderAsOneCall()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            await service.EmbedBatchAsync(Texts(5));

            Assert.Single(recorder.Batches);
            Assert.Equal(5, recorder.Batches[0].Count);
            Assert.Equal(0, recorder.SingleCallCount);
        }

        [Fact]
        public async Task EmbedBatchAsync_SplitsOnTextCountLimit()
        {
            BatchRecorder recorder = new BatchRecorder();
            // Concurrency 1 so the batches arrive in the order they were queued; this asserts the
            // sizes in sequence, which several workers would interleave.
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 3, Concurrency = 1 });

            await service.EmbedBatchAsync(Texts(7));

            Assert.Equal(3, recorder.Batches.Count);
            Assert.Equal([3, 3, 1], recorder.Batches.Select(batch => batch.Count));
        }

        [Fact]
        public async Task EmbedBatchAsync_SplitsOnCharacterLimit()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 100, MaxCharactersPerBatch = 25 });

            // Ten texts of ten characters: the character limit closes a batch after two.
            await service.EmbedBatchAsync(Texts(10, lengthEach: 10));

            Assert.Equal(5, recorder.Batches.Count);
            Assert.All(recorder.Batches, batch => Assert.Equal(2, batch.Count));
        }

        [Fact]
        public async Task EmbedBatchAsync_AppliesWhicheverLimitBindsFirst()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 4, MaxCharactersPerBatch = 30, Concurrency = 1 });

            // Four texts would fit the count limit, but three of ten characters reach the
            // character limit first, so both limits have to hold at once.
            await service.EmbedBatchAsync(Texts(8, lengthEach: 10));

            Assert.Equal([3, 3, 2], recorder.Batches.Select(batch => batch.Count));
        }

        [Fact]
        public async Task EmbedBatchAsync_TextLongerThanTheCharacterLimit_IsSentAloneRatherThanDropped()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder),
                new EmbeddingServiceOptions { MaxCharactersPerBatch = 10 });

            string oversized = new string('x', 50);
            float[][] vectors = await service.EmbedBatchAsync(["short", oversized, "tiny"]);

            Assert.Equal(3, vectors.Length);
            Assert.Contains(recorder.Batches, batch => batch.Count == 1 && batch[0] == oversized);
        }

        [Fact]
        public async Task EmbedBatchAsync_AcrossBatches_ReturnsVectorsInInputOrder()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 2, Concurrency = 4 });

            string[] texts = ["alpha", "bb", "cccccc", "dddd", "e", "ffffffff", "ggg"];

            float[][] vectors = await service.EmbedBatchAsync(texts);

            Assert.Equal(texts.Length, vectors.Length);
            for (int i = 0; i < texts.Length; i++)
            {
                Assert.Equal(BatchRecordingProvider.VectorFor(texts[i]), vectors[i]);
            }
        }

        [Fact]
        public async Task EmbedAsync_SingleText_ReachesTheProviderAsABatchOfOne()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            float[] vector = await service.EmbedAsync("only this");

            Assert.Single(recorder.Batches);
            Assert.Equal(["only this"], recorder.Batches[0]);
            Assert.Equal(BatchRecordingProvider.VectorFor("only this"), vector);
        }

        #endregion

        #region Purpose

        [Theory]
        [InlineData(EmbeddingPurpose.Query)]
        [InlineData(EmbeddingPurpose.Document)]
        public async Task EmbedBatchAsync_DeliversThePurposeToTheProvider(EmbeddingPurpose purpose)
        {
            // Asserted at the provider rather than at the call: the purpose travels through the
            // request and the worker, and a service that dropped it would return correct-looking
            // vectors from the wrong side of the model.
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            await service.EmbedBatchAsync(Texts(3), purpose);

            Assert.Single(recorder.Calls);
            Assert.Equal(purpose, recorder.Calls[0].Purpose);
        }

        [Theory]
        [InlineData(EmbeddingPurpose.Query)]
        [InlineData(EmbeddingPurpose.Document)]
        public async Task EmbedAsync_DeliversThePurposeToTheProvider(EmbeddingPurpose purpose)
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            await service.EmbedAsync("a query", purpose);

            Assert.Single(recorder.Calls);
            Assert.Equal(purpose, recorder.Calls[0].Purpose);
        }

        [Fact]
        public async Task EmbedBatchAsync_DefaultPurpose_IsDocument()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            await service.EmbedBatchAsync(Texts(2));

            Assert.Equal(EmbeddingPurpose.Document, recorder.Calls[0].Purpose);
        }

        #endregion

        #region Batching only for providers that gain from it

        [Fact]
        public async Task EmbedBatchAsync_ProviderThatDoesNotBatch_StillGetsOneTextPerCall()
        {
            // The batching policy says 64, but grouping texts for a provider that embeds one at a
            // time would only move them into a single worker. It is the provider's answer, not the
            // policy, that decides whether grouping happens at all.
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, supportsBatching: false),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64 });

            await service.EmbedBatchAsync(Texts(6));

            Assert.Equal(6, recorder.Batches.Count);
            Assert.All(recorder.Batches, batch => Assert.Single(batch));
        }

        [Fact]
        public async Task EmbedBatchAsync_ProviderThatDoesNotBatch_KeepsWorkingAcrossWorkers()
        {
            // The regression this guards against is invisible to a count assertion: a batch looped
            // inside one worker still returns the right vectors, just serially. The provider here
            // only completes a call once two have arrived, so a serialised path never finishes and
            // the timeout fails the test rather than hanging it.
            ArrivalLatch latch = new ArrivalLatch(arrivalsRequired: 2);
            await using EmbeddingService service = new EmbeddingService(
                () => new LatchedSingleTextProvider(latch),
                new EmbeddingServiceOptions { Concurrency = 2, MaxTextsPerBatch = 64 });

            float[][] vectors = await service.EmbedBatchAsync(Texts(4)).WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(4, vectors.Length);
        }

        [Fact]
        public async Task EmbedBatchAsync_ProviderThatBatches_GetsWholeBatches()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, supportsBatching: true),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64 });

            await service.EmbedBatchAsync(Texts(6));

            Assert.Single(recorder.Batches);
            Assert.Equal(6, recorder.Batches[0].Count);
        }

        [Fact]
        public void SupportsBatching_DefaultsToFalse()
        {
            // A provider written before the batch methods existed answers this without knowing it,
            // and has to answer no.
            IEmbeddingProvider provider = new TestEmbeddingProvider();

            Assert.False(provider.SupportsBatching);
        }

        #endregion

        #region The default interface implementation

        [Fact]
        public async Task EmbedBatchAsync_ProviderImplementingOnlyTheSingleTextMethod_StillWorks()
        {
            // TestEmbeddingProvider implements EmbedAsync and nothing else, so everything here
            // runs through the default implementations on IEmbeddingProvider.
            TestEmbeddingProvider provider = new TestEmbeddingProvider(dimension: 16);
            await using EmbeddingService service = new EmbeddingService(() => provider);

            float[][] vectors = await service.EmbedBatchAsync(["one", "two", "three"]);

            Assert.Equal(3, vectors.Length);
            Assert.All(vectors, vector => Assert.Equal(16, vector.Length));
            Assert.Equal(3, provider.CallCount);
        }

        [Fact]
        public async Task EmbedBatchAsync_ProviderThatBatchesButDoesNotMeter_StillGetsOneCallPerBatch()
        {
            // The combination the interface documents: EmbedBatchAsync overridden,
            // EmbedBatchWithUsageAsync inherited. The service calls the usage method, whose default
            // routes into the overridden one, so the batch stays a single call and reports nothing.
            BatchingOnlyProvider provider = new BatchingOnlyProvider();
            await using EmbeddingService service = new EmbeddingService(() => provider,
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(Texts(5));

            Assert.Equal(5, result.Vectors.Count);
            Assert.Null(result.Usage);
            Assert.Equal([5], provider.ReceivedBatchSizes);
            Assert.Equal(0, provider.SingleCallCount);
        }

        [Fact]
        public async Task DefaultEmbedBatchAsync_CalledDirectlyOnTheInterface_LoopsTheSingleTextMethod()
        {
            IEmbeddingProvider provider = new TestEmbeddingProvider(dimension: 16);

            float[][] vectors = await provider.EmbedBatchAsync(["a", "b"]);

            Assert.Equal(2, vectors.Length);
            Assert.Equal(await provider.EmbedAsync("a"), vectors[0]);
        }

        [Fact]
        public async Task DefaultEmbedBatchWithUsageAsync_ReportsNoUsage()
        {
            IEmbeddingProvider provider = new TestEmbeddingProvider();

            EmbeddingResult result = await provider.EmbedBatchWithUsageAsync(["a", "b"]);

            Assert.Equal(2, result.Vectors.Count);
            Assert.Null(result.Usage);
        }

        [Fact]
        public async Task DefaultEmbedBatchAsync_NullList_Throws()
        {
            IEmbeddingProvider provider = new TestEmbeddingProvider();

            await Assert.ThrowsAsync<ArgumentNullException>(() => provider.EmbedBatchAsync(null!));
        }

        [Fact]
        public async Task DefaultEmbedBatchAsync_EmptyList_ReturnsEmptyAndCallsNothing()
        {
            TestEmbeddingProvider provider = new TestEmbeddingProvider();

            float[][] vectors = await ((IEmbeddingProvider)provider).EmbedBatchAsync([]);

            Assert.Empty(vectors);
            Assert.Equal(0, provider.CallCount);
        }

        #endregion

        #region Validation

        [Fact]
        public async Task EmbedBatchAsync_NullElement_Throws()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                service.EmbedBatchAsync(["fine", null!]));

            Assert.Empty(recorder.Batches);
        }

        [Fact]
        public async Task EmbedBatchAsync_ProviderReturnsWrongNumberOfVectors_Fails()
        {
            // Vectors are matched to texts by position, so a short response is a lost mapping
            // rather than a partial success.
            await using EmbeddingService service = new EmbeddingService(() => new ShortResponseProvider());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EmbedBatchAsync(["a", "b", "c"]));
        }

        [Fact]
        public async Task Constructor_FactoryThrowsPartway_DisposesTheProvidersItAlreadyBuilt()
        {
            // The constructor never returns, so nothing else can dispose these.
            List<TestEmbeddingProvider> built = new List<TestEmbeddingProvider>();

            Assert.Throws<InvalidOperationException>(() =>
                new EmbeddingService(() =>
                {
                    if (built.Count == 2)
                        throw new InvalidOperationException("factory failed");

                    TestEmbeddingProvider provider = new TestEmbeddingProvider();
                    built.Add(provider);
                    return provider;
                },
                new EmbeddingServiceOptions { Concurrency = 4 }));

            Assert.Equal(2, built.Count);
            foreach (TestEmbeddingProvider provider in built)
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.EmbedAsync("anything"));
            }
        }

        #endregion

        #region Cancellation

        [Fact]
        public async Task EmbedBatchAsync_CancelledWhileInFlight_ReachesTheProvider()
        {
            // Completing the caller's task is not enough: the batch itself is what a remote API
            // bills, so the token has to reach the call that is running.
            CancellationTokenSource cts = new CancellationTokenSource();
            ObservedCancellationProvider provider = new ObservedCancellationProvider();

            await using EmbeddingService service = new EmbeddingService(() => provider);

            Task<float[][]> embedTask = service.EmbedBatchAsync(Texts(3), EmbeddingPurpose.Document, cts.Token);

            await provider.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => embedTask);
            Assert.True(await provider.SawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }

        [Fact]
        public async Task EmbedBatchAsync_TokenAlreadyCancelled_Throws()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            CancellationToken cancelled = new CancellationToken(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.EmbedBatchAsync(Texts(2), EmbeddingPurpose.Document, cancelled));
        }

        [Fact]
        public async Task EmbedBatchAsync_QueueFullAndServiceDisposed_FailsTheCallerRatherThanStranding()
        {
            // The blocked-writer path: capacity 1 with a worker held open, so later batches are
            // still queued or waiting to be written when the service is disposed under them.
            // Every one of them has to end up somewhere — a batch left in the channel after the
            // workers stop would leave this caller awaiting a result nothing will ever produce.
            ArrivalLatch neverReached = new ArrivalLatch(arrivalsRequired: 99);
            EmbeddingService service = new EmbeddingService(
                () => new LatchedSingleTextProvider(neverReached),
                new EmbeddingServiceOptions { Concurrency = 1, ChannelCapacity = 1, MaxTextsPerBatch = 1 });

            Task<float[][]> blocked = service.EmbedBatchAsync(Texts(8));

            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

            // Bounded so a regression fails this test instead of hanging the run — and the failure
            // has to be a shutdown failure. A TimeoutException from the bound would mean the
            // caller was left waiting, which is the very thing under test, so it must not count
            // as the exception this expects.
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => blocked.WaitAsync(TimeSpan.FromSeconds(30)));

            Assert.True(
                failure is ObjectDisposedException or OperationCanceledException,
                $"Expected the call to fail because the service was disposed, but it failed with {failure.GetType().Name}: {failure.Message}");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Releases every waiter once enough calls have arrived, counting arrivals cumulatively
        /// rather than tracking how many are inside at once — a call that has already returned
        /// still counts. That is all the test using it needs, because a serialised path never
        /// produces a second arrival while the first is held.
        /// </summary>
        private sealed class ArrivalLatch
        {
            private readonly int _arrivalsRequired;
            private readonly TaskCompletionSource _reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _arrivals;

            public ArrivalLatch(int arrivalsRequired)
            {
                _arrivalsRequired = arrivalsRequired;
            }

            public Task ArriveAndWaitAsync()
            {
                if (Interlocked.Increment(ref _arrivals) >= _arrivalsRequired)
                    _reached.TrySetResult();

                return _reached.Task;
            }
        }

        /// <summary>
        /// A provider that only implements the single-text method, standing in for a local model.
        /// </summary>
        private sealed class LatchedSingleTextProvider : IEmbeddingProvider
        {
            private readonly ArrivalLatch _latch;

            public int Dimension => 8;

            public LatchedSingleTextProvider(ArrivalLatch latch)
            {
                _latch = latch;
            }

            public async Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                // Bounded and cancellable on purpose. If the calls are ever serialised the latch
                // is never reached, and a wait that could not be broken out of would hang the
                // service's disposal — and with it the whole test run — instead of failing.
                await _latch.ArriveAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return new float[8];
            }

            public void Dispose() { }
        }

        /// <summary>
        /// Reports whether the token the service handed it was the one the caller cancelled.
        /// </summary>
        private sealed class ObservedCancellationProvider : IEmbeddingProvider
        {
            public int Dimension => 8;

            public bool SupportsBatching => true;

            public TaskCompletionSource CallStarted { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> SawCancellation { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new float[8]);
            }

            public async Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                CallStarted.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    SawCancellation.TrySetResult(true);
                    throw;
                }

                throw new InvalidOperationException("Unreachable: the delay never completes on its own.");
            }

            public void Dispose() { }
        }

        /// <summary>
        /// Batches but meters nothing: overrides only the plain batch method and inherits the
        /// usage one.
        /// </summary>
        private sealed class BatchingOnlyProvider : IEmbeddingProvider
        {
            private readonly List<int> _receivedBatchSizes = new List<int>();
            private int _singleCallCount;

            public int Dimension => 8;

            public bool SupportsBatching => true;

            public IReadOnlyList<int> ReceivedBatchSizes
            {
                get
                {
                    lock (_receivedBatchSizes)
                    {
                        return _receivedBatchSizes.ToList();
                    }
                }
            }

            public int SingleCallCount => Volatile.Read(ref _singleCallCount);

            public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _singleCallCount);
                return Task.FromResult(new float[8]);
            }

            public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                lock (_receivedBatchSizes)
                {
                    _receivedBatchSizes.Add(texts.Count);
                }

                float[][] vectors = new float[texts.Count][];
                for (int i = 0; i < texts.Count; i++)
                {
                    vectors[i] = new float[8];
                }

                return Task.FromResult(vectors);
            }

            public void Dispose() { }
        }

        /// <summary>
        /// A provider that drops a vector, the way a remote API would if it silently skipped an
        /// input.
        /// </summary>
        private sealed class ShortResponseProvider : IEmbeddingProvider
        {
            public int Dimension => 8;

            public bool SupportsBatching => true;

            public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new float[8]);
            }

            public Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
            {
                float[][] vectors = new float[Math.Max(0, texts.Count - 1)][];
                for (int i = 0; i < vectors.Length; i++)
                {
                    vectors[i] = new float[8];
                }

                return Task.FromResult(new EmbeddingResult { Vectors = vectors });
            }

            public void Dispose() { }
        }

        #endregion
    }
}
