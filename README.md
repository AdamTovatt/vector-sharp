# VectorSharp

A high-performance, zero-dependency .NET library for in-process vector similarity search. Supports both in-memory and disk-backed storage with SIMD-optimized cosine similarity.

## Why VectorSharp?

Most vector search solutions require running a separate service, Docker containers, or pulling in heavy native dependencies. VectorSharp is a single NuGet package that works in-process. No infrastructure, no external processes, no native binaries.

- **Zero dependencies** - just one NuGet install
- **In-process** - runs inside your application, no separate service
- **High performance** - SIMD-optimized similarity with automatic parallelization
- **Disk-backed storage** - memory-mapped files for datasets that don't fit in RAM
- **Multi-store search** - query across multiple stores in a single call
- **Generic keys** - use `int`, `long`, `Guid`, or any unmanaged struct as identifiers
- **Thread-safe** - concurrent reads with exclusive writes

## Install

```
dotnet add package VectorSharp.Storage
```

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

## Core Concepts

### Vector Stores

A vector store holds vectors and supports similarity search. VectorSharp provides two implementations behind a shared interface:

```csharp
// In-memory - fast, limited by available RAM
CosineVectorStore<int> memoryStore = VectorStore.Create<int>("my-store", 768);

// Disk-backed - uses memory-mapped files, handles larger datasets
DiskVectorStore<int> diskStore = VectorStore.OpenFile<int>("my-store", "vectors.dat", 768);
```

Both implement `IVectorStore<TKey>` and can be used interchangeably.

### Key Types

The key type is generic - use whatever identifier your data already has. The constraint is `where TKey : unmanaged, IEquatable<TKey>`, which covers the common cases:

```csharp
// Integer keys (e.g. database primary keys)
IVectorStore<int> store = VectorStore.Create<int>("store", 768);

// Long keys
IVectorStore<long> store = VectorStore.Create<long>("store", 768);

// Guid keys
IVectorStore<Guid> store = VectorStore.Create<Guid>("store", 768);
```

### Search Results

Search results are sorted by similarity score (highest first) and include the store name so you know where each result came from:

```csharp
IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(queryVector, count: 5);

SearchResult<int> best = results[0];
// best.Id       - your key (int, Guid, etc.)
// best.Score    - cosine similarity score
// best.StoreName - "my-store"
```

## Multi-Store Search

Search across multiple stores in a single call. Results are merged by score and the global top-K is returned:

```csharp
IVectorStore<int> codebaseA = VectorStore.Create<int>("codebase-a", 768);
IVectorStore<int> codebaseB = VectorStore.Create<int>("codebase-b", 768);
DiskVectorStore<int> archive = VectorStore.OpenFile<int>("archive", "archive.dat", 768);

// Search one
IReadOnlyList<SearchResult<int>> results = await codebaseA.FindMostSimilarAsync(query, 10);

// Search any combination
IReadOnlyList<SearchResult<int>> results = await VectorSearch.SearchAsync(query, 10, codebaseA, codebaseB);

// Search all three (including mixed in-memory and disk-backed)
IReadOnlyList<SearchResult<int>> results = await VectorSearch.SearchAsync(
    query, 10, codebaseA, codebaseB, archive);
```

Each store is queried in parallel. Results include `StoreName` so you know which store each hit came from.

## Disk-Backed Storage

For datasets too large for RAM, use `DiskVectorStore`. It uses memory-mapped files for sequential scan search - the OS page cache handles caching transparently:

```csharp
// Create or open a disk-backed store
DiskVectorStore<int> store = VectorStore.OpenFile<int>("large-store", "vectors.dat", 768);

// Same API as in-memory
await store.AddAsync(42, embeddingVector);
IReadOnlyList<SearchResult<int>> results = await store.FindMostSimilarAsync(queryVector, 10);

// Remove marks the record as deleted (excluded from search)
await store.RemoveAsync(42);

// Compact rewrites the file without deleted records to reclaim space
await store.CompactAsync();

// Always dispose when done (cleans up memory-mapped file handles)
store.Dispose();
```

### Building a Disk Store from an In-Memory Store

You can build vectors in memory and then save to disk format:

```csharp
// Build in memory
CosineVectorStore<int> memStore = VectorStore.Create<int>("builder", 768);
foreach (MyDocument doc in documents)
{
    float[] embedding = await embedder.EmbedAsync(doc.Content);
    await memStore.AddAsync(doc.Id, embedding);
}

// Save to disk
using (FileStream fs = File.Create("vectors.dat"))
{
    await memStore.SaveAsync(fs);
}

// Open as disk-backed store
DiskVectorStore<int> diskStore = VectorStore.OpenFile<int>("docs", "vectors.dat", 768);
```

The binary format is shared - files saved by `CosineVectorStore` can be opened by `DiskVectorStore` and vice versa.

## Persistence

In-memory stores can be saved to and loaded from any stream:

```csharp
CosineVectorStore<Guid> store = VectorStore.Create<Guid>("my-store", 768);

// Save
using (FileStream fs = File.Create("vectors.dat"))
{
    await store.SaveAsync(fs);
}

// Load (replaces current contents)
using (FileStream fs = File.OpenRead("vectors.dat"))
{
    await store.LoadAsync(fs);
}
```

## Typical Usage Pattern

VectorSharp stores vectors and returns your keys. Your existing database stores the actual content. This keeps the vector store compact - just IDs and float arrays:

```csharp
// Your data lives in Postgres (or wherever)
MyDocument doc = await repository.GetByIdAsync(docId);
float[] embedding = await embedder.EmbedAsync(doc.Content);

// Vector store only holds the ID and embedding
await vectorStore.AddAsync(doc.Id, embedding);

// Search returns IDs - you fetch content from your own database
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

Disk-backed search is only ~1.3x slower than in-memory when the OS page cache is warm (typical for repeated searches against the same store).

### Memory Usage

| Vectors (768-dim) | Approximate Memory |
|--------------------|-------------------|
| 1,000 | ~3 MB |
| 10,000 | ~30 MB |
| 100,000 | ~300 MB |

### When to Use VectorSharp

VectorSharp is designed for in-process vector search with up to a few hundred thousand vectors. If you need to search millions of vectors, distributed sharding, or sub-millisecond latency at scale, use a dedicated vector database.

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

### SearchResult&lt;TKey&gt;

```csharp
public readonly struct SearchResult<TKey>
{
    public TKey Id { get; }
    public float Score { get; }
    public string StoreName { get; }
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

### CosineVectorStore&lt;TKey&gt; (additional methods)

```csharp
public Task SaveAsync(Stream stream);
public Task LoadAsync(Stream stream);
```

### DiskVectorStore&lt;TKey&gt; (additional methods)

```csharp
public Task CompactAsync();
```

## License

MIT
