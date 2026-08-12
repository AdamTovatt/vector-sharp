namespace VectorSharp.Embedding
{
    /// <summary>
    /// Configuration options for <see cref="EmbeddingService"/>.
    /// </summary>
    public sealed class EmbeddingServiceOptions
    {
        /// <summary>
        /// Gets the number of concurrent embedding workers.
        /// Each worker owns its own <see cref="IEmbeddingProvider"/> instance created by the factory.
        /// Default is 1.
        /// </summary>
        public int Concurrency { get; init; } = 1;

        /// <summary>
        /// Gets the maximum number of pending embedding requests in the channel.
        /// When the channel is full, any of the service's embed methods will wait until space
        /// becomes available. Default is 1000.
        /// </summary>
        /// <remarks>
        /// Counted in batches, not texts: one queued item is one batch of up to
        /// <see cref="MaxTextsPerBatch"/> texts. Against a provider that does not batch a batch is
        /// one text, so the default bounds the queue at 1000 texts; against one that does, the
        /// same default bounds it at 1000 batches, which is 64000 texts at the default
        /// <see cref="MaxTextsPerBatch"/>. Lower this when the queued text itself is large enough
        /// to matter, since the queue holds the strings until a worker takes them.
        /// </remarks>
        public int ChannelCapacity { get; init; } = 1000;

        /// <summary>
        /// Gets the largest number of texts the service will put in one request to the provider.
        /// Default is 64.
        /// </summary>
        /// <remarks>
        /// Hosted embedding APIs cap the number of inputs per request, so a batch larger than a
        /// provider accepts has to be split before it is sent. Set this to what the API accepts.
        ///
        /// Ignored by a provider whose <see cref="IEmbeddingProvider.SupportsBatching"/> is false,
        /// which is sent one text per request whatever this says.
        /// </remarks>
        public int MaxTextsPerBatch { get; init; } = 64;

        /// <summary>
        /// Gets the largest total number of characters the service will put in one request to the
        /// provider. Default is 100000.
        /// </summary>
        /// <remarks>
        /// The other cap hosted APIs apply, independent of the text count: a batch is closed when
        /// either limit would be exceeded. A single text longer than this limit is still sent, on
        /// its own — splitting a text is the chunker's job, not this one's.
        ///
        /// Also only reached by a provider that batches, since one text is already a whole request
        /// for a provider that does not.
        /// </remarks>
        public int MaxCharactersPerBatch { get; init; } = 100000;
    }
}
