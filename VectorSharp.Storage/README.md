# VectorSharp.Storage

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Storage-blue.svg)](https://www.nuget.org/packages/VectorSharp.Storage)

In-memory and disk-backed vector storage with SIMD-optimized cosine similarity search. Zero dependencies.

## Install

```
dotnet add package VectorSharp.Storage
```

## Features

- **SIMD-optimized** cosine similarity with automatic parallelization
- **In-memory and disk-backed** storage behind a shared interface
- **Multi-store search** — query across any combination of stores in a single call
- **Generic keys** — use `int`, `long`, `Guid`, or any unmanaged struct
- **Thread-safe** — concurrent reads with exclusive writes
- **Pre-computed magnitudes** — ~30-40% fewer operations per search
- **Sorted results** — results returned by similarity score, highest first
- **Versioned binary format** — shared between in-memory persistence and disk stores

## Quick Start

```csharp
using VectorSharp.Storage;

// Create an in-memory store (768 is a common embedding dimension)
CosineVectorStore<int> store = VectorStore.Create<int>("my-store", 768);

// Add vectors with your own IDs
await store.AddAsync(1, embeddingVector);
await store.AddAsync(2, anotherVector);

// Find similar vectors
IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(queryVector, count: 10);

foreach (SearchResult<int> result in results)
{
    Console.WriteLine($"ID: {result.Id}, Score: {result.Score}");
}
```

## Vector Stores

Two implementations behind a shared `IVectorStore<TKey>` interface:

```csharp
// In-memory — fast, limited by available RAM
CosineVectorStore<int> memoryStore = VectorStore.Create<int>("my-store", 768);

// Disk-backed — uses memory-mapped files, handles larger datasets
DiskVectorStore<int> diskStore = VectorStore.OpenFile<int>("my-store", "vectors.dat", 768);
```

### Key Types

Use whatever identifier your data already has:

```csharp
IVectorStore<int> store = VectorStore.Create<int>("store", 768);    // database primary keys
IVectorStore<long> store = VectorStore.Create<long>("store", 768);  // long IDs
IVectorStore<Guid> store = VectorStore.Create<Guid>("store", 768);  // GUIDs
```

The constraint is `where TKey : unmanaged, IEquatable<TKey>`.

### Search Results

Results are sorted by similarity score (highest first) and include the store name:

```csharp
SearchResult<int> best = results[0];
// best.Id        — your key
// best.Score     — cosine similarity score
// best.StoreName — "my-store"
```

## Multi-Store Search

Search across multiple stores in a single call. Results are merged by score:

```csharp
IReadOnlyList<SearchResult<int>> results = await VectorSearch.SearchAsync(
    queryVector, 10, storeA, storeB, diskStore);
```

Stores are queried in parallel. You can mix in-memory and disk-backed stores freely.

## Disk-Backed Storage

For datasets too large for RAM:

```csharp
DiskVectorStore<int> store = VectorStore.OpenFile<int>("large-store", "vectors.dat", 768);

await store.AddAsync(42, embeddingVector);
IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(queryVector, 10);

// Remove marks the record as deleted (excluded from search)
await store.RemoveAsync(42);

// Compact rewrites the file without deleted records
await store.CompactAsync();

store.Dispose();
```

### Building from an In-Memory Store

```csharp
CosineVectorStore<int> memStore = VectorStore.Create<int>("builder", 768);
// ... add vectors ...

// Save to disk format
using (FileStream fs = File.Create("vectors.dat"))
    await memStore.SaveAsync(fs);

// Open as disk-backed store
DiskVectorStore<int> diskStore = VectorStore.OpenFile<int>("docs", "vectors.dat", 768);
```

The binary format is shared — files saved by `CosineVectorStore` can be opened by `DiskVectorStore` and vice versa.

## Typical Usage Pattern

VectorSharp stores vectors and returns your keys. Your existing database stores the actual content:

```csharp
// Your data lives in Postgres (or wherever)
MyDocument doc = await repository.GetByIdAsync(docId);
float[] embedding = await embedder.EmbedAsync(doc.Content);

// Vector store only holds the ID and embedding
await vectorStore.AddAsync(doc.Id, embedding);

// Search returns IDs — you fetch content from your own database
IReadOnlyList<SearchResult<int>> results = await vectorStore.FindMostSimilarAsync(queryEmbedding, 10);
foreach (SearchResult<int> result in results)
{
    MyDocument matched = await repository.GetByIdAsync(result.Id);
    Console.WriteLine($"Score: {result.Score:F3} - {matched.Title}");
}
```

## Performance

Benchmarked with 10,000 vectors at 768 dimensions:

| Operation | Performance |
|-----------|-------------|
| In-memory search | ~5 ms/query |
| Disk-backed search | ~7 ms/query |
| Concurrent search | ~1.6 ms/query |
| Insertion | ~285,000 vectors/sec |
| Save to file | ~170,000 vectors/sec |
| Load from file | ~830,000 vectors/sec |

### Memory Usage

| Vectors (768-dim) | Approximate Memory |
|--------------------|-------------------|
| 1,000 | ~3 MB |
| 10,000 | ~30 MB |
| 100,000 | ~300 MB |

Designed for up to a few hundred thousand vectors. For millions of vectors or distributed workloads, use a dedicated vector database.

## API Reference

### IVectorStore&lt;TKey&gt;

```csharp
public interface IVectorStore<TKey> : IDisposable
    where TKey : unmanaged, IEquatable<TKey>
{
    string Name { get; }
    int Dimension { get; }
    int Count { get; }
    Task AddAsync(TKey id, float[] values);
    Task<bool> RemoveAsync(TKey id);
    Task<IReadOnlyList<SearchResult<TKey>>> FindMostSimilarAsync(float[] queryVector, int count);
}
```

### VectorStore (Factory)

```csharp
public static class VectorStore
{
    public static CosineVectorStore<TKey> Create<TKey>(string name, int dimension);
    public static DiskVectorStore<TKey> OpenFile<TKey>(string name, string filePath, int dimension);
}
```

### VectorSearch

```csharp
public static class VectorSearch
{
    public static Task<IReadOnlyList<SearchResult<TKey>>> SearchAsync<TKey>(
        float[] queryVector, int count, params IVectorStore<TKey>[] stores);
}
```

## License

MIT
