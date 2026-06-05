using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

public sealed class DigestWriter(
    ISidecarClient sidecar,
    IOptions<DigestOptions> options,
    ILogger<DigestWriter> logger) : IDigestWriter
{
    private readonly DigestOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<DigestCopy?> WriteAsync(IReadOnlyList<DigestInput> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return null;

        var response = await sidecar.SendPromptAsync(System, BuildUser(items), _options.Model, ct);

        try
        {
            var raw = JsonSerializer.Deserialize<Raw>(StripFences(response), JsonOptions);
            if (raw is null) return null;

            var itemCopies = (raw.Items ?? [])
                .Select(i => new DigestItemCopy(i.Index, Clean(i.WhyItMatters ?? "")))
                .ToList();

            return new DigestCopy(Clean(raw.Title ?? "Daily Brief"), Clean(raw.Intro ?? ""), itemCopies);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse digest JSON");
            return null;
        }
    }

    // Honors the brand rule: no em-dashes in Matt-facing copy.
    private static string Clean(string s) => s.Replace('—', '-').Replace('–', '-');

    private const string System =
        """
        You write a daily AI news brief for Matt Kruczek, an enterprise AI thought leader, in his voice:
        direct, developer-to-executive, no hype, no filler. Given the day's top items, write a short intro
        (2-3 sentences) and a one-sentence "why it matters" for each item, framed around the content angle
        Matt could take. Never use em-dashes or en-dashes. Plain language only.

        Respond with ONLY JSON, no fences:
        {"title": "short title", "intro": "2-3 sentences",
         "items": [{"index": 0, "whyItMatters": "one sentence"}]}
        Include one items entry per input index.
        """;

    private static string BuildUser(IReadOnlyList<DigestInput> items)
    {
        var sb = new StringBuilder("Top items:\n");
        foreach (var item in items)
        {
            sb.Append('[').Append(item.Index).Append("] (score ").Append(item.Score).Append(") ")
              .Append(item.Title).Append(" -- ").Append(item.Summary).Append('\n');
        }
        return sb.ToString();
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
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("intro")] string? Intro,
        [property: JsonPropertyName("items")] List<RawItem>? Items);

    private sealed record RawItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("whyItMatters")] string? WhyItMatters);
}
