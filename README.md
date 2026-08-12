# VectorSharp

[![Tests](https://github.com/AdamTovatt/vector-sharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/AdamTovatt/vector-sharp/actions/workflows/dotnet.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

A high-performance .NET library for in-process vector similarity search and text embedding. No external services required, no Docker, no infrastructure — just NuGet packages that work. Search, storage and chunking run entirely in your process; the embedding and reranking packages let you reach a hosted API when you want one, but never oblige you to.

## Packages

| Package | Description |
|---------|-------------|
| [VectorSharp.Storage](VectorSharp.Storage/README.md) | In-memory and disk-backed vector storage with SIMD-optimized cosine similarity |
| [VectorSharp.Embedding](VectorSharp.Embedding/README.md) | Channel-based embedding service with configurable parallelism, request batching and token usage reporting |
| [VectorSharp.Embedding.NomicEmbed](VectorSharp.Embedding.NomicEmbed/README.md) | Nomic Embed Text v1.5 model for local inference (768-dim, 8192 token context) |
| [VectorSharp.Chunking](VectorSharp.Chunking/README.md) | Streaming text chunker with position-carrying chunks and predefined formats for Markdown, C#, JavaScript/TypeScript, HTML, CSS, Python and plain text |
| [VectorSharp.Reranking](VectorSharp.Reranking/README.md) | Reranking abstractions for the second stage after a vector or hybrid search |

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
