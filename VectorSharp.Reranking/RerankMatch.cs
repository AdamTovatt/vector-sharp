namespace VectorSharp.Reranking
{
    // A class rather than a struct, for the same reason TextChunk in VectorSharp.Chunking is one:
    // `required` is enforced only on object-creation expressions, so default(RerankMatch) or
    // FirstOrDefault() over an empty list would hand back Index 0 with score 0 — a match pointing
    // at the caller's first document, indistinguishable from a real one. As a class those
    // expressions produce a null the caller can see.

    /// <summary>
    /// One reranked candidate: which document it was, and how well the reranker judged it to
    /// answer the query.
    /// </summary>
    public sealed class RerankMatch
    {
        /// <summary>
        /// Gets the position of this document in the list passed to
        /// <see cref="IRerankProvider.RerankAsync"/>.
        /// </summary>
        /// <remarks>
        /// An index rather than the document text, because the caller is the one holding whatever
        /// sits behind each candidate — a row id, a chunk offset, a file path — and needs to get
        /// back to it. Returning the text would force a lookup by content, which is both slower
        /// and ambiguous when two candidates read the same.
        /// </remarks>
        public required int Index { get; init; }

        /// <summary>
        /// Gets the relevance score the provider gave this document. Higher is more relevant.
        /// </summary>
        /// <remarks>
        /// Comparable only within one call: providers differ in range and scale, and a score
        /// carries no meaning against a score from a different query or model.
        /// </remarks>
        public required float Score { get; init; }
    }
}
