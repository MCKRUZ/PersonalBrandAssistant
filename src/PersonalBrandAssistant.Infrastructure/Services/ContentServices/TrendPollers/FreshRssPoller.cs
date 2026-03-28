using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

internal sealed class FreshRssPoller(
    IHttpClientFactory httpClientFactory,
    IOptions<TrendMonitoringOptions> options) : ITrendSourcePoller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrendSourceType SourceType => TrendSourceType.FreshRSS;

    public async Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("FreshRSS");
        var url = $"{options.Value.FreshRssApiUrl}/greader.php/reader/api/0/stream/contents/reading-list";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var feed = JsonSerializer.Deserialize<FreshRssFeed>(json, JsonOptions);

        return (feed?.Items ?? []).Select(item => new TrendItem
        {
            Title = item.Title ?? "",
            Description = item.Summary,
            Url = item.Url,
            SourceName = source.Name,
            SourceType = TrendSourceType.FreshRSS,
            TrendSourceId = source.Id,
            DetectedAt = item.Published ?? DateTimeOffset.UtcNow,
        }).ToList();
    }

    private sealed class FreshRssFeed
    {
        [JsonPropertyName("items")] public List<FreshRssItem>? Items { get; set; }
    }

    private sealed class FreshRssItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("published")] public DateTimeOffset? Published { get; set; }
    }
}
