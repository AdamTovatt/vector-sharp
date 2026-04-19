# VectorSharp.Embedding.NomicEmbed

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Embedding.NomicEmbed-blue.svg)](https://www.nuget.org/packages/VectorSharp.Embedding.NomicEmbed)

Bundles the [Nomic Embed Text v1.5](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5) model for local inference via ONNX Runtime. One NuGet install, no API keys, no external services.

## Install

```
dotnet add package VectorSharp.Embedding.NomicEmbed
```

## Model Details

| Property | Value |
|----------|-------|
| Model | Nomic Embed Text v1.5 |
| Dimensions | 768 |
| Max tokens | 8192 |
| Quantization | Int8 |
| Package size | ~137 MB |
| Architecture | NomicBERT (137M parameters) |

## Usage

### Standalone

```csharp
using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

using NomicEmbedProvider provider = NomicEmbedProvider.Create();

float[] embedding = await provider.EmbedAsync("hello world");
// embedding.Length == 768
```

### With EmbeddingService

For managed concurrency and channel-based request handling:

```csharp
await using EmbeddingService embedder = new EmbeddingService(
    NomicEmbedProvider.Create,
    new EmbeddingServiceOptions { Concurrency = 2 }
);

// Embed documents for storage
float[] docEmbedding = await embedder.EmbedAsync("document to store", EmbeddingPurpose.Document);

// Embed search queries
float[] queryEmbedding = await embedder.EmbedAsync("search for this", EmbeddingPurpose.Query);
```

### End-to-End with VectorSharp.Storage

```csharp
using VectorSharp.Storage;
using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

await using EmbeddingService embedder = new EmbeddingService(NomicEmbedProvider.Create);
using CosineVectorStore<int> store = VectorStore.Create<int>("docs", embedder.Dimension);

// Index documents
foreach (MyDocument doc in documents)
{
    float[] embedding = await embedder.EmbedAsync(doc.Content, EmbeddingPurpose.Document);
    await store.AddAsync(doc.Id, embedding);
}

// Search
float[] queryEmbedding = await embedder.EmbedAsync("find similar docs", EmbeddingPurpose.Query);
IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(queryEmbedding, 10);
```

## Document vs Query Embeddings

Nomic Embed uses task-specific prefixes during training. The model automatically applies `search_document:` or `search_query:` based on the `EmbeddingPurpose` you pass:

- **`EmbeddingPurpose.Document`** (default) — use when embedding text for storage
- **`EmbeddingPurpose.Query`** — use when embedding search input

This asymmetry helps the model match questions to answers even when the wording differs. For example, a query "how to sort a list" will score highly against a document about "sorting algorithms" even though the phrasing is different.

## Custom Model Path

If you want to use a different model directory (e.g., a different quantization):

```csharp
using NomicEmbedProvider provider = NomicEmbedProvider.Create("/path/to/model/directory");
```

The directory must contain `model_int8.onnx` and `vocab.txt`.

## Performance Tuning

Each `EmbeddingService` worker runs its own `NomicEmbedProvider` with its own ONNX Runtime session. ONNX Runtime also parallelizes internally within each session using `IntraOpNumThreads`. These two knobs interact — the best configuration depends on your use case.

### Benchmarks (Apple M-series, 18 cores, int8 model)

| Config | Throughput | CPU usage | Memory |
|--------|-----------|-----------|--------|
| 1 worker, default threads | ~205/sec | ~98% | 4 MB |
| 2 workers, default threads | ~325/sec | ~98% | 8 MB |
| 2 workers, 4 threads each | ~281/sec | ~46% | 9 MB |
| 2 workers, 3 threads each | ~259/sec | ~35% | 9 MB |
| 9 workers, 2 threads each | ~680/sec | ~96% | 34 MB |

### Recommendations

- **Background indexing alongside other work**: 2 workers, 3-4 threads each. Good throughput without saturating the CPU.
- **Dedicated bulk indexing**: higher concurrency with 2 threads each (e.g., `cores / 2` workers).
- **Single request at a time**: 1 worker, default threads. Lowest latency per embedding.

### Configuring IntraOpNumThreads

```csharp
NomicEmbedOptions nomicOptions = new NomicEmbedOptions { IntraOpNumThreads = 4 };

await using EmbeddingService embedder = new EmbeddingService(
    () => NomicEmbedProvider.Create(nomicOptions),
    new EmbeddingServiceOptions { Concurrency = 2 }
);
```

When `IntraOpNumThreads` is not set, ONNX Runtime defaults to using all available cores per session — which means even a single worker will use significant CPU.

## Memory Usage

Each `NomicEmbedProvider` instance uses ~4 MB of managed memory. The ONNX model weights (~137 MB on disk) are memory-mapped by the OS and shared across sessions, so scaling concurrency has minimal additional memory cost.

## License

MIT

The Nomic Embed Text v1.5 model is licensed under [Apache 2.0](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5).
