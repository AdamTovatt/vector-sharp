# VectorSharp.Chunking

[← Back to VectorSharp](../README.md)

[![NuGet](https://img.shields.io/badge/nuget-VectorSharp.Chunking-blue.svg)](https://www.nuget.org/packages/VectorSharp.Chunking)

Streaming text chunker that splits text into token-bounded chunks suitable for embedding. Zero dependencies.

## Install

```
dotnet add package VectorSharp.Chunking
```

## Features

- **Stream-based** — reads character-by-character from any `TextReader`, holding one segment at a time rather than the whole file
- **Token-bounded** — every chunk respects the configured token limit, counted by your own token counter. Text that runs past the limit with no break point in it is cut at the limit rather than emitted whole
- **Position-aware** — `ReadChunksAsync` yields chunks that carry their offset in the source and their token count
- **Format-aware** — ships with predefined break strings and stop signals for Markdown, C#, JavaScript/TypeScript/JSX/TSX, HTML, CSS, Python, and generic plain text
- **Round-trip safe** — concatenating all chunks reproduces the original text exactly
- **Stop signals** — headings, code blocks, and other structural elements always start a new chunk
- **Zero dependencies** — pure text processing, no embedding or tokenizer dependency

> **Memory, as distinct from chunk size.** Input containing none of the configured break strings — minified JavaScript, a base64 blob, a single-line CSV — is one segment, and a segment is accumulated whole before it is cut into chunks. The chunks that come out are within the limit, so nothing over-sized reaches an embedding API, but peak memory is still the size of the longest run of text with no break point in it. Give such input break strings that do occur in it (`","`, `";"`, a fixed run of characters) if that matters.

## Quick Start

```csharp
using VectorSharp.Chunking;

using StreamReader reader = new StreamReader("document.md");
ChunkReader chunker = ChunkReader.Create(reader, text => myTokenizer.CountTokens(text));

await foreach (string chunk in chunker.ReadAllAsync())
{
    // Each chunk is within the token limit, splitting at break strings where it can
    float[] embedding = await embedder.EmbedAsync(chunk);
}
```

Any `TextReader` works, so text already in memory needs no stream wrapper:

```csharp
using StringReader reader = new StringReader(documentText);
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter);
```

## Chunks With Position

`ReadChunksAsync` yields `TextChunk` values instead of bare strings. Each one knows where it came
from, which is what lets a search hit link back to its place in the source document:

```csharp
await foreach (TextChunk chunk in chunker.ReadChunksAsync())
{
    // chunk.StartOffset  — character offset from the start of the input
    // chunk.EndOffset    — one past the last character, the next chunk's StartOffset
    // chunk.TokenCount   — as counted by the token counter you supplied
    float[] embedding = await embedder.EmbedAsync(chunk.Text);
}
```

An offset is the position the chunk's text was read from, not an estimate reconstructed afterwards.
Because the chunks reproduce the input exactly, it is also the sum of the lengths of all preceding
chunks. Offsets are measured in UTF-16 code units, the same unit as `string.Length`, and typed as
`long` because they accumulate over the whole input, which may run past `int.MaxValue` characters.

`ReadAllAsync` yields the same text as `ReadChunksAsync` — it is the string-only view of the same
pass.

A `ChunkReader` is a single forward pass over its reader: it is consumed by enumerating it, and
enumerating the same instance again throws `InvalidOperationException`. Create a new one to chunk
the same input a second time.

## Upgrading from 1.x

The only published 1.x release is 1.0.0, so this is the whole of what changes for every upgrader.
Two behaviours differ; no signature does.

**`MaxTokensPerChunk` is a bound rather than a target.** In 1.0.0, a segment with no break point
inside it was emitted whole however far over the limit it was; it is now cut at the limit. Text
whose segments already fit chunks identically — the cut only reaches text that used to come back
over the limit, which is text a model with a fixed context window would have rejected or silently
truncated. Read the note under [How It Works](#how-it-works) on where a cut lands before relying
on this, because it is not at a word boundary.

**Enumerating a `ChunkReader` twice throws.** In 1.0.0, a second enumeration returned an empty
sequence — which looked like "this document has no chunks" rather than "this reader is spent" —
and after an abandoned pass it silently dropped the segment that pass had consumed but not yet
yielded. Code that enumerated once, which is the only use that ever produced correct output, is
unaffected.

The public surface is otherwise source- and binary-compatible: nothing was removed or changed, and
the `StreamReader` overload of `Create` is still there.

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

Splits at blank lines, braces, and statement endings. Stop signals ensure XML doc comments start a new chunk, which naturally aligns chunks with public API members.

```csharp
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter, new ChunkReaderOptions
{
    BreakStrings = BreakStrings.CSharp,
    StopSignals = StopSignals.CSharp
});
```

### JavaScript / TypeScript / JSX / TSX

Splits at blank lines, braces, and statement endings. Stop signals ensure JSDoc blocks start a new chunk. The same predefined set applies to all four variants since they share the same block and statement syntax.

```csharp
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter, new ChunkReaderOptions
{
    BreakStrings = BreakStrings.JavaScript,
    StopSignals = StopSignals.JavaScript
});
```

### HTML

Splits at paragraph, line, and sentence boundaries. Stop signals ensure `<h1>`, `<h2>`, and `<h3>` tags start a new chunk, aligning chunks with document sections.

### CSS

Splits at rule boundaries (closing brace on its own line), blank lines, and statement endings. Stop signals ensure `@media`, `@keyframes`, `@import`, and `@supports` at-rules start a new chunk.

### Python

Python is whitespace-significant, so break strings are limited to paragraph, line, and sentence boundaries. Stop signals carry the structural load: `def`, `async def`, and `class` force a new chunk, which aligns chunks with function and class boundaries (including indented methods).

### Plain Text

A generic fallback with paragraph, line, and sentence break strings and no stop signals. Suitable for any text-like format without language-specific structure.

```csharp
ChunkReader chunker = ChunkReader.Create(reader, myTokenCounter, new ChunkReaderOptions
{
    BreakStrings = BreakStrings.PlainText,
    StopSignals = StopSignals.PlainText
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
TextReader ──▶ SegmentReader ──▶ ChunkReader ──▶ IAsyncEnumerable<TextChunk>
               (break strings)   (token limits,     or IAsyncEnumerable<string>
                                  stop signals,
                                  cutting)
```

1. **Segment reading** — text is read character-by-character and split at break string boundaries. Longer break strings are matched first (e.g., `\n\n` is preferred over `\n`).

2. **Chunk assembly** — segments are concatenated into chunks until adding the next segment would exceed the token limit. If a segment starts with a stop signal, it forces a new chunk to begin.

3. **Cutting** — a segment that is over the limit on its own is cut into consecutive pieces that are not. The cut is found by measuring candidate prefixes with your token counter, so it lands where your tokenizer says the limit falls rather than at a guessed character count. The pieces still concatenate back into the original text, and each carries its own offset, so round-tripping and `StartOffset` hold across a cut.

> **A cut lands at the token limit, not at anything you would call a boundary.** It is reached only by text that had no break point in it for long enough to run past the limit, and at that point there is nothing structural left to cut at. With a character- or subword-based counter — which is what real embedding models use — that means cutting mid-word: at a limit of 3 tokens, `"the quick brown fox jumps"` comes back as `"the quick br"`, `"own fox jump"`, `"s"`. A word-counting tokenizer never does this, because adding a character to a word does not raise its count, so a word counter is a poor model of what your provider will actually do.
>
> The one thing a cut will not do is divide a single character: it never falls between the two halves of a surrogate pair. It can still separate a base character from a combining mark that follows it. If you need cuts on grapheme or word boundaries, give the chunker break strings that occur in your text so the limit is never the thing that decides.

## End-to-End with VectorSharp

```csharp
using VectorSharp.Chunking;
using VectorSharp.Storage;
using VectorSharp.Embedding;
using VectorSharp.Embedding.NomicEmbed;

await using EmbeddingService embedder = new EmbeddingService(NomicEmbedProvider.Create);
using CosineVectorStore<long> store = VectorStore.Create<long>("docs", embedder.Dimension);

using StreamReader reader = new StreamReader("document.md");
ChunkReader chunker = ChunkReader.Create(reader, text => myTokenizer.CountTokens(text));

// Collect first, so the whole document goes to the embedder in one call rather than one call
// per chunk. Against a remote embedding API that is the difference between one HTTP round trip
// and one per chunk; the service splits the texts into provider-sized batches itself.
List<TextChunk> chunks = new List<TextChunk>();
await foreach (TextChunk chunk in chunker.ReadChunksAsync())
{
    chunks.Add(chunk);
}

float[][] embeddings = await embedder.EmbedBatchAsync(
    chunks.Select(chunk => chunk.Text).ToList(),
    EmbeddingPurpose.Document);

for (int i = 0; i < chunks.Count; i++)
{
    // Keying on the offset rather than a running counter is what StartOffset is for: it points
    // back into the source document, so a hit can be quoted or linked in context later.
    await store.AddAsync(chunks[i].StartOffset, embeddings[i]);
}
```

`AddAsync` is keyed by the store's key type — `VectorStore.Create<long>` here, to match `StartOffset`. Use whatever key your own records are addressed by; the point is that the chunk carries enough to build one.

If the document is large enough that holding every chunk in memory matters, batch as you go: fill a `List<TextChunk>` up to a few hundred entries, embed and store that, then clear it. What to avoid is a single `EmbedAsync` per chunk.

## API Reference

### ChunkReader

```csharp
public sealed class ChunkReader
{
    public static ChunkReader Create(TextReader reader, Func<string, int> countTokens,
        ChunkReaderOptions? options = null);
    public static ChunkReader Create(StreamReader reader, Func<string, int> countTokens,
        ChunkReaderOptions? options = null);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken = default);
    public IAsyncEnumerable<TextChunk> ReadChunksAsync(CancellationToken cancellationToken = default);
}
```

The `StreamReader` overload does nothing the `TextReader` one does not. It stays because
chunking-v1.0.0 shipped `Create` taking a `StreamReader`, and assemblies compiled against that
version bind to that exact signature at run time.

### TextChunk

```csharp
public sealed class TextChunk
{
    public required string Text { get; init; }
    public required long StartOffset { get; init; }   // characters from the start of the input
    public required int TokenCount { get; init; }
    public long EndOffset { get; }                    // StartOffset + Text.Length
}
```

### ChunkReaderOptions

```csharp
public sealed class ChunkReaderOptions
{
    public int MaxTokensPerChunk { get; init; }                  // default: 300
    public IReadOnlyList<string> BreakStrings { get; init; }     // default: BreakStrings.Markdown
    public IReadOnlyList<string> StopSignals { get; init; }      // default: StopSignals.Markdown
}
```

### BreakStrings

```csharp
public static class BreakStrings
{
    public static readonly IReadOnlyList<string> Markdown;    // 16 entries
    public static readonly IReadOnlyList<string> CSharp;      // 5 entries
    public static readonly IReadOnlyList<string> JavaScript;  // 5 entries
    public static readonly IReadOnlyList<string> Html;        // 3 entries
    public static readonly IReadOnlyList<string> Css;         // 4 entries
    public static readonly IReadOnlyList<string> Python;      // 3 entries
    public static readonly IReadOnlyList<string> PlainText;   // 5 entries
}
```

### StopSignals

```csharp
public static class StopSignals
{
    public static readonly IReadOnlyList<string> Markdown;    // 8 entries
    public static readonly IReadOnlyList<string> CSharp;      // 1 entry
    public static readonly IReadOnlyList<string> JavaScript;  // 1 entry
    public static readonly IReadOnlyList<string> Html;        // 3 entries
    public static readonly IReadOnlyList<string> Css;         // 4 entries
    public static readonly IReadOnlyList<string> Python;      // 3 entries
    public static readonly IReadOnlyList<string> PlainText;   // 0 entries
}
```

## License

MIT
