# section-04-embed-async — Embeddings via ISidecarClient.EmbedAsync

## Goal

Add a batched, order-stable, failure-isolated embeddings capability to the existing sidecar abstraction. This is the only place in the codebase that calls OpenRouter's `/api/v1/embeddings` endpoint. Everything downstream (pillar vectors, idea vectors, brand-fit pre-filter, dedup) depends on this returning **one vector per input, in input order, never zero/NaN**.

Concretely you will:
1. Add `EmbedAsync(...)` to the `ISidecarClient` interface.
2. Implement it in `OpenRouterClient` (POST `/api/v1/embeddings`, OpenAI-compatible).
3. Sanitize/skip empty inputs, batch in chunks of `BatchSize`, sort `data[]` by `index`, isolate per-batch failures.
4. Default the model from `EmbeddingOptions`.

This section does **not** implement `CosineSimilarity` or `EmbeddingOptions` — those are owned by **section-01-foundation** (dependency, see below). This section also does **not** call `EmbedAsync` from any service — that is sections 06/07.

## Dependencies

- **section-01-foundation** (REQUIRED, must be complete before this compiles):
  - `EmbeddingOptions` class — `PBA.Infrastructure/Configuration/EmbeddingOptions.cs`, with `Model` (default `"openai/text-embedding-3-small"`), `Dimensions` (default `1536`), `BatchSize` (default `128`). Bound from the `"Embedding"` appsettings section and registered for `IOptions<EmbeddingOptions>` / `IOptionsMonitor<EmbeddingOptions>`.
  - **R-M4 gate** already cleared in section-01: `GET /api/v1/embeddings/models` confirmed `openai/text-embedding-3-small` is proxied at native dim **1536**. You do not re-run that gate here; you rely on it and pass `dimensions: 1536` in every request body (**R-M3**) so the model and the `vector(1536)` column stay coupled.
  - `CosineSimilarity(float[], float[])` static util — not used in this section, listed only so you don't accidentally re-create it.

If section-01 is not yet merged, stub `EmbeddingOptions` locally with the three properties above so this section can be built and tested in isolation, then delete the stub once section-01 lands.

## Background / context the implementer needs

- Backend is .NET 10, Clean Architecture: `PBA.Application` (interfaces), `PBA.Infrastructure` (implementations).
- The sidecar abstraction is the **mandatory** path for all LLM/embedding calls — never call Anthropic/OpenAI directly, always go through `ISidecarClient` → `OpenRouterClient`.
- OpenRouter exposes an **OpenAI-compatible embeddings endpoint**. Request/response shape:
  - Request body: `{ "model": "...", "input": ["a","b",...], "encoding_format": "float", "dimensions": 1536 }`.
  - Response body: `{ "data": [ { "index": 0, "embedding": [..floats..] }, ... ] }`. **The API does not guarantee `data[]` is in request order** — each element carries its own `index` and you must sort by it.
