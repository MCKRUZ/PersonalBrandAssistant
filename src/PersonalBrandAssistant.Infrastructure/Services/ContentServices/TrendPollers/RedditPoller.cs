using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

internal sealed class RedditPoller(
    IHttpClientFactory httpClientFactory,
    IOptions<TrendMonitoringOptions> options,
    ILogger<RedditPoller> logger) : ITrendSourcePoller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrendSourceType SourceType => TrendSourceType.Reddit;

    public async Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("Reddit");
        var results = new List<TrendItem>();

        foreach (var subreddit in options.Value.RedditSubreddits)
        {
            try
            {
                var response = await client.GetAsync($"/r/{subreddit}/hot.json?limit=25", ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var listing = JsonSerializer.Deserialize<RedditListing>(json, JsonOptions);

                if (listing?.Data?.Children is not null)
                {
                    results.AddRange(listing.Data.Children.Select(child => new TrendItem
                    {
                        Title = child.Data?.Title ?? "",
                        Description = child.Data?.Selftext,
                        Url = child.Data?.Url,
                        SourceName = $"r/{subreddit}",
                        SourceType = TrendSourceType.Reddit,
                        TrendSourceId = source.Id,
                        DetectedAt = DateTimeOffset.UtcNow,
                    }));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to poll r/{Subreddit}", subreddit);
            }
        }

        return results;
    }

    private sealed class RedditListing
    {
        [JsonPropertyName("data")] public RedditListingData? Data { get; set; }
    }

    private sealed class RedditListingData
    {
        [JsonPropertyName("children")] public List<RedditChild>? Children { get; set; }
    }

    private sealed class RedditChild
    {
        [JsonPropertyName("data")] public RedditPost? Data { get; set; }
    }

    private sealed class RedditPost
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("selftext")] public string? Selftext { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
