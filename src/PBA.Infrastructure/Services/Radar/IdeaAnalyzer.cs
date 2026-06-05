using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaAnalyzer(
    ISidecarClient sidecar,
    IOptions<IdeaScoringOptions> options,
    ILogger<IdeaAnalyzer> logger) : IIdeaAnalyzer
{
    private readonly IdeaScoringOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IdeaAnalysis?> AnalyzeAsync(
        string title, string? description, string? url, string sourceName, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt();
        var user = BuildUserPrompt(title, description, url, sourceName);

        var response = await sidecar.SendPromptAsync(system, user, _options.Model, ct);

        var raw = Parse(response);
        if (raw is null) return null;

        var score = Math.Clamp(raw.Score, 0, 10);
        return new IdeaAnalysis(score, raw.Reason ?? "", raw.Summary ?? "", raw.Category,
            raw.Tags ?? []);
    }

    private static string BuildSystemPrompt() =>
        """
        You are a content strategist for Matt Kruczek, an enterprise AI thought leader. His brand
        covers enterprise AI adoption, agentic development, and AI strategy for a developer-to-executive
        audience. Score how strong a CONTENT OPPORTUNITY a news item is for his brand, 0 to 10.

        This is content-worthiness, NOT generic newsworthiness. A huge story he would never write about
        scores low. A smaller story with an ownable, opinionated angle scores high.

        Rubric:
        9-10: a strong, ownable thought-leadership angle he could publish a great post on
        7-8: clearly relevant, postable with a good take
        5-6: tangentially relevant, would need an angle
        3-4: weak fit
        0-2: off-brand or not worth covering

        Respond with ONLY a JSON object, no markdown fences, no extra text:
        {"score": 0-10, "reason": "one short sentence", "summary": "one-sentence summary of the item",
         "category": "short category or null", "tags": ["3-5", "keywords"]}
        """;

    private static string BuildUserPrompt(string title, string? description, string? url, string sourceName)
    {
        var lines = new List<string>
        {
            $"Title: {title}",
            $"Source: {sourceName}"
        };
        if (!string.IsNullOrWhiteSpace(url)) lines.Add($"URL: {url}");
        if (!string.IsNullOrWhiteSpace(description))
            lines.Add($"Content: {description[..Math.Min(1000, description.Length)]}");
        return string.Join('\n', lines);
    }

    private Raw? Parse(string response)
    {
        var json = StripFences(response);
        try
        {
            return JsonSerializer.Deserialize<Raw>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse idea analysis JSON: {Snippet}",
                response[..Math.Min(200, response.Length)]);
            return null;
        }
    }

    private static string StripFences(string response)
    {
        var json = response.Trim();
        if (!json.StartsWith("```")) return json;
        var firstNewline = json.IndexOf('\n');
        if (firstNewline >= 0) json = json[(firstNewline + 1)..];
        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) json = json[..lastFence];
        return json.Trim();
    }

    private sealed record Raw(
        int Score,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("tags")] List<string>? Tags);
}
