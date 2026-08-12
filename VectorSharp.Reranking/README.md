# VectorSharp.Reranking

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Reranking-blue.svg)](https://www.nuget.org/packages/VectorSharp.Reranking)

Reranking abstractions for the second stage of retrieval. A vector or hybrid search produces
candidates cheaply; a reranker scores them properly. Zero dependencies.

## Install

```
dotnet add package VectorSharp.Reranking
```

This package contains the abstraction and its result types. Implement `IRerankProvider` against
whichever reranker you use — a hosted API or a local model.

## Features

- **Index-based results** — matches say *which* candidate they scored, so you keep your own metadata
- **Usage reporting** — a provider that meters tokens reports them back; one that does not reports nothing at all, rather than a zero you could mistake for a free call
- **No machinery** — one interface and three result types, with no service, queue or worker pool
- **Zero dependencies** — not even on the sibling VectorSharp packages

## Usage

The candidate side of this example comes from `VectorSharp.Storage`; this package only takes the
list of strings and gives back positions into it.

```csharp
using VectorSharp.Reranking;
using VectorSharp.Storage;   // only for the search that produces the candidates

using IRerankProvider reranker = new MyRerankProvider();

// Candidates from a vector search, and whatever you know about each of them
IReadOnlyList<SearchResult<int>> candidates = await store.FindMostSimilarAsync(queryVector, count: 50);
string[] documents = candidates.Select(candidate => textById[candidate.Id]).ToArray();

RerankResult result = await reranker.RerankAsync("how do I sort a list", documents, topN: 5);

foreach (RerankMatch match in result.Matches)
{
    // match.Index is a position in `documents`, so it maps straight back to your own records
    int id = candidates[match.Index].Id;
    Console.WriteLine($"{id} scored {match.Score}");
}
```

## Why Indexes, Not Text

A reranker is given text and returns a judgement about it. What the caller actually needs back is
the *record* behind that text — a row id, a chunk offset, a file path — and only the caller has
that. Returning the document text would force a lookup by content, which is slower and ambiguous
the moment two candidates read the same. An index into the list you passed in is unambiguous and
free to resolve.

Matches come back ordered by score descending, and there are at most `topN` of them — fewer when
you supplied fewer documents.

Because the results are positions, the list you pass in has to hold still: don't modify it until
the call has returned and you have resolved the indexes. A collection mutated mid-call invalidates
every index without anything failing — the count still matches and the matches simply point at
different documents.

Scores are comparable only within a single call. Providers differ in range and scale, so a score
means nothing next to one from another query or another model.

## Usage Reporting

`RerankResult.Usage` carries what the provider reported spending, and is null when it reported
nothing:

```csharp
RerankResult result = await reranker.RerankAsync(query, documents, topN: 5);

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

An unreported count is never defaulted to `0`, because a caller billed per token has to be able to
tell "not reported" from "free".

## Implementing a Custom Provider

An instance owns its own resources and is not required to be thread-safe — this package has no
service serializing access to it, so a provider registered as a singleton has to be safe for
concurrent calls on its own, or be registered per scope.

```csharp
public sealed class HttpRerankProvider : IRerankProvider
{
    private readonly HttpClient _client;
    private readonly string _endpoint;

    public HttpRerankProvider(string endpoint)
    {
        _endpoint = endpoint;
        _client = new HttpClient();
    }

    public async Task<RerankResult> RerankAsync(string query, IReadOnlyList<string> documents,
        int topN, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);

        if (documents.Count == 0)
            return new RerankResult { Matches = [] };

        ApiResponse response = await PostAsync(query, documents, topN, cancellationToken);

        return new RerankResult
        {
            Matches = response.Results
                .Select(result => new RerankMatch { Index = result.Index, Score = result.Score })
                .ToArray(),
            Usage = response.TokenCount is int used      // only when the API actually reported it
                ? new RerankUsage { TokenCount = used, Model = response.Model }
                : null
        };
    }

    public void Dispose() => _client.Dispose();
}
```

The provider owns the `HttpClient` it created here, which is why it disposes it. A provider handed
a client from `IHttpClientFactory` or a typed-client registration must not — that handler belongs
to the container.

An empty candidate list is not an error — the answer is no matches. Reserve exceptions for a null
query, a null list, a null element inside it, or a `topN` below 1, so that an empty result never
doubles as a failure signal.

If the API behind a provider caps how many candidates it accepts, refuse a longer list rather than
truncating it. Truncation leaves every returned index valid while candidates vanish, which is the
one failure the caller has no way to notice.

## Where It Fits

```
query ──▶ embed ──▶ vector search ──▶ candidates ──▶ rerank ──▶ top N
```

- **embed** — [VectorSharp.Embedding](../VectorSharp.Embedding/README.md)
- **vector search** — [VectorSharp.Storage](../VectorSharp.Storage/README.md)
- **rerank** — this package

Reranking is one call per user query against a candidate set already in hand, so this package has
no service, queue or worker pool. The concurrency machinery that earns its place in
[VectorSharp.Embedding](../VectorSharp.Embedding/README.md), where a document can produce hundreds
of chunks, has nothing to do here.

## API Reference

### IRerankProvider

```csharp
public interface IRerankProvider : IDisposable
{
    Task<RerankResult> RerankAsync(string query, IReadOnlyList<string> documents, int topN,
        CancellationToken cancellationToken = default);
}
```

Instances own their own resources and are not required to be thread-safe.

### RerankResult, RerankMatch and RerankUsage

```csharp
public sealed class RerankResult
{
    public required IReadOnlyList<RerankMatch> Matches { get; init; }  // best first, at most topN
    public RerankUsage? Usage { get; init; }                           // null means unknown, never free
}

public sealed class RerankMatch
{
    public required int Index { get; init; }    // position in the documents you passed in
    public required float Score { get; init; }  // higher is more relevant
}

public sealed class RerankUsage
{
    public required int TokenCount { get; init; }
    public string? Model { get; init; }
}
```

## License

MIT
