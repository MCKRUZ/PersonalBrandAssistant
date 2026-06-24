using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services;

/// <summary>
/// ISidecarClient backed by the OpenRouter chat-completions API (OpenAI-compatible).
/// Replaces the agentic claude CLI for fast, length-controllable drafting.
/// </summary>
public sealed class OpenRouterClient(
    HttpClient httpClient,
    IOptions<OpenRouterOptions> options,
    IOptions<EmbeddingOptions> embeddingOptions,
    ILogger<OpenRouterClient> logger) : ISidecarClient
{
    private readonly OpenRouterOptions _options = options.Value;
    private readonly EmbeddingOptions _embeddingOptions = embeddingOptions.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
        => SendPromptAsync(systemPrompt, userPrompt, model, temperature: null, ct);

    public async Task<string> SendPromptAsync(
        string systemPrompt, string userPrompt, string? model, double? temperature, CancellationToken ct = default)
    {
        var payload = new ChatRequest(
            model ?? _options.Model,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
            _options.MaxTokens,
            temperature);

        var body = await PostJsonAsync("chat/completions", payload, ct);

        var content = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions)
            ?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"OpenRouter returned an empty response from {_options.Model}.");

        return content.Trim();
    }

    // Shared transport for chat + embeddings: auth/referer headers, per-request timeout, error mapping.
    private async Task<string> PostJsonAsync(string path, object payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("OpenRouter API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/{path}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Add("HTTP-Referer", "https://matthewkruczek.ai");
        request.Headers.Add("X-Title", "Personal Brand Assistant");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.TimeoutMs);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenRouter request to {path} timed out after {_options.TimeoutMs}ms");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("OpenRouter request to {Path} failed: {Status} {Body}", path, response.StatusCode, Truncate(body));
            throw new InvalidOperationException($"OpenRouter request to {path} failed ({(int)response.StatusCode})");
        }

        return body;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default)
    {
        // Drop empty/whitespace inputs — a meaningless vector would corrupt dedup/pre-filter (R-H2).
        // (API-key validation happens in PostJsonAsync, so an all-empty call returns [] without it.)
        // Cap each input to MaxInputChars: one over-long input exceeds the model's per-input token limit
        // and makes the provider return 0 vectors for the whole batch, leaving every co-batched idea null.
        var sanitized = inputs
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(TruncateInput)
            .ToList();
        if (sanitized.Count == 0)
            return [];

        var resolvedModel = model ?? _embeddingOptions.Model;
        var results = new List<float[]>(sanitized.Count);

        foreach (var batch in sanitized.Chunk(_embeddingOptions.BatchSize))
        {
            results.AddRange(await EmbedBatchAsync(batch, resolvedModel, ct));
        }

        return results;
    }

    private string TruncateInput(string s)
    {
        var max = _embeddingOptions.MaxInputChars;
        if (max <= 0 || s.Length <= max) return s;
        // Don't cut through a surrogate pair — a lone surrogate is invalid UTF-16 and breaks JSON encoding.
        var end = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
        return s[..end];
    }

    private async Task<float[][]> EmbedBatchAsync(string[] batch, string model, CancellationToken ct)
    {
        var payload = new EmbeddingRequest(model, batch, "float", _embeddingOptions.Dimensions);
        var body = await PostJsonAsync("embeddings", payload, ct);

        var data = JsonSerializer.Deserialize<EmbeddingResponse>(body, JsonOptions)?.Data;
        if (data is null || data.Count != batch.Length)
            throw new InvalidOperationException(
                $"OpenRouter embeddings returned {data?.Count ?? 0} vectors for {batch.Length} inputs.");

        // The API does not guarantee data[] order, so map each result to its input by `index` rather
        // than positional zip. Reject out-of-range, duplicate, or missing indices — a misaligned vector
        // would silently feed garbage into dedup/pre-filter.
        var ordered = new float[batch.Length][];
        foreach (var d in data)
        {
            if (d.Index < 0 || d.Index >= batch.Length)
                throw new InvalidOperationException($"OpenRouter embedding index {d.Index} out of range [0,{batch.Length}).");
            if (ordered[d.Index] is not null)
                throw new InvalidOperationException($"OpenRouter embeddings returned a duplicate index {d.Index}.");
            ordered[d.Index] = ValidateVector(d.Embedding);
        }

        for (var i = 0; i < ordered.Length; i++)
            if (ordered[i] is null)
                throw new InvalidOperationException($"OpenRouter embeddings missing a vector for input index {i}.");

        return ordered;
    }

    // Reject zero/NaN/Infinity vectors so a corrupt embedding never enters the system (R-H2);
    // the item stays unembedded and is retried on the next sweep.
    private float[] ValidateVector(float[]? vector)
    {
        if (vector is null || vector.Length != _embeddingOptions.Dimensions)
            throw new InvalidOperationException(
                $"OpenRouter embedding has length {vector?.Length ?? 0}, expected {_embeddingOptions.Dimensions}.");

        var allZero = true;
        foreach (var v in vector)
        {
            if (!float.IsFinite(v))
                throw new InvalidOperationException("OpenRouter embedding contains a NaN/Infinity value.");
            if (v != 0f) allZero = false;
        }

        if (allZero)
            throw new InvalidOperationException("OpenRouter embedding is a zero vector.");

        return vector;
    }

    // Non-incremental streaming: yields the full completion once. The UI gets the
    // text in a single chunk. Token-by-token SSE streaming is a future enhancement.
    public async IAsyncEnumerable<string> StreamPromptAsync(
        Guid contentId,
        string systemPrompt,
        string userPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await SendPromptAsync(systemPrompt, userPrompt, model: null, ct);
        yield return result;
    }

    private static string Truncate(string s) => s.Length > 500 ? s[..500] : s;

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        // Omitted from the JSON when null (DefaultIgnoreCondition = WhenWritingNull), so existing
        // drafting calls are byte-for-byte unchanged; only low-variance scoring sets it.
        [property: JsonPropertyName("temperature")] double? Temperature = null);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

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
}
