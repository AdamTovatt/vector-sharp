# VectorSharp.Embedding

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Embedding-blue.svg)](https://www.nuget.org/packages/VectorSharp.Embedding)

Channel-based embedding service with configurable parallelism. Provider-agnostic — works with local ONNX models, remote HTTP endpoints, or any custom embedding source.

## Install

```
dotnet add package VectorSharp.Embedding
```

This package contains the core abstractions and service. For a ready-to-use model, install a model package like [VectorSharp.Embedding.NomicEmbed](../VectorSharp.Embedding.NomicEmbed/README.md).

## Features

- **Channel-based architecture** — any code can request embeddings, workers process them in the background
- **Configurable parallelism** — N concurrent workers, each with its own provider instance
- **Backpressure** — bounded channel prevents unbounded memory growth
- **Provider-agnostic** — implement `IEmbeddingProvider` for any embedding source
- **Purpose-aware** — `EmbeddingPurpose.Document` vs `EmbeddingPurpose.Query` for models that distinguish between them
- **Zero dependencies** — only uses in-box `System.Threading.Channels`

## Usage

```csharp
using VectorSharp.Embedding;

// Create a service with 2 concurrent workers
await using EmbeddingService embedder = new EmbeddingService(
    MyProvider.Create,
    new EmbeddingServiceOptions { Concurrency = 2 }
);

// Embed text
float[] embedding = await embedder.EmbedAsync("some text");

// With purpose (models like Nomic Embed use this to optimize output)
float[] docEmbedding = await embedder.EmbedAsync("document text", EmbeddingPurpose.Document);
float[] queryEmbedding = await embedder.EmbedAsync("search query", EmbeddingPurpose.Query);

// Batch embedding — requests are distributed across workers
float[][] embeddings = await embedder.EmbedBatchAsync(new[] { "text1", "text2", "text3" });
```

## How It Works

```
Caller ──EmbedAsync──▶ Channel Writer ──▶ [bounded channel] ──▶ Channel Reader ──▶ Worker N
  ▲                                                                                   │
  │                                                                                   ▼
  └──── await TCS.Task ◀──── TCS.SetResult(float[]) ◀── provider.EmbedAsync(text) ───┘
```

- Requests are queued in a bounded channel with natural backpressure
- N workers consume from the channel, each owning its own `IEmbeddingProvider` instance
- Errors in one request don't affect other requests or workers
- Disposal completes the channel, drains workers, and disposes all providers

## EmbeddingPurpose

Some embedding models produce different vectors for documents vs search queries. This helps bridge the gap between how content is written vs how people phrase questions.

```csharp
// Use Document when embedding text for storage
float[] docEmbedding = await embedder.EmbedAsync("sorting algorithms in Python", EmbeddingPurpose.Document);

// Use Query when embedding search input
float[] queryEmbedding = await embedder.EmbedAsync("how to sort a list", EmbeddingPurpose.Query);
```

Providers that don't distinguish between purposes simply ignore the parameter.

## Configuration

```csharp
EmbeddingServiceOptions options = new EmbeddingServiceOptions
{
    Concurrency = 4,       // Number of concurrent workers (default: 1)
    ChannelCapacity = 500  // Max pending requests before backpressure (default: 1000)
};
```

Note: each worker creates its own provider instance via the factory. For ONNX models, this means N copies of the model in memory.

## Implementing a Custom Provider

```csharp
public class HttpEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _client;
    private readonly string _endpoint;

    public int Dimension => 768;

    public HttpEmbeddingProvider(string endpoint)
    {
        _endpoint = endpoint;
        _client = new HttpClient();
    }

    public async Task<float[]> EmbedAsync(string text,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default)
    {
        // Call your remote embedding server
        HttpResponseMessage response = await _client.PostAsJsonAsync(_endpoint,
            new { text, purpose = purpose.ToString() }, cancellationToken);
        return await response.Content.ReadFromJsonAsync<float[]>(cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}

// Use it with the same EmbeddingService
await using EmbeddingService embedder = new EmbeddingService(
    () => new HttpEmbeddingProvider("https://my-server/embed"),
    new EmbeddingServiceOptions { Concurrency = 4 }
);
```

## API Reference

### IEmbeddingProvider

```csharp
public interface IEmbeddingProvider : IDisposable
{
    int Dimension { get; }
    Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
}
```

### EmbeddingService

```csharp
public sealed class EmbeddingService : IAsyncDisposable
{
    public EmbeddingService(Func<IEmbeddingProvider> providerFactory, EmbeddingServiceOptions? options = null);
    public int Dimension { get; }
    public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}
```

## License

MIT
