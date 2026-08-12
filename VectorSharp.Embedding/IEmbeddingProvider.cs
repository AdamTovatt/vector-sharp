namespace VectorSharp.Embedding
{
    /// <summary>
    /// Defines the contract for a component that produces vector embeddings from text.
    /// Implementations may use local model inference, remote API calls, or any other source.
    /// Each instance owns its own resources and must be disposed when no longer needed.
    /// Individual instances are NOT required to be thread-safe — the <see cref="EmbeddingService"/>
    /// creates one instance per worker to avoid shared state.
    /// </summary>
    /// <remarks>
    /// There is deliberately no single-text counterpart to <see cref="EmbedBatchWithUsageAsync"/>:
    /// <see cref="EmbeddingService"/> reaches a provider only through the batch method, a single
    /// text being a batch of one, so a single-text usage member here would never be called.
    /// The service exposes the single-text form to callers instead.
    /// </remarks>
    public interface IEmbeddingProvider : IDisposable
    {
        /// <summary>
        /// Gets the dimensionality of the embedding vectors produced by this provider.
        /// </summary>
        int Dimension { get; }

        /// <summary>
        /// Gets whether this provider embeds several texts in one call more cheaply than one at a
        /// time. False unless a provider says otherwise.
        /// </summary>
        /// <remarks>
        /// This is what <see cref="EmbeddingService"/> uses to decide whether to group a caller's
        /// texts into batches at all. A provider that has not overridden the batch methods gains
        /// nothing from grouping — the default implementations loop the single-text method, so a
        /// group would run serially inside one worker instead of spreading across them. Answering
        /// false keeps such a provider, a local ONNX model in particular, on one text per unit of
        /// work and therefore parallel across workers.
        ///
        /// Override to true together with <see cref="EmbedBatchWithUsageAsync"/>, and ideally
        /// <see cref="EmbedBatchAsync"/> as well: claiming batching without implementing it is what
        /// turns the parallel path into a serial one.
        /// </remarks>
        bool SupportsBatching => false;

        /// <summary>
        /// Produces a vector embedding for the given text.
        /// </summary>
        /// <param name="text">The text to embed.</param>
        /// <param name="purpose">The intended purpose of the embedding. Providers that support
        /// task-specific embeddings (e.g., Nomic Embed) will use this to optimize the output.
        /// Providers that do not distinguish between purposes will ignore this parameter.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A float array of length <see cref="Dimension"/> representing the embedding.</returns>
        Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default);

        /// <summary>
        /// Produces vector embeddings for several texts in one call.
        /// </summary>
        /// <remarks>
        /// The default implementation loops <see cref="EmbedAsync"/>, so a provider that only
        /// implements the single-text method keeps working unchanged. Override it in providers
        /// that can embed an array in one request — against a remote API this is the difference
        /// between one HTTP round trip and one per text — and set
        /// <see cref="SupportsBatching"/> to true so the service groups texts for it.
        /// </remarks>
        // Deliberately a virtual interface member, not an extension method over
        // EmbedBatchWithUsageAsync. Folding it in would tidy the shape — there would be no way
        // left to override the wrong one of the two — at the cost of the case this member exists
        // for: a provider that batches but meters nothing can override this alone, and would
        // otherwise have to implement a usage-shaped method to say it has no usage to report.
        // Removing it stops being free once this ships as part of a released version, since
        // assemblies compiled against it bind to this signature, so decide against it once here
        // rather than each time the shape is reviewed.
        /// <param name="texts">The texts to embed.</param>
        /// <param name="purpose">The intended purpose of the embeddings.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>One embedding per input text, in the same order.</returns>
        async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(texts);

            float[][] vectors = new float[texts.Count][];
            for (int i = 0; i < texts.Count; i++)
            {
                vectors[i] = await EmbedAsync(texts[i], purpose, cancellationToken).ConfigureAwait(false);
            }

            return vectors;
        }

        /// <summary>
        /// Produces vector embeddings for several texts in one call, reporting what they cost if
        /// this provider meters that.
        /// </summary>
        /// <remarks>
        /// This is the method <see cref="EmbeddingService"/> calls, and the one to override in a
        /// provider that meters tokens. The default implementation calls
        /// <see cref="EmbedBatchAsync"/> and reports no usage, because a provider that has not
        /// been written to report usage has nothing truthful to say about it.
        ///
        /// The two batch methods are separately overridable, so a provider that batches and meters
        /// should override both: overriding only this one leaves a direct caller of
        /// <see cref="EmbedBatchAsync"/> looping single calls. That direction is deliberate — the
        /// service reaches a provider through this method, so a provider that overrides only
        /// <see cref="EmbedBatchAsync"/> (batches, does not meter) still gets one request per
        /// batch through the service.
        /// </remarks>
        /// <param name="texts">The texts to embed.</param>
        /// <param name="purpose">The intended purpose of the embeddings.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The embeddings, and usage when this provider reports it.</returns>
        async Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            float[][] vectors = await EmbedBatchAsync(texts, purpose, cancellationToken).ConfigureAwait(false);
            return new EmbeddingResult { Vectors = vectors, Usage = null };
        }
    }
}
