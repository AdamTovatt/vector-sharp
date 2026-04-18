using VectorSharp.Embedding;

namespace VectorSharp.Embedding.Tests
{
    /// <summary>
    /// A deterministic embedding provider for testing. Produces vectors
    /// derived from a hash of the input text so identical inputs always
    /// return identical embeddings within the same process.
    /// </summary>
    internal sealed class TestEmbeddingProvider : IEmbeddingProvider
    {
        private readonly TimeSpan _delay;
        private bool _disposed;
        private int _callCount;

        public int Dimension { get; }

        public int CallCount => _callCount;

        public TestEmbeddingProvider(int dimension = 768, TimeSpan? delay = null)
        {
            Dimension = dimension;
            _delay = delay ?? TimeSpan.Zero;
        }

        public async Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken);

            Interlocked.Increment(ref _callCount);

            // Deterministic vector from text using a simple hash-based seed
            int seed = 0;
            foreach (char c in text)
            {
                seed = (seed * 31) + c;
            }

            Random rng = new Random(seed);
            float[] result = new float[Dimension];
            for (int i = 0; i < Dimension; i++)
            {
                result[i] = (float)(rng.NextDouble() * 2 - 1);
            }

            return result;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