- The existing `OpenRouterClient` (`src/PBA.Infrastructure/Services/OpenRouterClient.cs`) already has the relevant plumbing you will reuse:
  - Primary constructor: `(HttpClient httpClient, IOptions<OpenRouterOptions> options, ILogger<OpenRouterClient> logger)`. You will **add** `IOptions<EmbeddingOptions>` (or `IOptionsMonitor<EmbeddingOptions>`) to this constructor — keep the existing params.
  - `private static readonly JsonSerializerOptions JsonOptions` — `SnakeCaseLower` naming, ignore-null. Reuse it for serialize/deserialize so `encoding_format` / `dimensions` map correctly without per-property attributes (or add `[JsonPropertyName]` attributes as the existing `ChatRequest` records do — pick one and be consistent within the new request/response records).
  - `_options.ApiKey`, `_options.BaseUrl`, `_options.TimeoutMs` from `OpenRouterOptions`. The base URL already points at the OpenRouter root used for `{BaseUrl}/chat/completions`; the embeddings path is `{BaseUrl}/embeddings` (the `/api/v1` prefix is part of `BaseUrl` — verify against the configured value; do not double up the prefix).
  - Auth header pattern: `Authorization: Bearer {ApiKey}`, plus `HTTP-Referer` and `X-Title` headers (mirror `SendPromptAsync`).
  - Timeout pattern: `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `cts.CancelAfter(_options.TimeoutMs)`, catching `OperationCanceledException when (!ct.IsCancellationRequested)` → `TimeoutException`.

## Tests FIRST (write before implementing)

New test file: `tests/PBA.Infrastructure.Tests/Services/OpenRouterClientEmbedTests.cs` (match the existing test project layout — confirm the actual path with the other `OpenRouterClient`/sidecar tests and place it alongside them).

Use a **fake `HttpMessageHandler`** (or Moq'd handler) so you can assert the outgoing request shape and feed canned `data[]` responses. Construct the client with `Options.Create(new OpenRouterOptions { ApiKey="test", BaseUrl="https://openrouter.ai/api/v1", TimeoutMs=30000 })`, `Options.Create(new EmbeddingOptions { Model="openai/text-embedding-3-small", Dimensions=1536, BatchSize=128 })`, and `NullLogger<OpenRouterClient>.Instance`.

xUnit, Arrange-Act-Assert, naming `Method_Scenario_ExpectedResult`. From `claude-plan-tdd.md` §6:

```
# Test: posts to /api/v1/embeddings with { model, input[], encoding_format:"float", dimensions:1536 } (R-M3)
# Test: returns one vector per input in INPUT order even when API returns data[] out of order (sorts by index)
# Test: batches inputs in chunks of BatchSize (128) — N>128 produces multiple requests
# Test: model defaults from EmbeddingOptions when model arg null
# Test: empty / whitespace inputs are skipped or sanitized, not sent (R-H2)
# Test: a failing batch throws/propagates without corrupting other batches' order
```

Test-by-test expectations:

- **EmbedAsync_Request_PostsToEmbeddingsEndpointWithExpectedBody** — capture the outgoing `HttpRequestMessage`; assert method POST, URL ends `/embeddings`, and the JSON body contains `model`, an `input` array, `encoding_format: "float"`, and `dimensions: 1536` (**R-M3**). Deserialize the captured body rather than substring-matching where practical.
- **EmbedAsync_ApiReturnsDataOutOfOrder_ResultsInInputOrder** — feed a response whose `data[]` elements have `index` values shuffled (e.g. `[{index:2},{index:0},{index:1}]`); assert the returned `IReadOnlyList<float[]>` is ordered to match the *input* order. This is the load-bearing correctness test for everything downstream.
- **EmbedAsync_MoreThanBatchSizeInputs_IssuesMultipleRequests** — set `BatchSize=128`, pass e.g. 200 inputs, assert exactly 2 outgoing requests (sizes 128 + 72) and that the concatenated result preserves global input order across batch boundaries.
- **EmbedAsync_NullModelArg_UsesEmbeddingOptionsModel** — call with `model: null`; assert the request body `model` equals `EmbeddingOptions.Model`. Then call with an explicit override and assert it wins.
- **EmbedAsync_EmptyOrWhitespaceInputs_AreSkipped** (**R-H2**) — pass inputs containing `""` / `"   "` interleaved with real strings. **Recommended contract: filter empties out of the request entirely and return only vectors for the non-empty inputs, in their original relative order** — empties are never sent to the API. Document which positions were dropped if a caller needs alignment; callers in sections 06/07 only embed non-empty `title + " " + description`, so the simpler "drop empties" contract is acceptable. Assert no empty string appears in any outgoing request body.
- **EmbedAsync_OneBatchFails_DoesNotCorruptOtherBatchOrder** (**R-H2**) — make the handler throw / return non-200 for the *second* batch. Pin the contract: **the exception propagates** (the caller — the embedding service in section 06 — wraps the call in try/catch per batch at the service level and leaves those ideas `Embedding == null` for retry). Assert that a failing batch surfaces as a thrown exception and that any already-completed batch's results were not silently reordered or mangled. (Per-batch try/catch swallowing happens in the *service*, section 06 — not here. `EmbedAsync` itself surfaces failures.) Confirm this division of responsibility against section-06's `IdeaEmbeddingServiceTests`.
- **EmbedAsync_NeverReturnsZeroOrNaNVector** (R-H2 corollary) — if the API ever returns an all-zero or NaN-containing embedding, the result must not be persisted downstream. **Recommended: guard here** — if any returned embedding is all-zero or contains NaN/Infinity, throw so the item stays unembedded for retry. Add a test asserting that behavior. Keep this consistent with section-06's "never persists a zero/NaN vector" test — do not duplicate the guard in both with conflicting behavior; pick this layer and reference it from section 06.

## Implementation

### 1. Interface — `src/PBA.Application/Common/Interfaces/ISidecarClient.cs`

Add the method (signature is binding — sections 06/07 call it exactly this way):

```csharp
/// <summary>
/// Returns one embedding vector per input, in input order. Empty/whitespace inputs are skipped
/// (never sent to the API). Batches internally in chunks of EmbeddingOptions.BatchSize. Passes
/// dimensions=1536 so the model and the vector(1536) column stay coupled (R-M3). Never returns a
/// zero or NaN vector — a corrupt embedding from the API surfaces as an exception so the caller
/// can leave the item unembedded for retry (R-H2).
/// </summary>
/// <param name="inputs">Texts to embed (e.g. title + " " + description, or pillar descriptions).</param>
/// <param name="model">Optional model override; defaults to EmbeddingOptions.Model when null.</param>
Task<IReadOnlyList<float[]>> EmbedAsync(
    IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default);
