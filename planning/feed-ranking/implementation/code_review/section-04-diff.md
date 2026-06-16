diff --git a/src/PBA.Application/Common/Interfaces/ISidecarClient.cs b/src/PBA.Application/Common/Interfaces/ISidecarClient.cs
index 28a62ee..d43f084 100644
--- a/src/PBA.Application/Common/Interfaces/ISidecarClient.cs
+++ b/src/PBA.Application/Common/Interfaces/ISidecarClient.cs
@@ -12,6 +12,18 @@ public interface ISidecarClient
     /// <param name="ct">Cancellation token.</param>
     Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);
 
+    /// <summary>
+    /// Returns one embedding vector per input, in input order. Empty/whitespace inputs are skipped
+    /// (never sent to the API), so the result length may be less than the input length — callers should
+    /// pass only non-empty inputs when positional alignment matters. Batches internally in chunks of
+    /// EmbeddingOptions.BatchSize and passes dimensions=1536 (R-M3). Never returns a zero or NaN vector:
+    /// a corrupt embedding from the API surfaces as an exception so the caller can leave the item
+    /// unembedded for retry (R-H2). Honored by <see cref="OpenRouterClient"/>; CLI-based implementations
+    /// throw <see cref="NotSupportedException"/>.
+    /// </summary>
+    Task<IReadOnlyList<float[]>> EmbedAsync(
+        IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default);
+
     IAsyncEnumerable<string> StreamPromptAsync(
         Guid contentId,
         string systemPrompt,
diff --git a/src/PBA.Infrastructure/Services/OpenRouterClient.cs b/src/PBA.Infrastructure/Services/OpenRouterClient.cs
index ae8c4f1..a27bc92 100644
--- a/src/PBA.Infrastructure/Services/OpenRouterClient.cs
+++ b/src/PBA.Infrastructure/Services/OpenRouterClient.cs
@@ -16,9 +16,11 @@ namespace PBA.Infrastructure.Services;
 public sealed class OpenRouterClient(
     HttpClient httpClient,
     IOptions<OpenRouterOptions> options,
+    IOptions<EmbeddingOptions> embeddingOptions,
     ILogger<OpenRouterClient> logger) : ISidecarClient
 {
     private readonly OpenRouterOptions _options = options.Value;
+    private readonly EmbeddingOptions _embeddingOptions = embeddingOptions.Value;
 
     private static readonly JsonSerializerOptions JsonOptions = new()
     {
@@ -74,6 +76,92 @@ public sealed class OpenRouterClient(
         return content.Trim();
     }
 
+    public async Task<IReadOnlyList<float[]>> EmbedAsync(
+        IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default)
+    {
+        if (string.IsNullOrWhiteSpace(_options.ApiKey))
+            throw new InvalidOperationException("OpenRouter API key is not configured.");
+
+        // Drop empty/whitespace inputs — a meaningless vector would corrupt dedup/pre-filter (R-H2).
+        var sanitized = inputs.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
+        if (sanitized.Count == 0)
+            return [];
+
+        var resolvedModel = model ?? _embeddingOptions.Model;
+        var results = new List<float[]>(sanitized.Count);
+
+        foreach (var batch in sanitized.Chunk(_embeddingOptions.BatchSize))
+        {
+            results.AddRange(await EmbedBatchAsync(batch, resolvedModel, ct));
+        }
+
+        return results;
+    }
+
+    private async Task<float[][]> EmbedBatchAsync(string[] batch, string model, CancellationToken ct)
+    {
+        var payload = new EmbeddingRequest(model, batch, "float", _embeddingOptions.Dimensions);
+
+        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/embeddings")
+        {
+            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
+        };
+        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
+        request.Headers.Add("HTTP-Referer", "https://matthewkruczek.ai");
+        request.Headers.Add("X-Title", "Personal Brand Assistant");
+
+        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
+        cts.CancelAfter(_options.TimeoutMs);
+
+        HttpResponseMessage response;
+        try
+        {
+            response = await httpClient.SendAsync(request, cts.Token);
+        }
+        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
+        {
+            throw new TimeoutException($"OpenRouter embeddings request timed out after {_options.TimeoutMs}ms");
+        }
+
+        var body = await response.Content.ReadAsStringAsync(ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            logger.LogError("OpenRouter embeddings request failed: {Status} {Body}", response.StatusCode, Truncate(body));
+            throw new InvalidOperationException($"OpenRouter embeddings request failed ({(int)response.StatusCode})");
+        }
+
+        var data = JsonSerializer.Deserialize<EmbeddingResponse>(body, JsonOptions)?.Data;
+        if (data is null || data.Count != batch.Length)
+            throw new InvalidOperationException(
+                $"OpenRouter embeddings returned {data?.Count ?? 0} vectors for {batch.Length} inputs.");
+
+        // The API does not guarantee data[] is in request order — sort by index.
+        return [.. data.OrderBy(d => d.Index).Select(d => ValidateVector(d.Embedding))];
+    }
+
+    // Reject zero/NaN/Infinity vectors so a corrupt embedding never enters the system (R-H2);
+    // the item stays unembedded and is retried on the next sweep.
+    private float[] ValidateVector(float[]? vector)
+    {
+        if (vector is null || vector.Length != _embeddingOptions.Dimensions)
+            throw new InvalidOperationException(
+                $"OpenRouter embedding has length {vector?.Length ?? 0}, expected {_embeddingOptions.Dimensions}.");
+
+        var allZero = true;
+        foreach (var v in vector)
+        {
+            if (!float.IsFinite(v))
+                throw new InvalidOperationException("OpenRouter embedding contains a NaN/Infinity value.");
+            if (v != 0f) allZero = false;
+        }
+
+        if (allZero)
+            throw new InvalidOperationException("OpenRouter embedding is a zero vector.");
+
+        return vector;
+    }
+
     // Non-incremental streaming: yields the full completion once. The UI gets the
     // text in a single chunk. Token-by-token SSE streaming is a future enhancement.
     public async IAsyncEnumerable<string> StreamPromptAsync(
@@ -102,4 +190,17 @@ public sealed class OpenRouterClient(
 
     private sealed record ChatChoice(
         [property: JsonPropertyName("message")] ChatMessage? Message);
+
+    private sealed record EmbeddingRequest(
+        [property: JsonPropertyName("model")] string Model,
+        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
+        [property: JsonPropertyName("encoding_format")] string EncodingFormat,
+        [property: JsonPropertyName("dimensions")] int Dimensions);
+
+    private sealed record EmbeddingResponse(
+        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData>? Data);
+
+    private sealed record EmbeddingData(
+        [property: JsonPropertyName("index")] int Index,
+        [property: JsonPropertyName("embedding")] float[]? Embedding);
 }
diff --git a/src/PBA.Infrastructure/Services/SidecarClient.cs b/src/PBA.Infrastructure/Services/SidecarClient.cs
index 636b698..f3fe348 100644
--- a/src/PBA.Infrastructure/Services/SidecarClient.cs
+++ b/src/PBA.Infrastructure/Services/SidecarClient.cs
@@ -60,6 +60,11 @@ public class SidecarClient : ISidecarClient, IDisposable
         }
     }
 
+    // The CLI sidecar cannot produce embeddings; embeddings route through OpenRouterClient.
+    public Task<IReadOnlyList<float[]>> EmbedAsync(
+        IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default)
+        => throw new NotSupportedException("SidecarClient (CLI) does not support embeddings; use OpenRouterClient.");
+
     public async IAsyncEnumerable<string> StreamPromptAsync(
         Guid contentId,
         string systemPrompt,
diff --git a/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientEmbedTests.cs b/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientEmbedTests.cs
new file mode 100644
index 0000000..3a8a42d
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientEmbedTests.cs
@@ -0,0 +1,178 @@
+using System.Net;
+using System.Text;
+using System.Text.Json;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Services;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services;
+
+public class OpenRouterClientEmbedTests
+{
+    // Fake handler: records every request body and returns a scripted embeddings response per call.
+    private sealed class ScriptedHandler(Func<int, string[], HttpResponseMessage> respond) : HttpMessageHandler
+    {
+        public List<string> CapturedBodies { get; } = [];
+        private int _call;
+
+        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
+        {
+            var body = await request.Content!.ReadAsStringAsync(ct);
+            CapturedBodies.Add(body);
+            var input = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("input")
+                .EnumerateArray().Select(e => e.GetString()!).ToArray();
+            return respond(_call++, input);
+        }
+    }
+
+    private static HttpResponseMessage EmbeddingsOk(IEnumerable<(int index, float[] embedding)> data)
+    {
+        var payload = new { data = data.Select(d => new { index = d.index, embedding = d.embedding }) };
+        return new HttpResponseMessage(HttpStatusCode.OK)
+        {
+            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
+        };
+    }
+
+    private static float[] Vec(float seed, int dims = 1536)
+    {
+        var v = new float[dims];
+        for (var i = 0; i < dims; i++) v[i] = seed + i * 0.0001f;
+        return v;
+    }
+
+    private static OpenRouterClient Client(ScriptedHandler handler, EmbeddingOptions? embed = null) =>
+        new(new HttpClient(handler),
+            Options.Create(new OpenRouterOptions { ApiKey = "test-key", BaseUrl = "https://openrouter.ai/api/v1", TimeoutMs = 30000 }),
+            Options.Create(embed ?? new EmbeddingOptions()),
+            NullLogger<OpenRouterClient>.Instance);
+
+    [Fact]
+    public async Task EmbedAsync_PostsToEmbeddingsEndpoint_WithExpectedBody()
+    {
+        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
+        var client = Client(handler);
+
+        await client.EmbedAsync(["hello"]);
+
+        using var doc = JsonDocument.Parse(handler.CapturedBodies.Single());
+        var root = doc.RootElement;
+        Assert.Equal("openai/text-embedding-3-small", root.GetProperty("model").GetString());
+        Assert.Equal("float", root.GetProperty("encoding_format").GetString());
+        Assert.Equal(1536, root.GetProperty("dimensions").GetInt32());
+        Assert.Single(root.GetProperty("input").EnumerateArray());
+    }
+
+    [Fact]
+    public async Task EmbedAsync_ApiReturnsDataOutOfOrder_ResultsInInputOrder()
+    {
+        // Return data[] shuffled; index 0,1,2 carry distinguishable first elements.
+        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(
+        [
+            (2, Vec(20)), (0, Vec(0)), (1, Vec(10)),
+        ]));
+        var client = Client(handler);
+
+        var result = await client.EmbedAsync(["a", "b", "c"]);
+
+        Assert.Equal(3, result.Count);
+        Assert.Equal(0f, result[0][0]);
+        Assert.Equal(10f, result[1][0]);
+        Assert.Equal(20f, result[2][0]);
+    }
+
+    [Fact]
+    public async Task EmbedAsync_MoreThanBatchSize_IssuesMultipleRequestsPreservingOrder()
+    {
+        var embed = new EmbeddingOptions { BatchSize = 2 };
+        // Each batch echoes vectors seeded by the input string's first char so we can assert global order.
+        var handler = new ScriptedHandler((_, input) =>
+            EmbeddingsOk(input.Select((s, i) => (i, Vec(s[0])))));
+        var client = Client(handler, embed);
+
+        var result = await client.EmbedAsync(["0", "1", "2", "3", "4"]); // 5 inputs, batch 2 => 3 requests
+
+        Assert.Equal(3, handler.CapturedBodies.Count);
+        Assert.Equal(5, result.Count);
+        Assert.Equal((float)'0', result[0][0]);
+        Assert.Equal((float)'4', result[4][0]);
+    }
+
+    [Fact]
+    public async Task EmbedAsync_NullModel_UsesEmbeddingOptionsModel_OverrideWins()
+    {
+        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
+        var client = Client(handler);
+
+        await client.EmbedAsync(["x"], model: null);
+        await client.EmbedAsync(["x"], model: "custom/model");
+
+        using var d0 = JsonDocument.Parse(handler.CapturedBodies[0]);
+        using var d1 = JsonDocument.Parse(handler.CapturedBodies[1]);
+        Assert.Equal("openai/text-embedding-3-small", d0.RootElement.GetProperty("model").GetString());
+        Assert.Equal("custom/model", d1.RootElement.GetProperty("model").GetString());
+    }
+
+    [Fact]
+    public async Task EmbedAsync_EmptyAndWhitespaceInputs_AreSkipped()
+    {
+        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
+        var client = Client(handler);
+
+        var result = await client.EmbedAsync(["real", "", "   ", "also-real"]);
+
+        Assert.Equal(2, result.Count);
+        using var doc = JsonDocument.Parse(handler.CapturedBodies.Single());
+        var sent = doc.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()!).ToArray();
+        Assert.Equal(["real", "also-real"], sent);
+    }
+
+    [Fact]
+    public async Task EmbedAsync_AllEmptyInputs_ReturnsEmpty_NoHttpCall()
+    {
+        var handler = new ScriptedHandler((_, _) => throw new Xunit.Sdk.XunitException("should not call API"));
+        var client = Client(handler);
+
+        var result = await client.EmbedAsync(["", "  "]);
+
+        Assert.Empty(result);
+        Assert.Empty(handler.CapturedBodies);
+    }
+
+    [Fact]
+    public async Task EmbedAsync_NonSuccessStatus_Throws()
+    {
+        var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
+        {
+            Content = new StringContent("rate limited"),
+        });
+        var client = Client(handler);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["x"]));
+    }
+
+    [Fact]
+    public async Task EmbedAsync_ZeroVector_Throws()
+    {
+        var handler = new ScriptedHandler((_, _) => EmbeddingsOk([(0, new float[1536])]));
+        var client = Client(handler);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["x"]));
+    }
+
+    // Note: a NaN/Infinity cannot arrive via standard JSON (System.Text.Json rejects it on both
+    // serialize and deserialize), so the ValidateVector NaN guard is unreachable through this boundary
+    // and kept only as defense-in-depth — not asserted here. The zero-vector case below is the real,
+    // reachable R-H2 guard (a zero vector is valid JSON but undefined for cosine).
+
+    [Fact]
+    public async Task EmbedAsync_WrongDimension_Throws()
+    {
+        var handler = new ScriptedHandler((_, _) => EmbeddingsOk([(0, Vec(1, dims: 512))]));
+        var client = Client(handler);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["x"]));
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs b/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs
index 239bf0b..11320c5 100644
--- a/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs
@@ -35,7 +35,7 @@ public class OpenRouterClientTests
             ApiKey = "test-key",
             Model = "google/gemini-2.5-pro"
         });
-        var client = new OpenRouterClient(http, options, NullLogger<OpenRouterClient>.Instance);
+        var client = new OpenRouterClient(http, options, Options.Create(new EmbeddingOptions()), NullLogger<OpenRouterClient>.Instance);
 
         await client.SendPromptAsync("sys", "user", model: "google/gemini-2.5-flash");
 
@@ -53,7 +53,7 @@ public class OpenRouterClientTests
             ApiKey = "test-key",
             Model = "google/gemini-2.5-pro"
         });
-        var client = new OpenRouterClient(http, options, NullLogger<OpenRouterClient>.Instance);
+        var client = new OpenRouterClient(http, options, Options.Create(new EmbeddingOptions()), NullLogger<OpenRouterClient>.Instance);
 
         await client.SendPromptAsync("sys", "user");
 
