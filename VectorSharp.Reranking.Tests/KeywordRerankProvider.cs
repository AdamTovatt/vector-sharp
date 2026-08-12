namespace VectorSharp.Reranking.Tests
{
    /// <summary>
    /// A deterministic reranker for testing, scoring each document by how many of the query's
    /// words it contains. Stands in for a real provider closely enough to exercise the contract:
    /// it scores every candidate, sorts, truncates to topN, and reports the positions it was
    /// given rather than the text it was given.
    /// </summary>
    internal sealed class KeywordRerankProvider : IRerankProvider
    {
        private readonly RerankUsage? _usage;
        private bool _disposed;

        public KeywordRerankProvider(RerankUsage? usage = null)
        {
            _usage = usage;
        }

        public Task<RerankResult> RerankAsync(string query, IReadOnlyList<string> documents, int topN, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(documents);
            ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);

            for (int i = 0; i < documents.Count; i++)
            {
                if (documents[i] == null)
                    throw new ArgumentNullException($"{nameof(documents)}[{i}]");
            }

            cancellationToken.ThrowIfCancellationRequested();

            string[] queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            List<RerankMatch> scored = new List<RerankMatch>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                scored.Add(new RerankMatch { Index = i, Score = ScoreOf(documents[i], queryWords) });
            }

            List<RerankMatch> best = scored
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.Index)
                .Take(topN)
                .ToList();

            return Task.FromResult(new RerankResult { Matches = best, Usage = _usage });
        }

        private static float ScoreOf(string document, string[] queryWords)
        {
            int hits = 0;
            foreach (string word in queryWords)
            {
                if (document.Contains(word, StringComparison.OrdinalIgnoreCase))
                    hits++;
            }

            return hits;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
