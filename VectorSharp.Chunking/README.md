# VectorSharp.Chunking

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Chunking-blue.svg)](https://www.nuget.org/packages/VectorSharp.Chunking)

Streaming text chunker that splits text into token-bounded chunks suitable for embedding. Zero dependencies.

## Install

```
dotnet add package VectorSharp.Chunking
```

## Features

- **Stream-based** — reads character-by-character, never loads the full file into memory
- **Token-bounded** — chunks respect a configurable token limit via your own token counter
- **Format-aware** — ships with predefined break strings and stop signals for Markdown and C#
- **Round-trip safe** — concatenating all chunks reproduces the original text exactly
- **Stop signals** — headings, code blocks, and other structural elements always start a new chunk
- **Zero dependencies** — pure text processing, no embedding or tokenizer dependency

## Quick Start

```csharp
using VectorSharp.Chunking;

using StreamReader reader = new StreamReader("document.md");
ChunkReader chunker = ChunkReader.Create(reader, text => myTokenizer.CountTokens(text));

await foreach (string chunk in chunker.ReadAllAsync())
{
    // Each chunk is within the token limit and splits at natural boundaries
    float[] embedding = await embedder.EmbedAsync(chunk);
}
```

## Configuration

```csharp
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter, new ChunkReaderOptions
{
    MaxTokensPerChunk = 500,           // default: 300
    BreakStrings = BreakStrings.CSharp, // default: BreakStrings.Markdown
    StopSignals = StopSignals.CSharp    // default: StopSignals.Markdown
});
```

### Token Counting

The `countTokens` parameter is a `Func<string, int>` — you provide whatever token counter matches your embedding model:

```csharp
// With a real tokenizer
ChunkReader.Create(reader, text => bertTokenizer.CountTokens(text));

// Simple word-based approximation
ChunkReader.Create(reader, text => text.Split(' ').Length);
```

## Predefined Formats

### Markdown (default)

Splits at headings, paragraphs, list items, code blocks, and sentence boundaries. Stop signals ensure headings and code blocks always start a new chunk.

### C#

Splits at blank lines, braces, indentation boundaries, and statement endings. Stop signals ensure namespace declarations, class declarations, and XML doc comments start a new chunk.

```csharp
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter, new ChunkReaderOptions
{
    BreakStrings = BreakStrings.CSharp,
    StopSignals = StopSignals.CSharp
});
```

### Custom Formats

Pass your own break strings and stop signals for any text format:

```csharp
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter, new ChunkReaderOptions
{
    BreakStrings = ["\n\n", "\n", ". "],
    StopSignals = ["CHAPTER "]
});
```

## How It Works

```
StreamReader ──▶ SegmentReader ──▶ ChunkReader ──▶ IAsyncEnumerable<string>
                 (break strings)   (token limits,
                                    stop signals)
```

1. **Segment reading** — text is read character-by-character and split at break string boundaries. Longer break strings are matched first (e.g., `\n\n` is preferred over `\n`).

2. **Chunk assembly** — segments are concatenated into chunks until adding the next segment would exceed the token limit. If a segment starts with a stop signal, it forces a new chunk to begin.

## End-to-End with VectorSharp

```csharp
using VectorSharp.Chunking;
using VectorSharp.Storage;
using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

await using EmbeddingService embedder = new EmbeddingService(NomicEmbedProvider.Create);
using CosineVectorStore<int> store = VectorStore.Create<int>("docs", embedder.Dimension);

using StreamReader reader = new StreamReader("document.md");
ChunkReader chunker = ChunkReader.Create(reader, text => myTokenizer.CountTokens(text));

int id = 0;
await foreach (string chunk in chunker.ReadAllAsync())
{
    float[] embedding = await embedder.EmbedAsync(chunk, EmbeddingPurpose.Document);
    await store.AddAsync(id++, embedding);
}
```

## API Reference

### ChunkReader

```csharp
public sealed class ChunkReader
{
    public static ChunkReader Create(StreamReader reader, Func<string, int> countTokens,
        ChunkReaderOptions? options = null);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken = default);
}
```

### ChunkReaderOptions

```csharp
public sealed class ChunkReaderOptions
{
    public int MaxTokensPerChunk { get; init; }   // default: 300
    public string[] BreakStrings { get; init; }   // default: BreakStrings.Markdown
    public string[] StopSignals { get; init; }    // default: StopSignals.Markdown
}
```

### BreakStrings

```csharp
public static class BreakStrings
{
    public static readonly string[] Markdown;  // 16 break strings
    public static readonly string[] CSharp;    // 11 break strings
}
```

### StopSignals

```csharp
public static class StopSignals
{
    public static readonly string[] Markdown;  // 8 stop signals
    public static readonly string[] CSharp;    // 7 stop signals
}
```

## License

MIT
