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
    ILogger<OpenRouterClient> logger) : ISidecarClient
{
    private readonly OpenRouterOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("OpenRouter API key is not configured.");

        var payload = new ChatRequest(
            model ?? _options.Model,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
            _options.MaxTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/chat/completions")
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
            throw new TimeoutException($"OpenRouter request timed out after {_options.TimeoutMs}ms");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("OpenRouter request failed: {Status} {Body}", response.StatusCode, Truncate(body));
            throw new InvalidOperationException($"OpenRouter request failed ({(int)response.StatusCode})");
        }

        var content = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions)
            ?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"OpenRouter returned an empty response from {_options.Model}.");

        return content.Trim();
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
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
