namespace VectorSharp.Reranking
{
    /// <summary>
    /// The outcome of a rerank call: the candidates that survived, best first, and what the
    /// provider reported spending if it reported anything.
    /// </summary>
    public sealed class RerankResult
    {
        /// <summary>
        /// Gets the reranked candidates, ordered by <see cref="RerankMatch.Score"/> descending.
        /// Holds at most the topN the caller asked for, and may hold fewer when fewer documents
        /// were supplied.
        /// </summary>
        public required IReadOnlyList<RerankMatch> Matches { get; init; }

        /// <summary>
        /// Gets what the call cost, or null when the provider reported nothing.
        /// </summary>
        /// <remarks>
        /// Null means unknown, never free. A caller metering spend has to treat it as a gap in its
        /// accounting rather than as a zero, which is why an unreported count is not defaulted.
        /// </remarks>
        public RerankUsage? Usage { get; init; }
    }
}
