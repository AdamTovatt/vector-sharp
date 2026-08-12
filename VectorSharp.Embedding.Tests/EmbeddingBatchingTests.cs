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

            // Deliberately including same-length texts and an anagram pair. Literals of seven
            // distinct lengths would let this pass against a provider double whose vector only
            // encoded the length, so the misordering it exists to catch has to be catchable
            // without relying on the inputs happening to differ in an easy way.
            string[] texts = ["alpha", "bravo", "ab", "ba", "cccccc", "e", "ggg"];

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

            Assert.Single(recorder.Calls);
            Assert.Equal(EmbeddingPurpose.Document, recorder.Calls[0].Purpose);
        }

        [Fact]
        public void VectorFor_DistinguishesTextsAPlainLengthAndSumWouldNot()
        {
            // The property the order assertions above rest on. Both of these pairs are equal under
            // "length plus sum of characters", so a double encoding only that would hand back
            // identical vectors for different texts and every ordering assertion would hold
            // whatever order the vectors came back in.
            Assert.NotEqual(BatchRecordingProvider.VectorFor("ab"), BatchRecordingProvider.VectorFor("ba"));
            Assert.NotEqual(BatchRecordingProvider.VectorFor("ac"), BatchRecordingProvider.VectorFor("bb"));
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
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchingOnlyProvider(recorder),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(Texts(5));

            Assert.Equal(5, result.Vectors.Count);
            Assert.Null(result.Usage);
            Assert.Equal([5], recorder.Batches.Select(batch => batch.Count));
            Assert.Equal(0, recorder.SingleCallCount);
        }

        [Fact]
        public async Task DefaultEmbedBatchAsync_CalledDirectlyOnTheInterface_LoopsTheSingleTextMethod()
        {
            TestEmbeddingProvider provider = new TestEmbeddingProvider(dimension: 16);

            float[][] vectors = await ((IEmbeddingProvider)provider).EmbedBatchAsync(["a", "b"]);

            // The call count is what says "loops the single-text method". Comparing the batch's
            // vectors against a fresh EmbedAsync would pass for any implementation that is merely
            // deterministic, including one that never called EmbedAsync at all.
            Assert.Equal(2, provider.CallCount);
            Assert.Equal(2, vectors.Length);
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
        public async Task EmbedBatchWithUsageAsync_NullList_Throws()
        {
            // Its own public entry point with its own documented ArgumentNullException, not just
            // the path EmbedBatchAsync happens to reach it through.
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider());

            await Assert.ThrowsAsync<ArgumentNullException>(() => service.EmbedBatchWithUsageAsync(null!));
        }

        [Fact]
        public async Task MaxCharactersPerBatch_ProviderThatDoesNotBatch_ChangesNothing()
        {
            // The README says the character limit is only reached by a provider that batches,
            // because one text is already a whole request for a provider that does not. A limit
            // low enough to split these texts several ways still leaves one text per call.
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, supportsBatching: false),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64, MaxCharactersPerBatch = 1 });

            await service.EmbedBatchAsync(Texts(4, lengthEach: 10));

            Assert.Equal(4, recorder.Batches.Count);
            Assert.All(recorder.Batches, batch => Assert.Single(batch));
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
        public async Task EmbedBatchAsync_ProviderCancelsAgainstItsOwnToken_SurfacesTheProvidersFailure()
        {
            // The third of the worker's three OperationCanceledException catches, and the only one
            // nothing else covers. Neither the caller's token nor the service's is cancelled here,
            // so this is a failed call wearing a cancellation's exception type. The worker has to
            // pass the provider's own exception through rather than replacing it with a
            // cancellation of its own — the message is the only thing that tells a caller their
            // provider timed out rather than something in here deciding to stop.
            await using EmbeddingService service = new EmbeddingService(() => new SelfCancellingProvider());

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.EmbedBatchAsync(Texts(2)));

            Assert.Equal(SelfCancellingProvider.FailureMessage, failure.Message);
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
    }
}
