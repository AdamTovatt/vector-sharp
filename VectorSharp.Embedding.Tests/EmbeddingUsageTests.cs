namespace VectorSharp.Embedding.Tests
{
    public class EmbeddingUsageTests
    {
        private static EmbeddingUsage TokensPerText(IReadOnlyList<string> batch, string? model = "test-model")
        {
            return new EmbeddingUsage { TokenCount = batch.Count * 10, Model = model };
        }

        #region Nothing reported

        [Fact]
        public async Task EmbedBatchWithUsageAsync_ProviderReportsNothing_UsageIsNullNotZero()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b"]);

            Assert.Null(result.Usage);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_ProviderMetersNothingAtAll_UsageIsNullNotZero()
        {
            // The local-model case end to end: a provider that implements only the single-text
            // method has nothing to report, and the service must not turn that into a zero a
            // caller could mistake for a free call.
            await using EmbeddingService service = new EmbeddingService(() => new TestEmbeddingProvider());

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b"]);

            Assert.Equal(2, result.Vectors.Count);
            Assert.Null(result.Usage);
        }

        [Fact]
        public async Task EmbedWithUsageAsync_ProviderReportsNothing_UsageIsNullNotZero()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            EmbeddingResult result = await service.EmbedWithUsageAsync("a");

            Assert.Null(result.Usage);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_EmptyList_ReturnsNoVectorsAndNoUsage()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => TokensPerText(batch)));

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync([]);

            Assert.Empty(result.Vectors);
            Assert.Null(result.Usage);
        }

        #endregion

        #region Reported usage

        [Fact]
        public async Task EmbedBatchWithUsageAsync_ProviderReportsZeroTokens_UsageSurvivesAsAReportedZero()
        {
            // The other half of "null means unknown, never free": a reported zero is a real
            // measurement and has to arrive as one, or the distinction the null carries is empty.
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => new EmbeddingUsage { TokenCount = 0, Model = "free-tier" }));

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b"]);

            Assert.NotNull(result.Usage);
            Assert.Equal(0, result.Usage.TokenCount);
            Assert.Equal("free-tier", result.Usage.Model);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_ProviderReportsUsage_PassesItThrough()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => TokensPerText(batch)));

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b", "c"]);

            Assert.NotNull(result.Usage);
            Assert.Equal(30, result.Usage.TokenCount);
            Assert.Equal("test-model", result.Usage.Model);
        }

        [Fact]
        public async Task EmbedWithUsageAsync_ProviderReportsUsage_PassesItThrough()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => TokensPerText(batch)));

            EmbeddingResult result = await service.EmbedWithUsageAsync("a");

            Assert.Single(result.Vectors);
            Assert.NotNull(result.Usage);
            Assert.Equal(10, result.Usage.TokenCount);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_SplitIntoBatches_SumsTheReportedTokens()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => TokensPerText(batch)),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 2 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b", "c", "d", "e"]);

            Assert.Equal(3, recorder.Batches.Count);
            Assert.NotNull(result.Usage);
            Assert.Equal(50, result.Usage.TokenCount);
            Assert.Equal("test-model", result.Usage.Model);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_EveryBatchNamesNoModel_KeepsTheTotalAndReportsNoModel()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => TokensPerText(batch, model: null)),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 2 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b", "c", "d"]);

            Assert.Equal(2, recorder.Batches.Count);
            Assert.NotNull(result.Usage);
            Assert.Equal(40, result.Usage.TokenCount);
            Assert.Null(result.Usage.Model);
        }

        #endregion

        #region Combining across batches

        [Fact]
        public async Task EmbedBatchWithUsageAsync_OnlySomeBatchesReport_ReturnsNullRatherThanAPartialTotal()
        {
            // A total assembled from some of the batches would understate the spend, and
            // under-reporting is the failure mode that matters when the caller is billed.
            // The discriminator is a literal from this test's own input, so the mixed split it
            // depends on cannot quietly stop happening.
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => batch.Contains("metered") ? TokensPerText(batch) : null),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 1 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["metered", "silent", "also silent"]);

            // The premise of the test: some batches reported and some did not.
            Assert.Equal(3, recorder.Batches.Count);
            Assert.Contains(recorder.Batches, batch => batch.Contains("metered"));
            Assert.Contains(recorder.Batches, batch => !batch.Contains("metered"));

            Assert.Null(result.Usage);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_FirstBatchNamesNoModel_DoesNotBorrowAnotherBatchsModel()
        {
            // The two are not the same model just because one of them declined to say. Attributing
            // the whole call to the only name that turned up would misreport spend per model.
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => TokensPerText(batch, model: batch.Contains("unnamed") ? null : "model-x")),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 1, Concurrency = 1 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["unnamed", "named", "also named"]);

            Assert.NotNull(result.Usage);
            Assert.Equal(30, result.Usage.TokenCount);
            Assert.Null(result.Usage.Model);
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_BatchesReportDifferentModels_DropsTheModelButKeepsTheTotal()
        {
            int batchNumber = 0;
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(
                () => new BatchRecordingProvider(recorder, batch => new EmbeddingUsage
                {
                    TokenCount = 10,
                    Model = $"model-{Interlocked.Increment(ref batchNumber)}"
                }),
                new EmbeddingServiceOptions { MaxTextsPerBatch = 1 });

            EmbeddingResult result = await service.EmbedBatchWithUsageAsync(["a", "b", "c"]);

            Assert.NotNull(result.Usage);
            Assert.Equal(30, result.Usage.TokenCount);
            Assert.Null(result.Usage.Model);
        }

        #endregion

        #region EmbedWithUsageAsync guards

        [Fact]
        public async Task EmbedWithUsageAsync_NullText_Throws()
        {
            BatchRecorder recorder = new BatchRecorder();
            await using EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));

            await Assert.ThrowsAsync<ArgumentNullException>(() => service.EmbedWithUsageAsync(null!));
        }

        [Fact]
        public async Task EmbedWithUsageAsync_AfterDispose_Throws()
        {
            BatchRecorder recorder = new BatchRecorder();
            EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));
            await service.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.EmbedWithUsageAsync("a"));
        }

        [Fact]
        public async Task EmbedBatchWithUsageAsync_AfterDispose_Throws()
        {
            BatchRecorder recorder = new BatchRecorder();
            EmbeddingService service = new EmbeddingService(() => new BatchRecordingProvider(recorder));
            await service.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.EmbedBatchWithUsageAsync(["a"]));
        }

        #endregion
    }
}
