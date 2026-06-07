using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Scrapers;

/// <summary>Scrapes GitHub repo releases or user public events. The watched target comes from
/// <c>source.ApiUrl</c> as <c>github:repo:owner/name</c> or <c>github:user:username</c>; the value is
/// parsed into path segments only — never used as a raw request URL (no SSRF surface).</summary>
public sealed class GitHubScraper(
    HttpClient http,
    IOptions<GitHubScraperOptions> options,
    ILogger<GitHubScraper> logger) : ISourceScraper
{
    private readonly GitHubScraperOptions _options = options.Value;

    public async Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default)
    {
        var spec = ParseApiUrl(source.ApiUrl);
        if (spec is null)
        {
            logger.LogWarning("GitHub source {Name} has invalid ApiUrl '{ApiUrl}'", source.Name, source.ApiUrl);
            return [];
        }

        try
        {
            return spec.Value.Kind switch
            {
                "repo" => await FetchReleasesAsync(spec.Value.A, spec.Value.B!, since, ct),
                "user" => await FetchUserEventsAsync(spec.Value.A, since, ct),
                _ => []
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "GitHub fetch failed for {Name}", source.Name);
            return [];
        }
    }

    // "github:repo:owner/name" -> ("repo","owner","name"); "github:user:username" -> ("user","username",null)
    private static (string Kind, string A, string? B)? ParseApiUrl(string? apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl)) return null;
        var parts = apiUrl.Split(':', 3);
        if (parts.Length < 3 || parts[0] != "github") return null;
        var kind = parts[1];
        var target = parts[2];
        if (kind == "repo")
        {
            var seg = target.Split('/', 2);
            if (seg.Length != 2 || seg.Any(string.IsNullOrWhiteSpace)) return null;
            return ("repo", seg[0], seg[1]);
        }
        if (kind == "user" && !string.IsNullOrWhiteSpace(target))
            return ("user", target, null);
        return null;
    }

    private HttpRequestMessage Request(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.UserAgent.ParseAdd("PBA-Radar");
        if (!string.IsNullOrWhiteSpace(_options.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("token", _options.Token);
        return req;
    }

    private async Task<IReadOnlyList<ScrapedItem>> FetchReleasesAsync(
        string owner, string repo, DateTimeOffset since, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Request($"/repos/{owner}/{repo}/releases"), ct);
        if (!resp.IsSuccessStatusCode) return [];
        var releases = await resp.Content.ReadFromJsonAsyncSafe<List<GhRelease>>(ct) ?? [];
        return releases
            .Where(r => r.PublishedAt >= since && !string.IsNullOrWhiteSpace(r.HtmlUrl))
            .Select(r => new ScrapedItem(
                $"{owner}/{repo} {(string.IsNullOrWhiteSpace(r.Name) ? r.TagName : r.Name)}",
                r.Body, r.HtmlUrl, null, r.PublishedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<ScrapedItem>> FetchUserEventsAsync(
        string username, DateTimeOffset since, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Request($"/users/{username}/events/public"), ct);
        if (!resp.IsSuccessStatusCode) return [];
        var events = await resp.Content.ReadFromJsonAsyncSafe<List<GhEvent>>(ct) ?? [];
        return events
            .Where(e => e.CreatedAt >= since && e.Repo is not null)
            .Take(_options.MaxEventsPerSource)
            .Select(e => new ScrapedItem(
                $"{e.Type}: {e.Repo!.Name}", null,
                $"https://github.com/{e.Repo.Name}", null, e.CreatedAt))
            .ToList();
    }

    private sealed class GhRelease
    {
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        public string? Body { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; set; }
    }

    private sealed class GhEvent
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        public GhRepo? Repo { get; set; }
    }

    private sealed class GhRepo { public string? Name { get; set; } }
}
