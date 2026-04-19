namespace VectorSharp.Embedding.NomicEmbed
{
    /// <summary>
    /// Configuration options for <see cref="NomicEmbedProvider"/>.
    /// </summary>
    public sealed class NomicEmbedOptions
    {
        /// <summary>
        /// Gets the number of threads used for parallelism within a single ONNX inference call.
        /// When null, ONNX Runtime uses its default (typically all available cores).
        /// Lower values are useful when running multiple provider instances concurrently.
        /// </summary>
        public int? IntraOpNumThreads { get; init; }
    }
}
