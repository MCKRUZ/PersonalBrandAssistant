using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

/// <summary>
/// Scores an idea against the active brand profile snapshot, per pillar, on the {0,.25,.5,.75,1} scale.
/// The LLM returns pillar NAMES; identity is the <c>BrandPillarId</c> resolved from the snapshot (R-C3),
/// so a later rename never breaks stored sub-scores. Runs at low temperature for cross-sweep stability.
/// </summary>
public sealed class IdeaAnalyzer(
    ISidecarClient sidecar,
    IOptions<IdeaScoringOptions> options,
    ILogger<IdeaAnalyzer> logger) : IIdeaAnalyzer
{
    private readonly IdeaScoringOptions _options = options.Value;

    // Low temperature so the same item scores consistently sweep-to-sweep (the ranking pipeline's input).
    private const double ScoringTemperature = 0.1;
    private const double CollapseEpsilon = 1e-9;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IdeaAnalysis?> AnalyzeAsync(
        IdeaAnalysisInput input, BrandRankingProfileSnapshot profile, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(profile);
        var user = BuildUserPrompt(input);

        var response = await sidecar.SendPromptAsync(system, user, _options.Model, ScoringTemperature, ct);

        var raw = Parse(response);
        if (raw?.Pillars is null)
        {
            logger.LogWarning("Idea analysis returned no parseable pillars for '{Title}'", input.Title);
            return null;
        }

        // Resolve LLM-returned names -> BrandPillarId against the snapshot (case-insensitive, trimmed —
        // LLMs drift on casing/whitespace). Unknown names are dropped, not invented (R-C3).
        var byName = profile.Pillars.ToDictionary(p => p.Name.Trim(), p => p, StringComparer.OrdinalIgnoreCase);
        var mapped = new List<PillarScore>(raw.Pillars.Count);
        foreach (var rp in raw.Pillars)
        {
            if (string.IsNullOrWhiteSpace(rp.Name) || !byName.TryGetValue(rp.Name.Trim(), out var pillar))
            {
                logger.LogWarning("Dropping unmapped pillar name '{Name}' for '{Title}'", rp.Name, input.Title);
                continue;
            }
            mapped.Add(new PillarScore(pillar.Id, pillar.Name, Math.Clamp(rp.Score, 0, 1), rp.Reason));
        }

        if (mapped.Count == 0)
        {
            logger.LogWarning("No returned pillar mapped to the profile for '{Title}'", input.Title);
            return null;
        }

        // Central-collapse guard (R-M5): an all-identical sub-score vector is the model refusing to
        // discriminate; storing it would flatten brandFit. Only meaningful with >= 2 pillars.
        if (mapped.Count >= 2 && mapped.All(p => Math.Abs(p.Score - mapped[0].Score) < CollapseEpsilon))
        {
            logger.LogWarning("Rejecting collapsed analysis (all {Count} pillars == {Score}) for '{Title}'",
                mapped.Count, mapped[0].Score, input.Title);
            return null;
        }

        return new IdeaAnalysis(mapped, raw.IsAntiTopic, raw.IsAuthorityTopic, raw.Reason ?? "");
    }

    private static string BuildSystemPrompt(BrandRankingProfileSnapshot p)
    {
        var sb = new StringBuilder();
        sb.Append("You are a content strategist for a thought leader positioned as: ")
          .Append(p.Positioning).Append('\n');
        sb.Append("Primary audience: ").Append(p.AudiencePrimary).Append('\n');
        if (!string.IsNullOrWhiteSpace(p.AudienceSecondary))
            sb.Append("Secondary audience: ").Append(p.AudienceSecondary).Append('\n');

        sb.Append("\nScore how strong a CONTENT OPPORTUNITY the item is for EACH brand pillar below, ")
          .Append("independently. This is content-worthiness, not generic newsworthiness.\n\nPillars:\n");
        foreach (var pillar in p.Pillars)
            sb.Append("- ").Append(pillar.Name).Append(": ").Append(pillar.Description).Append('\n');

        sb.Append("\nPer-pillar score scale:\n")
          .Append("1.0 = a strong, ownable thought-leadership angle for this pillar\n")
          .Append("0.75 = clearly relevant, postable for this pillar\n")
          .Append("0.5 = tangential, would need an angle\n")
          .Append("0.25 = weak fit\n")
          .Append("0 = off-topic for this pillar\n");

        if (p.AuthorityTopics.Count > 0)
            sb.Append("\nAuthority topics (set isAuthorityTopic=true if the item is squarely one of these): ")
              .Append(string.Join("; ", p.AuthorityTopics)).Append('\n');
        if (p.AntiTopics.Count > 0)
            sb.Append("Anti topics (set isAntiTopic=true if the item is one of these): ")
              .Append(string.Join("; ", p.AntiTopics)).Append('\n');

        sb.Append('\n').Append(FewShotAnchors).Append('\n');

        sb.Append("\nScore deterministically and with low variance — the same item must score the same ")
          .Append("way every time. Respond with ONLY a JSON object (no markdown fences, no extra text):\n")
          .Append("""{"pillars":[{"name":"<exact pillar name>","score":0-1,"reason":"one short phrase"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"one short sentence"}""");
        return sb.ToString();
    }

    // Fixed, brand-agnostic exemplars that calibrate the 0..1 scale. Hardcoded and identical on every
    // call (NOT derived from the snapshot) so the scale stays stable; the pillar names here are
    // illustrative placeholders — always score the actual pillars listed above.
    private const string FewShotAnchors =
        """
        Calibration examples (illustrative; score the pillars listed above):
        Example A — an item that is a strong, ownable angle for one pillar and off-topic for another:
        {"pillars":[{"name":"Pillar One","score":1,"reason":"ownable angle"},{"name":"Pillar Two","score":0,"reason":"off-topic"}],"isAntiTopic":false,"isAuthorityTopic":true,"reason":"strong fit for pillar one"}
        Example B — an item that is off-brand for every pillar:
        {"pillars":[{"name":"Pillar One","score":0,"reason":"off-brand"},{"name":"Pillar Two","score":0.25,"reason":"weak tangent"}],"isAntiTopic":true,"isAuthorityTopic":false,"reason":"off-brand, anti-topic"}
        """;

    private static string BuildUserPrompt(IdeaAnalysisInput input)
    {
        var lines = new List<string> { $"Title: {input.Title}", $"Source: {input.SourceName}" };
        if (!string.IsNullOrWhiteSpace(input.Url)) lines.Add($"URL: {input.Url}");
        if (!string.IsNullOrWhiteSpace(input.Description))
            lines.Add($"Content: {input.Description[..Math.Min(1000, input.Description.Length)]}");
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

    // The model still occasionally wraps output in ``` fences despite the instruction; tolerate it.
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
        List<RawPillar>? Pillars,
        bool IsAntiTopic,
        bool IsAuthorityTopic,
        string? Reason);

    private sealed record RawPillar(string? Name, double Score, string? Reason);
}
