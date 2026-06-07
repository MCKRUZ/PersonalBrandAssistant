using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Scrapers;

/// <summary>Scrapes top Hacker News stories (Firebase API, no auth) above a score threshold,
/// folding a few top comments into the description for content-angle context.</summary>
public sealed class HackerNewsScraper(
    HttpClient http,
    IOptions<HackerNewsOptions> options,
    ILogger<HackerNewsScraper> logger) : ISourceScraper
{
    private const string BaseUrl = "https://hacker-news.firebaseio.com/v0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HackerNewsOptions _options = options.Value;

    public async Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default)
    {
        try
        {
            var ids = await GetJsonAsync<long[]>($"{BaseUrl}/topstories.json", ct) ?? [];
            var items = new List<ScrapedItem>();

            foreach (var id in ids.Take(_options.FetchTopStories))
            {
                var story = await GetItemAsync(id, ct);
                if (story is null || story.Type != "story") continue;
                if (story.Score < _options.MinScore) continue;

                var publishedAt = DateTimeOffset.FromUnixTimeSeconds(story.Time);
                if (publishedAt < since) continue;

                var comments = new List<string>();
                foreach (var cid in (story.Kids ?? []).Take(_options.TopComments))
                {
                    var c = await GetItemAsync(cid, ct);
                    if (!string.IsNullOrWhiteSpace(c?.Text)) comments.Add(StripHtml(c!.Text!));
                }

                var url = string.IsNullOrWhiteSpace(story.Url)
                    ? $"https://news.ycombinator.com/item?id={story.Id}"
                    : story.Url;

                items.Add(new ScrapedItem(
                    story.Title ?? "(untitled)",
                    BuildDescription(story.Text, comments),
                    url,
                    null,
                    publishedAt));
            }

            return items;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Hacker News fetch failed");
            return [];
        }
    }

    private async Task<HnItem?> GetItemAsync(long id, CancellationToken ct)
    {
        try { return await GetJsonAsync<HnItem>($"{BaseUrl}/item/{id}.json", ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { return null; }
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    private static string? BuildDescription(string? selfText, IReadOnlyList<string> comments)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(selfText)) parts.Add(StripHtml(selfText));
        if (comments.Count > 0) parts.Add("Top comments: " + string.Join(" | ", comments));
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static string StripHtml(string s) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty));

    private sealed class HnItem
    {
        public long Id { get; set; }
        public string? Type { get; set; }
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Text { get; set; }
        public int Score { get; set; }
        public long Time { get; set; }
        public List<long>? Kids { get; set; }
    }
}
