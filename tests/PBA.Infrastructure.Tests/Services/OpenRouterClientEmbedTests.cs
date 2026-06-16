using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class OpenRouterClientEmbedTests
{
    // Fake handler: records every request body and returns a scripted embeddings response per call.
    private sealed class ScriptedHandler(Func<int, string[], HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> CapturedBodies { get; } = [];
        private int _call;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            CapturedBodies.Add(body);
            var input = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("input")
                .EnumerateArray().Select(e => e.GetString()!).ToArray();
            return respond(_call++, input);
        }
    }

    private static HttpResponseMessage EmbeddingsOk(IEnumerable<(int index, float[] embedding)> data)
    {
        var payload = new { data = data.Select(d => new { index = d.index, embedding = d.embedding }) };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private static float[] Vec(float seed, int dims = 1536)
    {
        var v = new float[dims];
        for (var i = 0; i < dims; i++) v[i] = seed + i * 0.0001f;
        return v;
    }

    private static OpenRouterClient Client(ScriptedHandler handler, EmbeddingOptions? embed = null) =>
        new(new HttpClient(handler),
            Options.Create(new OpenRouterOptions { ApiKey = "test-key", BaseUrl = "https://openrouter.ai/api/v1", TimeoutMs = 30000 }),
            Options.Create(embed ?? new EmbeddingOptions()),
            NullLogger<OpenRouterClient>.Instance);

    [Fact]
    public async Task EmbedAsync_PostsToEmbeddingsEndpoint_WithExpectedBody()
    {
        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
        var client = Client(handler);

        await client.EmbedAsync(["hello"]);

        using var doc = JsonDocument.Parse(handler.CapturedBodies.Single());
        var root = doc.RootElement;
        Assert.Equal("openai/text-embedding-3-small", root.GetProperty("model").GetString());
        Assert.Equal("float", root.GetProperty("encoding_format").GetString());
        Assert.Equal(1536, root.GetProperty("dimensions").GetInt32());
        Assert.Single(root.GetProperty("input").EnumerateArray());
    }

    [Fact]
    public async Task EmbedAsync_ApiReturnsDataOutOfOrder_ResultsInInputOrder()
    {
        // Return data[] shuffled; index 0,1,2 carry distinguishable first elements.
        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(
        [
            (2, Vec(20)), (0, Vec(0)), (1, Vec(10)),
        ]));
        var client = Client(handler);

        var result = await client.EmbedAsync(["a", "b", "c"]);

        Assert.Equal(3, result.Count);
        Assert.Equal(0f, result[0][0]);
        Assert.Equal(10f, result[1][0]);
        Assert.Equal(20f, result[2][0]);
    }

    [Fact]
    public async Task EmbedAsync_MoreThanBatchSize_IssuesMultipleRequestsPreservingOrder()
    {
        var embed = new EmbeddingOptions { BatchSize = 2 };
        // Each batch echoes vectors seeded by the input string's first char so we can assert global order.
        var handler = new ScriptedHandler((_, input) =>
            EmbeddingsOk(input.Select((s, i) => (i, Vec(s[0])))));
        var client = Client(handler, embed);

        var result = await client.EmbedAsync(["0", "1", "2", "3", "4"]); // 5 inputs, batch 2 => 3 requests

        Assert.Equal(3, handler.CapturedBodies.Count);
        Assert.Equal(5, result.Count);
        Assert.Equal((float)'0', result[0][0]);
        Assert.Equal((float)'4', result[4][0]);
    }

    [Fact]
    public async Task EmbedAsync_NullModel_UsesEmbeddingOptionsModel_OverrideWins()
    {
        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
        var client = Client(handler);

        await client.EmbedAsync(["x"], model: null);
        await client.EmbedAsync(["x"], model: "custom/model");

        using var d0 = JsonDocument.Parse(handler.CapturedBodies[0]);
        using var d1 = JsonDocument.Parse(handler.CapturedBodies[1]);
        Assert.Equal("openai/text-embedding-3-small", d0.RootElement.GetProperty("model").GetString());
        Assert.Equal("custom/model", d1.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task EmbedAsync_EmptyAndWhitespaceInputs_AreSkipped()
    {
        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
        var client = Client(handler);

        var result = await client.EmbedAsync(["real", "", "   ", "also-real"]);

        Assert.Equal(2, result.Count);
        using var doc = JsonDocument.Parse(handler.CapturedBodies.Single());
        var sent = doc.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["real", "also-real"], sent);
    }

    [Fact]
    public async Task EmbedAsync_InputLongerThanMax_IsTruncatedBeforeSend()
    {
        // A single over-long input (e.g. a full article body) exceeds the embedding model's per-input
        // token limit and makes the provider return 0 vectors, failing the whole batch. The client must
        // cap each input to MaxInputChars before sending so one giant idea never poisons its batch.
        var embed = new EmbeddingOptions { MaxInputChars = 10 };
        var handler = new ScriptedHandler((_, input) => EmbeddingsOk(input.Select((_, i) => (i, Vec(i)))));
        var client = Client(handler, embed);

        await client.EmbedAsync([new string('a', 50)]);

        using var doc = JsonDocument.Parse(handler.CapturedBodies.Single());
        var sent = doc.RootElement.GetProperty("input").EnumerateArray().Single().GetString()!;
        Assert.Equal(10, sent.Length);
    }

    [Fact]
    public async Task EmbedAsync_AllEmptyInputs_ReturnsEmpty_NoHttpCall()
    {
        var handler = new ScriptedHandler((_, _) => throw new Xunit.Sdk.XunitException("should not call API"));
        var client = Client(handler);

        var result = await client.EmbedAsync(["", "  "]);

        Assert.Empty(result);
        Assert.Empty(handler.CapturedBodies);
    }

    [Fact]
    public async Task EmbedAsync_NonSuccessStatus_Throws()
    {
        var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("rate limited"),
        });
        var client = Client(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["x"]));
    }

    [Fact]
    public async Task EmbedAsync_ZeroVector_Throws()
    {
        var handler = new ScriptedHandler((_, _) => EmbeddingsOk([(0, new float[1536])]));
        var client = Client(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["x"]));
    }

    // Note: a NaN/Infinity cannot arrive via standard JSON (System.Text.Json rejects it on both
    // serialize and deserialize), so the ValidateVector NaN guard is unreachable through this boundary
    // and kept only as defense-in-depth — not asserted here. The zero-vector case below is the real,
    // reachable R-H2 guard (a zero vector is valid JSON but undefined for cosine).

    [Fact]
    public async Task EmbedAsync_WrongDimension_Throws()
    {
        var handler = new ScriptedHandler((_, _) => EmbeddingsOk([(0, Vec(1, dims: 512))]));
        var client = Client(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["x"]));
    }

    [Fact]
    public async Task EmbedAsync_NonContiguousIndices_Throws()
    {
        // 3 inputs but the API returns index 5 (count matches but index is out of range) — must NOT be
        // silently positionally zipped.
        var handler = new ScriptedHandler((_, _) => EmbeddingsOk([(0, Vec(0)), (1, Vec(1)), (5, Vec(5))]));
        var client = Client(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["a", "b", "c"]));
    }

    [Fact]
    public async Task EmbedAsync_DuplicateIndex_Throws()
    {
        var handler = new ScriptedHandler((_, _) => EmbeddingsOk([(0, Vec(0)), (0, Vec(1))]));
        var client = Client(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync(["a", "b"]));
    }
}
