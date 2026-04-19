# VectorSharp

[![Tests](https://github.com/AdamTovatt/vector-sharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/AdamTovatt/vector-sharp/actions/workflows/dotnet.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

A high-performance .NET library for in-process vector similarity search and text embedding. No external services, no Docker, no infrastructure — just NuGet packages that work.

## Packages

| Package | Description | Install |
|---------|-------------|---------|
| [VectorSharp.Storage](VectorSharp.Storage/README.md) | In-memory and disk-backed vector storage with SIMD-optimized cosine similarity | `dotnet add package VectorSharp.Storage` |
| [VectorSharp.Embedding](VectorSharp.Embedding/README.md) | Channel-based embedding service with configurable parallelism | `dotnet add package VectorSharp.Embedding` |
| [VectorSharp.Embedding.NomicEmbed](VectorSharp.Embedding.NomicEmbed/README.md) | Nomic Embed Text v1.5 model for local inference (768-dim, 8192 token context) | `dotnet add package VectorSharp.Embedding.NomicEmbed` |
| [VectorSharp.Chunking](VectorSharp.Chunking/README.md) | Streaming text chunker with predefined formats for Markdown and C# | `dotnet add package VectorSharp.Chunking` |

## Quick Start

```bash
dotnet add package VectorSharp.Storage
dotnet add package VectorSharp.Embedding.NomicEmbed
```

```csharp
using VectorSharp.Storage;
using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

// Create an embedding service with local model inference
await using EmbeddingService embedder = new EmbeddingService(NomicEmbedProvider.Create);

// Create a vector store
CosineVectorStore<int> store = VectorStore.Create<int>("my-store", embedder.Dimension);

// Embed and store documents
float[] embedding = await embedder.EmbedAsync("some document text", EmbeddingPurpose.Document);
await store.AddAsync(1, embedding);

// Search with a query
float[] queryEmbedding = await embedder.EmbedAsync("find similar docs", EmbeddingPurpose.Query);
IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(queryEmbedding, count: 10);
```

## License

MIT
