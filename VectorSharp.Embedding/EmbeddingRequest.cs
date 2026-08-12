namespace VectorSharp.Embedding
{
    /// <summary>
    /// Internal message type representing a pending embedding request in the channel.
    /// Carries a whole batch rather than a single text, so one batch is one unit of work for
    /// one worker and reaches the provider as one call.
    /// </summary>
    internal sealed class EmbeddingRequest
    {
        /// <summary>
        /// The texts to produce embeddings for. Already sized to the batching policy by the
        /// service, so a worker hands this list to the provider as it stands.
        /// </summary>
        public required IReadOnlyList<string> Texts { get; init; }

        /// <summary>
        /// The intended purpose of the embeddings.
        /// </summary>
        public required EmbeddingPurpose Purpose { get; init; }

        /// <summary>
        /// The token the caller passed. Carried through so the worker can stop the provider call
        /// itself: completing the caller's task is not enough when the call in flight is a whole
        /// batch being billed by a remote API.
        /// </summary>
        public required CancellationToken CallerToken { get; init; }

        /// <summary>
        /// The completion source that the caller awaits. The worker sets the result
        /// after producing the embeddings, or sets an exception if inference fails.
        /// </summary>
        public required TaskCompletionSource<EmbeddingResult> CompletionSource { get; init; }
    }
}
