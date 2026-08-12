namespace VectorSharp.Embedding.Tests
{
    /// <summary>
    /// The splitting rule on its own. The service-level tests in
    /// <c>EmbeddingBatchingTests</c> still cover that these partitions reach a provider as calls;
    /// these cover the partitions themselves, which do not need a worker pool to be wrong.
    /// </summary>
    public class BatchingPolicyTests
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

        private static int[] Sizes(IReadOnlyList<IReadOnlyList<string>> batches)
        {
            return batches.Select(batch => batch.Count).ToArray();
        }

        [Fact]
        public void Split_WithinBothLimits_IsOneBatch()
        {
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 64, maxCharactersPerBatch: 1000);

            Assert.Equal([5], Sizes(policy.Split(Texts(5))));
        }

        [Fact]
        public void Split_OnTextCount_FillsEachBatchBeforeStartingTheNext()
        {
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 3, maxCharactersPerBatch: 1000);

            Assert.Equal([3, 3, 1], Sizes(policy.Split(Texts(7))));
        }

        [Fact]
        public void Split_OnCharacterCount_ClosesTheBatchBeforeItWouldExceed()
        {
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 100, maxCharactersPerBatch: 25);

            Assert.Equal([2, 2, 2, 2, 2], Sizes(policy.Split(Texts(10, lengthEach: 10))));
        }

        [Fact]
        public void Split_BothLimitsInPlay_AppliesWhicheverBindsFirst()
        {
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 4, maxCharactersPerBatch: 30);

            // Four would fit the count limit, but three of ten characters reach the character
            // limit first.
            Assert.Equal([3, 3, 2], Sizes(policy.Split(Texts(8, lengthEach: 10))));
        }

        [Fact]
        public void Split_TextLongerThanTheCharacterLimit_GoesInABatchOfItsOwn()
        {
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 64, maxCharactersPerBatch: 10);
            string oversized = new string('x', 50);

            IReadOnlyList<IReadOnlyList<string>> batches = policy.Split(["short", oversized, "tiny"]);

            Assert.Contains(batches, batch => batch.Count == 1 && batch[0] == oversized);
            Assert.Equal(3, batches.Sum(batch => batch.Count));
        }

        [Fact]
        public void Split_PreservesOrderAcrossBatches()
        {
            // The service reassembles vectors by position, so the batches concatenated in order
            // have to reproduce the input. A split that reordered would misalign every vector.
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 2, maxCharactersPerBatch: 1000);
            string[] texts = ["a", "b", "c", "d", "e"];

            IReadOnlyList<IReadOnlyList<string>> batches = policy.Split(texts);

            Assert.Equal(texts, batches.SelectMany(batch => batch));
        }

        [Fact]
        public void Split_NoTexts_ProducesNoBatches()
        {
            BatchingPolicy policy = new BatchingPolicy(maxTextsPerBatch: 64, maxCharactersPerBatch: 1000);

            Assert.Empty(policy.Split([]));
        }

        [Fact]
        public void For_ProviderThatDoesNotBatch_CapsAtOneTextWhateverTheOptionsSay()
        {
            BatchingPolicy policy = BatchingPolicy.For(
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64 },
                providerSupportsBatching: false);

            Assert.Equal(1, policy.MaxTextsPerBatch);
        }

        [Fact]
        public void For_ProviderThatBatches_TakesTheConfiguredLimit()
        {
            BatchingPolicy policy = BatchingPolicy.For(
                new EmbeddingServiceOptions { MaxTextsPerBatch = 64 },
                providerSupportsBatching: true);

            Assert.Equal(64, policy.MaxTextsPerBatch);
        }

        [Fact]
        public void For_CharacterLimit_IsTakenWhetherOrNotTheProviderBatches()
        {
            // Only the text count depends on the provider's answer. A single text is a whole
            // request either way, so the character limit is carried through unchanged.
            EmbeddingServiceOptions options = new EmbeddingServiceOptions { MaxCharactersPerBatch = 4096 };

            Assert.Equal(4096, BatchingPolicy.For(options, providerSupportsBatching: false).MaxCharactersPerBatch);
            Assert.Equal(4096, BatchingPolicy.For(options, providerSupportsBatching: true).MaxCharactersPerBatch);
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(-1, 100)]
        [InlineData(64, 0)]
        [InlineData(64, -1)]
        public void Constructor_LimitBelowOne_Throws(int maxTextsPerBatch, int maxCharactersPerBatch)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BatchingPolicy(maxTextsPerBatch, maxCharactersPerBatch));
        }
    }
}
