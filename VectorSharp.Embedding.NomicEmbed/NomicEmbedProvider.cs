using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VectorSharp.Embedding.NomicEmbed
{
    /// <summary>
    /// An embedding provider that uses the Nomic Embed Text v1.5 model via ONNX Runtime.
    /// Produces 768-dimensional embeddings with up to 8192 token context.
    /// </summary>
    public sealed class NomicEmbedProvider : IEmbeddingProvider
    {
        private const int EmbeddingDimension = 768;
        private const int MaxTokens = 8192;

        private readonly InferenceSession _session;
        private readonly FastBertTokenizer.BertTokenizer _tokenizer;
        private bool _disposed;

        /// <inheritdoc />
        public int Dimension => EmbeddingDimension;

        private NomicEmbedProvider(InferenceSession session, FastBertTokenizer.BertTokenizer tokenizer)
        {
            _session = session;
            _tokenizer = tokenizer;
        }

        /// <summary>
        /// Creates a new <see cref="NomicEmbedProvider"/> using the bundled model files.
        /// The model files are expected in a "Models" directory relative to the assembly location.
        /// </summary>
        /// <returns>A new <see cref="NomicEmbedProvider"/> instance.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the model files cannot be found.</exception>
        public static NomicEmbedProvider Create()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(NomicEmbedProvider).Assembly.Location)!;
            string modelsDir = Path.Combine(assemblyDir, "Models");
            return Create(modelsDir);
        }

        /// <summary>
        /// Creates a new <see cref="NomicEmbedProvider"/> using model files from the specified directory.
        /// </summary>
        /// <param name="modelDirectoryPath">The path to the directory containing model_int8.onnx and tokenizer.json.</param>
        /// <returns>A new <see cref="NomicEmbedProvider"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when modelDirectoryPath is null.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
        /// <exception cref="FileNotFoundException">Thrown when required model files are missing.</exception>
        public static NomicEmbedProvider Create(string modelDirectoryPath)
        {
            ArgumentNullException.ThrowIfNull(modelDirectoryPath);

            if (!Directory.Exists(modelDirectoryPath))
                throw new DirectoryNotFoundException($"Model directory not found: {modelDirectoryPath}");

            string modelPath = Path.Combine(modelDirectoryPath, "model_int8.onnx");
            string tokenizerPath = Path.Combine(modelDirectoryPath, "tokenizer.json");

            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX model file not found: {modelPath}");

            if (!File.Exists(tokenizerPath))
                throw new FileNotFoundException($"Tokenizer file not found: {tokenizerPath}");

            SessionOptions sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            InferenceSession session = new InferenceSession(modelPath, sessionOptions);

            FastBertTokenizer.BertTokenizer tokenizer = new FastBertTokenizer.BertTokenizer();
            using (Stream tokenizerStream = File.OpenRead(tokenizerPath))
            {
                tokenizer.LoadTokenizerJson(tokenizerStream);
            }

            return new NomicEmbedProvider(session, tokenizer);
        }

        /// <inheritdoc />
        public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(text);

            float[] result = EmbedInternal(text, purpose);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                _session.Dispose();
                _disposed = true;
            }
        }

        private float[] EmbedInternal(string text, EmbeddingPurpose purpose)
        {
            // Step 1: Prepend task prefix based on purpose
            string prefix = purpose == EmbeddingPurpose.Query ? "search_query: " : "search_document: ";
            string prefixedText = prefix + text;

            // Step 2: Tokenize using FastBertTokenizer
            (Memory<long> inputIdsMem, Memory<long> attentionMaskMem, Memory<long> tokenTypeIdsMem) =
                _tokenizer.Encode(prefixedText, MaxTokens);

            int sequenceLength = inputIdsMem.Length;

            // Step 3: Build input tensors
            long[] inputIds = inputIdsMem.ToArray();
            long[] attentionMask = attentionMaskMem.ToArray();
            long[] tokenTypeIds = tokenTypeIdsMem.ToArray();

            int[] shape = new int[] { 1, sequenceLength };

            DenseTensor<long> inputIdsTensor = new DenseTensor<long>(inputIds, shape);
            DenseTensor<long> attentionMaskTensor = new DenseTensor<long>(attentionMask, shape);
            DenseTensor<long> tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, shape);

            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            // Step 4: Run inference
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
            DisposableNamedOnnxValue output = results.First();
            Tensor<float> outputTensor = output.AsTensor<float>();

            // Step 5: Mean pooling (average across token dimension, respecting attention mask)
            float[] pooled = new float[EmbeddingDimension];
            int tokenCount = 0;

            for (int t = 0; t < sequenceLength; t++)
            {
                if (attentionMask[t] == 1)
                {
                    for (int d = 0; d < EmbeddingDimension; d++)
                    {
                        pooled[d] += outputTensor[0, t, d];
                    }

                    tokenCount++;
                }
            }

            if (tokenCount > 0)
            {
                for (int d = 0; d < EmbeddingDimension; d++)
                {
                    pooled[d] /= tokenCount;
                }
            }

            // Step 6: L2 normalize
            float magnitude = 0;
            for (int d = 0; d < EmbeddingDimension; d++)
            {
                magnitude += pooled[d] * pooled[d];
            }

            magnitude = MathF.Sqrt(magnitude);

            if (magnitude > 0)
            {
                for (int d = 0; d < EmbeddingDimension; d++)
                {
                    pooled[d] /= magnitude;
                }
            }

            return pooled;
        }
    }
}
