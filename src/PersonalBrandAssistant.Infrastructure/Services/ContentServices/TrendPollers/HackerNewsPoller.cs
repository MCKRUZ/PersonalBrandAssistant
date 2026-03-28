using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

internal sealed class HackerNewsPoller(
    IHttpClientFactory httpClientFactory,
    IOptions<TrendMonitoringOptions> options,
    ILogger<HackerNewsPoller> logger) : ITrendSourcePoller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrendSourceType SourceType => TrendSourceType.HackerNews;

    public async Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("HackerNews");
        var opts = options.Value;

        var response = await client.GetAsync($"{opts.HackerNewsApiUrl}/topstories.json", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var ids = JsonSerializer.Deserialize<int[]>(json, JsonOptions) ?? [];

        var topIds = ids.Take(opts.HackerNewsTopN);
        using var semaphore = new SemaphoreSlim(opts.HackerNewsConcurrency);

        var tasks = topIds.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var itemResponse = await client.GetAsync(
                    $"{opts.HackerNewsApiUrl}/item/{id}.json", ct);
                if (!itemResponse.IsSuccessStatusCode)
                    return null;

                var itemJson = await itemResponse.Content.ReadAsStringAsync(ct);
                var hnItem = JsonSerializer.Deserialize<HackerNewsItem>(itemJson, JsonOptions);

                if (hnItem is null)
                    return null;

                return new TrendItem
                {
                    Title = hnItem.Title ?? "",
                    Description = hnItem.Text,
                    Url = hnItem.Url,
                    SourceName = "HackerNews",
                    SourceType = TrendSourceType.HackerNews,
                    TrendSourceId = source.Id,
                    DetectedAt = hnItem.Time is > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(hnItem.Time.Value)
                        : DateTimeOffset.UtcNow,
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch HN item {ItemId}", id);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var fetched = await Task.WhenAll(tasks);
        return fetched.Where(i => i is not null).ToList()!;
    }

    private sealed class HackerNewsItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("time")] public long? Time { get; set; }
    }
}
