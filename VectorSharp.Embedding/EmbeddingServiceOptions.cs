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
        /// When the channel is full, <see cref="EmbeddingService.EmbedAsync"/> will
        /// wait until space becomes available. Default is 1000.
        /// </summary>
        public int ChannelCapacity { get; init; } = 1000;
    }
}
