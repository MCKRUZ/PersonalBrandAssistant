using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaClusterer(
    ISidecarClient sidecar,
    IOptions<ClusteringOptions> options,
    ILogger<IdeaClusterer> logger) : IIdeaClusterer
{
    private readonly ClusteringOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IReadOnlyList<IReadOnlyList<int>>> ClusterAsync(
        IReadOnlyList<ClusterInput> items, CancellationToken ct = default)
    {
        if (items.Count < 2) return [];

        var response = await sidecar.SendPromptAsync(System, BuildUser(items), _options.Model, ct);

        try
        {
            var parsed = JsonSerializer.Deserialize<Result>(StripFences(response), JsonOptions);
            return parsed?.Groups?
                .Where(g => g.Count >= 2)
                .Select(g => (IReadOnlyList<int>)g)
                .ToList() ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse clustering JSON");
            return [];
        }
    }

    private const string System =
        """
        You are a news deduplication assistant. Group items that report the IDENTICAL real-world
        event (for example the same product release announced in different outlets). Do NOT group
        items that merely share a topic but cover different events. When unsure, keep them separate.

        Respond with ONLY JSON, no fences:
        {"groups": [[0, 3], [5, 7, 9]]}
        Each inner array lists the input indices of one event. The first index is the primary.
        Only include groups of 2 or more. Omit singletons.
        """;

    private static string BuildUser(IReadOnlyList<ClusterInput> items)
    {
        var sb = new StringBuilder("Items:\n");
        foreach (var item in items)
        {
            sb.Append('[').Append(item.Index).Append("] ").Append(item.Title);
            if (!string.IsNullOrWhiteSpace(item.Summary)) sb.Append(" -- ").Append(item.Summary);
            sb.Append('\n');
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

    private sealed record Result([property: JsonPropertyName("groups")] List<List<int>>? Groups);
}
