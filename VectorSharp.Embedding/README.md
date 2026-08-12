# VectorSharp.Embedding

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Embedding-blue.svg)](https://www.nuget.org/packages/VectorSharp.Embedding)

Channel-based embedding service with configurable parallelism, request batching and token usage reporting. Provider-agnostic — works with local ONNX models, remote HTTP endpoints, or any custom embedding source.

## Install

```
dotnet add package VectorSharp.Embedding
```

This package contains the core abstractions and service. For a ready-to-use model, install a model package like [VectorSharp.Embedding.NomicEmbed](../VectorSharp.Embedding.NomicEmbed/README.md).

## Features

- **Channel-based architecture** — any code can request embeddings, workers process them in the background
- **Configurable parallelism** — N concurrent workers, each with its own provider instance
- **Real batching** — providers that embed an array in one call get whole batches under configurable size limits, so a remote API sees one request rather than one per text; providers that do not stay one text per call, spread across workers
- **Usage reporting** — a provider that meters tokens reports them back; one that does not reports nothing at all, rather than a zero you could mistake for a free call
- **Backpressure** — a bounded channel caps how many batches wait to be picked up, so a queue cannot outgrow it faster than the workers drain it
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

// Batch embedding — split into batches, each batch one call to the provider
float[][] embeddings = await embedder.EmbedBatchAsync(new[] { "text1", "text2", "text3" });

// The same call, with what the provider reported spending
EmbeddingResult result = await embedder.EmbedBatchWithUsageAsync(new[] { "text1", "text2" });
int? tokens = result.Usage?.TokenCount;   // null when the provider meters nothing
```

## How It Works

```
Caller ──EmbedAsync────────▶ one batch of one ──┐
        EmbedBatchAsync ──▶ split into batches ─┴─▶ [bounded channel] ──▶ Worker N
  ▲                                                                        │
  │                                                                        ▼
  └── await TCS.Task ◀── TCS.SetResult(EmbeddingResult) ◀── provider.EmbedBatchWithUsageAsync(texts)
```

- Batches are queued in a bounded channel with natural backpressure
- N workers consume from the channel, each owning its own `IEmbeddingProvider` instance
- One queued item is one batch, so a batch is one provider call rather than one per text
- A failing batch fails only that batch; other batches and the workers carry on
- Disposal completes the channel, drains workers, and disposes all providers. Any batch still
  queued at that point is failed with `ObjectDisposedException` rather than left awaiting a worker
  that has stopped
- **Disposal waits for whatever call is already inside a provider, with no timeout.** Observe the
  `CancellationToken` you are handed and disposal is prompt; ignore it and disposal takes as long
  as your slowest round trip. There is deliberately no bound — abandoning the wait would dispose a
  provider with a call still running inside it

## Batching

Batching is per provider, not per caller. A provider answers `SupportsBatching`, which is `false`
unless it says otherwise:

- **`false`** — the texts are sent one per request, spread across `Concurrency` workers. This is
  what a local ONNX model wants: it embeds one text at a time either way, so grouping would only
  move the work into a single worker and run it serially.
- **`true`** — the texts are grouped by the limits below and each group is queued as one unit of
  work, so a hosted API sees one request instead of one per text. This applies to whichever method
  the caller used: `EmbedBatchAsync` and `EmbedBatchWithUsageAsync` take the same path.

Every hosted embedding API accepts an array of inputs and caps both how many it takes and how much
text, so both limits are applied and whichever binds first closes a batch:

```csharp
EmbeddingServiceOptions options = new EmbeddingServiceOptions
{
    MaxTextsPerBatch = 64,          // default
    MaxCharactersPerBatch = 100000  // default
};
```

A single text longer than `MaxCharactersPerBatch` is sent on its own rather than split — deciding
how to shorten a text belongs to the caller, or to `VectorSharp.Chunking` before this point.

Unrelated single `EmbedAsync` calls are never coalesced into a shared batch. A batch is a batch
because the caller asked for one.

## Usage Reporting

`EmbedWithUsageAsync` and `EmbedBatchWithUsageAsync` return an `EmbeddingResult`: the vectors, plus
what the provider reported spending.

```csharp
EmbeddingResult result = await embedder.EmbedBatchWithUsageAsync(chunks);

if (result.Usage != null)
{
    meter.Record(result.Usage.TokenCount, result.Usage.Model);
}
else
{
    // This provider reports nothing. Not the same as a call that cost nothing.
    meter.RecordUnknownSpend();
}
```

`Usage` is null unless the provider actually reported something, and an unreported count is never
defaulted to `0` — a caller billed per token has to be able to tell "not reported" from "free".
When a call is split into several batches, the totals are summed only if every batch reported one;
a total assembled from some of them would understate the spend, so it comes back null instead.

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
    Concurrency = 4,               // Number of concurrent workers (default: 1)
    ChannelCapacity = 500,         // Max pending batches before backpressure (default: 1000)
    MaxTextsPerBatch = 64,         // Max texts in one provider call (default: 64)
    MaxCharactersPerBatch = 100000 // Max characters in one provider call (default: 100000)
};
```

Note: each worker creates its own provider instance via the factory. For ONNX models, this means N copies of the model in memory.

## Implementing a Custom Provider

Only `Dimension`, `EmbedAsync` and `Dispose` have to be implemented. `SupportsBatching` and the
batch methods are default interface implementations — the batch ones loop the single-text method
and the capability answers false — so an existing provider keeps working, and keeps behaving,
untouched:

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

A provider talking to an API that embeds an array in one request should say so and override the
batch methods, which is where the round-trip saving comes from. The service only groups texts for a
provider that advertises the capability, so both parts are needed:

```csharp
public bool SupportsBatching => true;

public async Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts,
    EmbeddingPurpose purpose = EmbeddingPurpose.Document,
    CancellationToken cancellationToken = default)
{
    ApiResponse response = await PostAsync(texts, purpose, cancellationToken);

    return new EmbeddingResult
    {
        Vectors = response.Embeddings,          // one per input text, in the same order
        Usage = response.TokenCount is int used // only when the API actually reported it
            ? new EmbeddingUsage { TokenCount = used, Model = response.Model }
            : null
    };
}

// Override the plain batch method too, so callers that reach for it get one request as well
public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts,
    EmbeddingPurpose purpose = EmbeddingPurpose.Document,
    CancellationToken cancellationToken = default)
{
    EmbeddingResult result = await EmbedBatchWithUsageAsync(texts, purpose, cancellationToken);
    return result.Vectors.ToArray();
}
```

The service matches vectors to texts by position, so a batch must come back with exactly one vector
per input text, in order. A response of a different length fails the request rather than returning
vectors attributed to the wrong text.

## API Reference

### IEmbeddingProvider

```csharp
public interface IEmbeddingProvider : IDisposable
{
    int Dimension { get; }
    Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);

    bool SupportsBatching { get; }   // default false — decides whether the service groups texts

    // Default implementations — override to embed an array in one request
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
    Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
}
```

### EmbeddingResult and EmbeddingUsage

```csharp
public sealed class EmbeddingResult
{
    public required IReadOnlyList<float[]> Vectors { get; init; }
    public EmbeddingUsage? Usage { get; init; }   // null means unknown, never free
}

public sealed class EmbeddingUsage
{
    public required int TokenCount { get; init; }
    public string? Model { get; init; }
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
    public Task<EmbeddingResult> EmbedWithUsageAsync(string text,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
    public Task<EmbeddingResult> EmbedBatchWithUsageAsync(IReadOnlyList<string> texts,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}
```

## License

MIT
