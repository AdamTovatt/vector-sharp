namespace VectorSharp.Reranking.Tests
{
    /// <summary>
    /// The behaviour every <see cref="IRerankProvider"/> is expected to have, written against the
    /// interface rather than any one implementation. A new provider inherits this class and
    /// supplies itself, and gets the contract checked instead of hand-rolling its own version of
    /// these tests.
    /// </summary>
    /// <remarks>
    /// The package ships abstractions only, so these tests necessarily run against a double. What
    /// keeps them from being a test of the double is that the double is not where they live: the
    /// assertions are the contract, and the first real provider runs the same ones.
    /// </remarks>
    public abstract class RerankProviderContractTests
    {
        /// <summary>
        /// The candidate list every test below ranks. A fresh array each time rather than one
        /// shared instance: <see cref="IRerankProvider"/>'s own remarks say that mutating the list
        /// passed to a call invalidates every index it returned, and a static array a subclass or
        /// a provider double could write into would carry that between tests, where the failure
        /// would land in whichever test happened to run second.
        /// </summary>
        protected static string[] Documents =>
        [
            "cats sleep most of the day",
            "sorting a list in python",
            "dogs and cats living together",
            "how to sort a python list quickly"
        ];

        /// <summary>
        /// Ranks the documents above without a tie: document 3 matches every word, document 1
        /// matches all but "quickly", and the other two match none. A query that tied the top two
        /// would leave the ordering assertions resting on the provider's tiebreak instead of on
        /// the scores.
        /// </summary>
        protected const string UnambiguousQuery = "python list sort quickly";

        /// <summary>
        /// Creates the provider under test. It must score <see cref="Documents"/> against
        /// <see cref="UnambiguousQuery"/> by relevance, and report <paramref name="usage"/>.
        /// </summary>
        protected abstract IRerankProvider CreateProvider(RerankUsage? usage = null);

        #region Results point back at the caller's own records

        [Fact]
        public async Task RerankAsync_MatchesCarryTheIndexOfTheDocumentTheyScored()
        {
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync(UnambiguousQuery, Documents, topN: 2);

            Assert.Equal(2, result.Matches.Count);
            Assert.Equal(3, result.Matches[0].Index);
            Assert.Equal(1, result.Matches[1].Index);
        }

        [Fact]
        public async Task RerankAsync_IndexesMapBackToTheCallersMetadata()
        {
            // A worked illustration of why matches are indexes rather than text, kept for what it
            // shows a reader: the same two positions the test above pins, resolved through the
            // caller's own array.
            int[] documentIds = [101, 102, 103, 104];
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync(UnambiguousQuery, Documents, topN: 2);

            int[] rerankedIds = result.Matches.Select(match => documentIds[match.Index]).ToArray();

            Assert.Equal([104, 102], rerankedIds);
        }

        [Fact]
        public async Task RerankAsync_IndexesAreWithinTheSuppliedList()
        {
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync("cats", Documents, topN: Documents.Length);

            // Pinned first: without it, a provider returning nothing satisfies both assertions
            // below, since Assert.All passes over an empty sequence and 0 distinct equals 0.
            Assert.Equal(Documents.Length, result.Matches.Count);

            Assert.All(result.Matches, match => Assert.InRange(match.Index, 0, Documents.Length - 1));
            Assert.Equal(result.Matches.Count, result.Matches.Select(match => match.Index).Distinct().Count());
        }

        #endregion

        #region Ordering and topN

        [Fact]
        public async Task RerankAsync_ReturnsMatchesInDescendingScoreOrder()
        {
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync(UnambiguousQuery, Documents, topN: 4);

            Assert.Equal(4, result.Matches.Count);

            // The top two are strictly ordered, so this cannot pass on a run of equal scores.
            Assert.True(
                result.Matches[0].Score > result.Matches[1].Score,
                $"Expected a strict ordering at the top, got {result.Matches[0].Score} then {result.Matches[1].Score}.");

            for (int i = 1; i < result.Matches.Count; i++)
            {
                Assert.True(
                    result.Matches[i - 1].Score >= result.Matches[i].Score,
                    $"Match {i - 1} scored {result.Matches[i - 1].Score} and match {i} scored {result.Matches[i].Score}.");
            }
        }

        [Fact]
        public async Task RerankAsync_ReturnsAtMostTopN()
        {
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync("cats", Documents, topN: 1);

            Assert.Single(result.Matches);
        }

        [Fact]
        public async Task RerankAsync_FewerDocumentsThanTopN_ReturnsWhatThereIs()
        {
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync("cats", ["cats sleep", "dogs bark"], topN: 10);

            Assert.Equal(2, result.Matches.Count);
        }

        [Fact]
        public async Task RerankAsync_NoDocuments_ReturnsNoMatchesRatherThanFailing()
        {
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync("cats", [], topN: 5);

            Assert.Empty(result.Matches);
        }

        [Fact]
        public async Task RerankAsync_NoMatches_YieldsNullFromFirstOrDefaultRatherThanAnIndexZeroMatch()
        {
            // Why RerankMatch is a class: as a struct this would hand back Index 0 with score 0,
            // a match pointing at the caller's first document and indistinguishable from a real
            // one. Asserted through behaviour so that changing the type back reddens a test.
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync("cats", [], topN: 5);

            Assert.Null(result.Matches.FirstOrDefault());
        }

        #endregion

        #region Usage

        [Fact]
        public async Task RerankAsync_ProviderMetersNothing_UsageIsNullNotZero()
        {
            // A provider that reports nothing must be distinguishable from one reporting a call
            // that cost nothing, the same rule the embedding package follows.
            using IRerankProvider provider = CreateProvider();

            RerankResult result = await provider.RerankAsync("cats", Documents, topN: 2);

            Assert.Null(result.Usage);
        }

        [Fact]
        public async Task RerankAsync_ProviderReportsZeroTokens_UsageSurvivesAsAReportedZero()
        {
            // The other half of the rule: a reported zero is a real measurement and has to come
            // back as one, or "null means unknown, never free" says nothing.
            using IRerankProvider provider = CreateProvider(new RerankUsage { TokenCount = 0, Model = "free-tier" });

            RerankResult result = await provider.RerankAsync("cats", Documents, topN: 2);

            Assert.NotNull(result.Usage);
            Assert.Equal(0, result.Usage.TokenCount);
            Assert.Equal("free-tier", result.Usage.Model);
        }

        [Fact]
        public async Task RerankAsync_ProviderReportsUsage_PassesItThrough()
        {
            using IRerankProvider provider = CreateProvider(new RerankUsage { TokenCount = 42, Model = "test-reranker" });

            RerankResult result = await provider.RerankAsync("cats", Documents, topN: 2);

            Assert.NotNull(result.Usage);
            Assert.Equal(42, result.Usage.TokenCount);
            Assert.Equal("test-reranker", result.Usage.Model);
        }

        [Fact]
        public async Task RerankAsync_ProviderReportsUsageWithoutAModel_KeepsTheCountAndLeavesTheModelNull()
        {
            using IRerankProvider provider = CreateProvider(new RerankUsage { TokenCount = 7 });

            RerankResult result = await provider.RerankAsync("cats", Documents, topN: 2);

            Assert.NotNull(result.Usage);
            Assert.Equal(7, result.Usage.TokenCount);
            Assert.Null(result.Usage.Model);
        }

        #endregion

        #region Argument handling

        [Fact]
        public async Task RerankAsync_NullQuery_Throws()
        {
            using IRerankProvider provider = CreateProvider();

            await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RerankAsync(null!, Documents, topN: 2));
        }

        [Fact]
        public async Task RerankAsync_NullDocuments_Throws()
        {
            using IRerankProvider provider = CreateProvider();

            await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RerankAsync("cats", null!, topN: 2));
        }

        [Fact]
        public async Task RerankAsync_NullDocumentElement_Throws()
        {
            using IRerankProvider provider = CreateProvider();

            await Assert.ThrowsAsync<ArgumentNullException>(() => provider.RerankAsync("cats", ["fine", null!], topN: 2));
        }

        [Fact]
        public async Task RerankAsync_TopNBelowOne_Throws()
        {
            using IRerankProvider provider = CreateProvider();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.RerankAsync("cats", Documents, topN: 0));
        }

        [Fact]
        public async Task RerankAsync_CancelledToken_Throws()
        {
            using IRerankProvider provider = CreateProvider();
            CancellationToken cancelled = new CancellationToken(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.RerankAsync("cats", Documents, topN: 2, cancelled));
        }

        [Fact]
        public async Task RerankAsync_AfterDispose_Throws()
        {
            IRerankProvider provider = CreateProvider();
            provider.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.RerankAsync("cats", Documents, topN: 2));
        }

        #endregion
    }
}