```

Note: CLI-based / streaming sidecar implementations (if any others implement `ISidecarClient`) must also implement this. Search for other implementers (`: ISidecarClient`) and add a method that throws `NotSupportedException` for any non-OpenRouter implementation that cannot embed, mirroring how `SendPromptAsync`'s `model` override is documented as ignored by CLI clients. Only `OpenRouterClient` gets a real implementation.

### 2. Implementation — `src/PBA.Infrastructure/Services/OpenRouterClient.cs`

- Extend the primary constructor to accept the embedding options:
  `OpenRouterClient(HttpClient httpClient, IOptions<OpenRouterOptions> options, IOptions<EmbeddingOptions> embeddingOptions, ILogger<OpenRouterClient> logger)`. Store `_embeddingOptions = embeddingOptions.Value`.
- Implement `EmbedAsync`:
  1. Guard `ApiKey` (mirror `SendPromptAsync`: throw `InvalidOperationException` if blank).
  2. Filter/sanitize: drop `string.IsNullOrWhiteSpace` inputs. If the filtered list is empty, return an empty list without any HTTP call.
  3. Resolve `model = model ?? _embeddingOptions.Model`.
  4. Chunk the filtered inputs into batches of `_embeddingOptions.BatchSize` (`Chunk(...)` LINQ or a simple loop).
  5. For each batch: build `EmbeddingRequest(model, batch, "float", _embeddingOptions.Dimensions)`, POST to `{_options.BaseUrl}/embeddings` with the same auth/referer/title headers and the linked-CTS timeout pattern as `SendPromptAsync`. On non-success, log (truncated body, reuse the existing `Truncate` helper) and throw `InvalidOperationException` — do **not** swallow.
  6. Deserialize into `EmbeddingResponse`, **sort `data` by `index`**, project to `float[]` (`embedding`).
  7. Validate each vector: not null, length `== _embeddingOptions.Dimensions`, no NaN/Infinity, not all-zero. Throw `InvalidOperationException` on violation (R-H2 guard).
  8. Accumulate batch results in order; return the concatenated `IReadOnlyList<float[]>`.
- Add private request/response records next to the existing `ChatRequest`/`ChatResponse`:

```csharp
private sealed record EmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
    [property: JsonPropertyName("encoding_format")] string EncodingFormat,
    [property: JsonPropertyName("dimensions")] int Dimensions);

private sealed record EmbeddingResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData>? Data);

private sealed record EmbeddingData(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("embedding")] float[]? Embedding);
```

Reuse the existing `JsonOptions`, `Truncate`, and the timeout/cancellation handling already in the file — do not duplicate that logic; extract a small private `PostJsonAsync` helper only if it cleanly serves both `SendPromptAsync` and `EmbedAsync` (YAGNI — only refactor if it reduces duplication without contortion).

### 3. DI / registration

`OpenRouterClient` is already registered (find it in `PBA.Infrastructure`'s `DependencyInjection.cs`). Adding `IOptions<EmbeddingOptions>` to the constructor requires that `EmbeddingOptions` is bound and registered — that registration is section-01's responsibility. Verify the binding exists; if you stubbed `EmbeddingOptions` locally pending section-01, also stub a `services.Configure<EmbeddingOptions>(config.GetSection("Embedding"))` call so the client resolves in tests/runtime, and remove the stub when section-01 merges.

## Files touched

| File | Change |
|------|--------|
| `src/PBA.Application/Common/Interfaces/ISidecarClient.cs` | Add `EmbedAsync` signature + XML doc |
| `src/PBA.Infrastructure/Services/OpenRouterClient.cs` | Add `IOptions<EmbeddingOptions>` ctor param, `EmbedAsync` impl, embedding request/response records |
| Any other `ISidecarClient` implementer | Add `EmbedAsync` throwing `NotSupportedException` |
| `tests/PBA.Infrastructure.Tests/Services/OpenRouterClientEmbedTests.cs` (new) | The tests above |
| `PBA.Infrastructure` `DependencyInjection.cs` | (only if section-01 not yet merged) temporary `EmbeddingOptions` binding stub |

## Verification

- `dotnet build` then `dotnet test --filter OpenRouterClientEmbedTests` — all green.
- Confirm the order-by-index test fails if you remove the sort (sanity that the test is load-bearing).
- 80% coverage on the new `EmbedAsync` method.

## Out of scope for this section

- `CosineSimilarity` and `EmbeddingOptions` definitions (section-01).
- Calling `EmbedAsync` from any background service, persisting vectors, per-batch retry/`Embedding == null` handling (section-06 `IdeaEmbeddingService`, section-07 scoring sweep).
- The `vector(1536)` EF column/migration (sections 02, 03).
- Live integration call to OpenRouter — tests use a fake handler only.
