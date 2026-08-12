namespace VectorSharp.Reranking
{
    /// <summary>
    /// Defines the contract for a component that reorders a candidate set against a query.
    /// This is the second stage of retrieval: a vector or hybrid search produces candidates
    /// cheaply, and a reranker scores them properly.
    /// Implementations may use a local model, remote API calls, or any other source.
    /// Each instance owns its own resources and must be disposed when no longer needed.
    /// Individual instances are NOT required to be thread-safe.
    /// </summary>
    /// <remarks>
    /// There is no service around this interface on purpose. Reranking happens once per user
    /// query against a candidate set that is already in hand, not as a bulk pipeline, so the
    /// worker pool and queue that earn their place in embedding would only add machinery here.
    /// </remarks>
    public interface IRerankProvider : IDisposable
    {
        /// <summary>
        /// Scores the given documents against the query and returns the best ones.
        /// </summary>
        /// <param name="query">The query the documents are judged against.</param>
        /// <param name="documents">The candidates, typically the results of a vector search.
        /// Positions in this list are what the returned matches refer to, so the list must not be
        /// modified until the call has returned and its indexes have been resolved. Passing a
        /// collection the caller goes on mutating invalidates every index silently: the count
        /// still matches, nothing throws, and the matches simply point at different documents.</param>
        /// <param name="topN">The maximum number of matches to return. A provider returns fewer
        /// when fewer documents were supplied. Named for the term the rerank APIs themselves use,
        /// rather than the <c>count</c> of VectorSharp.Storage searches.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The matches ordered by score descending, and usage when the provider reports it.</returns>
        /// <remarks>
        /// Implementations should reject a null query, a null document list, and a null element
        /// within it, with <see cref="ArgumentNullException"/>; and a <paramref name="topN"/> below
        /// 1 with <see cref="ArgumentOutOfRangeException"/>. Rejecting is the point: an empty
        /// result is a real answer here, so it must not double as an error signal.
        /// An empty document list is not an error either — the answer is no matches.
        ///
        /// A provider whose backing API caps how many candidates it accepts should refuse a longer
        /// list rather than truncate it. Truncation leaves every returned index valid and every
        /// assertion satisfied while candidates disappear, which is the failure a caller cannot see.
        ///
        /// Argument rejection may surface either synchronously or as a faulted task, since a
        /// provider that validates inside an <c>async</c> method necessarily does the latter.
        /// Callers should await the call to observe it rather than expecting a throw at the call
        /// site.
        /// </remarks>
        Task<RerankResult> RerankAsync(string query, IReadOnlyList<string> documents, int topN, CancellationToken cancellationToken = default);
    }
}
