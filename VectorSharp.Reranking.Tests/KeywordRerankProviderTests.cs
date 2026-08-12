namespace VectorSharp.Reranking.Tests
{
    /// <summary>
    /// Runs the <see cref="IRerankProvider"/> contract against the in-repo double. A provider
    /// added later inherits the same base and gets the same checks.
    /// </summary>
    public class KeywordRerankProviderTests : RerankProviderContractTests
    {
        protected override IRerankProvider CreateProvider(RerankUsage? usage = null)
        {
            return new KeywordRerankProvider(usage);
        }
    }
}
