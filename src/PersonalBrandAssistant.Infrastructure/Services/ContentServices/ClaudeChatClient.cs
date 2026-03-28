using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class ClaudeChatClient : IClaudeChatClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeChatClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ClaudeChatClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ClaudeChatClient> logger)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Anthropic");
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/");
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var apiKey = configuration["Anthropic:ApiKey"] ?? "";
        if (!string.IsNullOrEmpty(apiKey))
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string model, int maxTokens, string systemPrompt,
        IReadOnlyList<ClaudeChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new AnthropicRequest(model, maxTokens, systemPrompt,
            messages.Select(m => new AnthropicMessage(m.Role, m.Content)).ToList(), true);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {

            if (line.StartsWith("data: "))
            {
                var data = line[6..];
                if (data == "[DONE]") break;

                var textChunk = ExtractTextDelta(data);
                if (textChunk is not null)
                    yield return textChunk;
            }
        }
    }

    public async Task<string> SendMessageAsync(
        string model, int maxTokens, string systemPrompt,
        IReadOnlyList<ClaudeChatMessage> messages, CancellationToken ct)
    {
        var request = new AnthropicRequest(model, maxTokens, systemPrompt,
            messages.Select(m => new AnthropicMessage(m.Role, m.Content)).ToList(), false);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("content", out var content))
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }
        }
        return sb.ToString();
    }

    private static string? ExtractTextDelta(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeEl)
                && typeEl.GetString() == "content_block_delta"
                && root.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private record AnthropicMessage(string Role, string Content);

    private record AnthropicRequest(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        string System,
        List<AnthropicMessage> Messages,
        bool Stream);
}
