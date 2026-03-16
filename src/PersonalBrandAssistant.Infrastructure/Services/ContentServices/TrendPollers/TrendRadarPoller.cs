using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

internal sealed class TrendRadarPoller(
    IHttpClientFactory httpClientFactory,
    IOptions<TrendMonitoringOptions> options) : ITrendSourcePoller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrendSourceType SourceType => TrendSourceType.TrendRadar;

    public async Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("TrendRadar");
        var response = await client.GetAsync($"{options.Value.TrendRadarApiUrl}/trends", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var trends = JsonSerializer.Deserialize<List<TrendRadarItem>>(json, JsonOptions) ?? [];

        return trends.Select(t => new TrendItem
        {
            Title = t.Title ?? "",
            Description = t.Description,
            Url = t.Url,
            SourceName = source.Name,
            SourceType = TrendSourceType.TrendRadar,
            TrendSourceId = source.Id,
            DetectedAt = t.DetectedAt ?? DateTimeOffset.UtcNow,
        }).ToList();
    }

    private sealed class TrendRadarItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("detectedAt")] public DateTimeOffset? DetectedAt { get; set; }
    }
}
