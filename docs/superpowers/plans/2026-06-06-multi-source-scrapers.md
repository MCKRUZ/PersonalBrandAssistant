# Multi-Source Scrapers (Hacker News + GitHub) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Hacker News and GitHub as idea sources behind a keyed `ISourceScraper` abstraction, feeding the existing Idea → dedup → scoring → digest pipeline, by generalizing `RssPollingService` into a type-dispatching `SourcePollingService`.

**Architecture:** A new `ISourceScraper` interface is registered with keyed DI by `IdeaSourceType` (same pattern as `IPlatformConnector`). RSS becomes one scraper; HN and GitHub are two more. `SourcePollingService` iterates enabled sources, resolves the scraper for each source's type, fetches `ScrapedItem`s, and runs the existing dedup + Idea-creation + failure-tracking logic. New ideas flow into the existing scoring/clustering/digest/alert pipeline unchanged.

**Tech Stack:** .NET 10, C#, EF Core (Npgsql, in-memory for tests), keyed DI, `IHttpClientFactory` typed clients, System.Text.Json, xUnit + Moq (`Mock<HttpMessageHandler>` via `Moq.Protected`).

---

## File structure

| File | Responsibility |
|---|---|
| `src/PBA.Domain/Enums/IdeaSourceType.cs` (modify) | Append `HackerNews`, `GitHub` |
| `src/PBA.Application/Common/Interfaces/ScrapedItem.cs` (new) | Neutral ingestion DTO |
| `src/PBA.Application/Common/Interfaces/ISourceScraper.cs` (new) | Scraper interface |
| `src/PBA.Infrastructure/Services/Scrapers/RssScraper.cs` (new) | RSS impl wrapping `IRssFeedReader` |
| `src/PBA.Infrastructure/Configuration/HackerNewsOptions.cs` (new) | HN config |
| `src/PBA.Infrastructure/Services/Scrapers/HackerNewsScraper.cs` (new) | HN Firebase scraper |
| `src/PBA.Infrastructure/Configuration/GitHubScraperOptions.cs` (new) | GitHub config |
| `src/PBA.Infrastructure/Services/Scrapers/GitHubScraper.cs` (new) | GitHub releases/events scraper |
| `src/PBA.Infrastructure/Services/SourcePollingService.cs` (new, replaces RssPollingService) | Type-dispatching poller |
| `src/PBA.Infrastructure/Services/RssPollingService.cs` (delete) | Removed (replaced) |
| `src/PBA.Infrastructure/DependencyInjection.cs` (modify) | Register keyed scrapers, options, hosted service |
| `src/PBA.Api/appsettings.json` (modify) | `HackerNews` + `GitHubScraper` sections |
| Tests under `tests/PBA.Infrastructure.Tests/Services/Scrapers/` + rename polling test | |

Run backend tests with: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "<Name>"` from repo root.

Reference types (already exist):
- `RssFeedItem(string Title, string? Description, string? Url, string? ThumbnailUrl, string? Category, DateTimeOffset PublishedAt)` in `PBA.Application/Common/Interfaces`.
- `IRssFeedReader.ReadFeedAsync(string feedUrl, CancellationToken) : Task<List<RssFeedItem>>`.
- `DeduplicationKeyGenerator.Generate(string? url, string title)` in `PBA.Application.Common`.
- `IdeaSource { Guid Id; string Name; IdeaSourceType Type; string? FeedUrl; string? ApiUrl; string Category; ... LastSuccessAt; ConsecutiveFailures; ... }`.
- `Idea { string Title; string? Description; string? Url; string SourceName; Guid? IdeaSourceId; string? ThumbnailUrl; string? Category; List<string> Tags; IdeaStatus Status; DateTimeOffset DetectedAt; string DeduplicationKey; }`.
- `RssPollingOptions { int PollIntervalMinutes=30; int MaxConsecutiveFailures=5; }` (reused as-is).

---

## Task 1: Add HackerNews + GitHub to IdeaSourceType

**Files:**
- Modify: `src/PBA.Domain/Enums/IdeaSourceType.cs`

- [ ] **Step 1: Append the enum values**

Replace the enum body so it reads (append at END to keep existing int values stable):

```csharp
namespace PBA.Domain.Enums;

public enum IdeaSourceType
{
    RSS,
    Twitter,
    LinkedIn,
    API,
    HackerNews,
    GitHub
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/PBA.Domain/PBA.Domain.csproj -v q --nologo`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Domain/Enums/IdeaSourceType.cs
git commit -m "feat: add HackerNews and GitHub idea source types"
```

---

## Task 2: ScrapedItem DTO + ISourceScraper interface

**Files:**
- Create: `src/PBA.Application/Common/Interfaces/ScrapedItem.cs`
- Create: `src/PBA.Application/Common/Interfaces/ISourceScraper.cs`

- [ ] **Step 1: Create `ScrapedItem.cs`**

```csharp
namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Source-neutral item produced by an <see cref="ISourceScraper"/> before it becomes an Idea.
/// Url is the canonical link used both for display and deduplication.
/// </summary>
public record ScrapedItem(
    string Title,
    string? Description,
    string? Url,
    string? ThumbnailUrl,
    DateTimeOffset PublishedAt);
```

- [ ] **Step 2: Create `ISourceScraper.cs`**

```csharp
using PBA.Domain.Entities;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Fetches new items for a single idea source. Implementations are registered with keyed DI
/// keyed by <see cref="PBA.Domain.Enums.IdeaSourceType"/>. Must never throw for an expected
/// failure (network, parse) — return an empty list and let the caller record the failure.
/// </summary>
public interface ISourceScraper
{
    Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/PBA.Application/PBA.Application.csproj -v q --nologo`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/PBA.Application/Common/Interfaces/ScrapedItem.cs src/PBA.Application/Common/Interfaces/ISourceScraper.cs
git commit -m "feat: add ScrapedItem DTO and ISourceScraper interface"
```

---

## Task 3: RssScraper (wraps IRssFeedReader)

**Files:**
- Create: `src/PBA.Infrastructure/Services/Scrapers/RssScraper.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Scrapers/RssScraperTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Services.Scrapers;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Scrapers;

public class RssScraperTests
{
    private static IdeaSource Source() => new()
    { Name = "Blog", Type = IdeaSourceType.RSS, FeedUrl = "https://x/feed", Category = "Tech" };

    [Fact]
    public async Task FetchAsync_MapsFeedItemsToScrapedItems_AndIgnoresSince()
    {
        var reader = new Mock<IRssFeedReader>();
        reader.Setup(r => r.ReadFeedAsync("https://x/feed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>
            {
                new("Title A", "Desc A", "https://x/a", "https://x/thumb", "Tech",
                    new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            });
        var scraper = new RssScraper(reader.Object, NullLogger<RssScraper>.Instance);

        // since is in the future on purpose; RSS must ignore it and still return the item
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UtcNow.AddYears(10), CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Title A", item.Title);
        Assert.Equal("https://x/a", item.Url);
        Assert.Equal("Desc A", item.Description);
        Assert.Equal("https://x/thumb", item.ThumbnailUrl);
    }

    [Fact]
    public async Task FetchAsync_NoFeedUrl_ReturnsEmpty()
    {
        var reader = new Mock<IRssFeedReader>();
        var scraper = new RssScraper(reader.Object, NullLogger<RssScraper>.Instance);
        var items = await scraper.FetchAsync(new IdeaSource { Name = "x", Type = IdeaSourceType.RSS, Category = "" },
            DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(items);
        reader.Verify(r => r.ReadFeedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~RssScraperTests" --nologo`
Expected: FAIL — `RssScraper` does not exist.

- [ ] **Step 3: Implement `RssScraper.cs`**

```csharp
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Services.Scrapers;

/// <summary>RSS source scraper. Reads the whole feed and ignores <c>since</c> (dedup handles repeats),
/// preserving the original RSS polling behavior.</summary>
public sealed class RssScraper(IRssFeedReader reader, ILogger<RssScraper> logger) : ISourceScraper
{
    public async Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source.FeedUrl))
            return [];

        var entries = await reader.ReadFeedAsync(source.FeedUrl, ct);
        return entries
            .Select(e => new ScrapedItem(e.Title, e.Description, e.Url, e.ThumbnailUrl, e.PublishedAt))
            .ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~RssScraperTests" --nologo`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Scrapers/RssScraper.cs tests/PBA.Infrastructure.Tests/Services/Scrapers/RssScraperTests.cs
git commit -m "feat: add RssScraper wrapping IRssFeedReader"
```

---

## Task 4: HackerNewsScraper

**Files:**
- Create: `src/PBA.Infrastructure/Configuration/HackerNewsOptions.cs`
- Create: `src/PBA.Infrastructure/Services/Scrapers/HackerNewsScraper.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Scrapers/HackerNewsScraperTests.cs`

- [ ] **Step 1: Create `HackerNewsOptions.cs`**

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";
    public int MinScore { get; init; } = 100;
    public int FetchTopStories { get; init; } = 30;
    public int TopComments { get; init; } = 5;
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Scrapers;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Scrapers;

public class HackerNewsScraperTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();

    private HackerNewsScraper Build(HackerNewsOptions opts)
    {
        var http = new HttpClient(_handler.Object);
        return new HackerNewsScraper(http, Options.Create(opts), NullLogger<HackerNewsScraper>.Instance);
    }

    private void Route(Func<string, string?> responder)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var body = responder(req.RequestUri!.ToString());
                return body is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            });
    }

    private static IdeaSource Source() => new() { Name = "HN", Type = IdeaSourceType.HackerNews, Category = "Tech" };

    [Fact]
    public async Task FetchAsync_ReturnsStoriesAtOrAboveMinScore_WithCommentsFolded()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Route(url =>
        {
            if (url.Contains("topstories")) return "[1, 2]";
            if (url.EndsWith("/item/1.json"))
                return $"{{\"id\":1,\"type\":\"story\",\"title\":\"Big AI\",\"url\":\"https://ex/ai\",\"score\":250,\"time\":{now},\"kids\":[11]}}";
            if (url.EndsWith("/item/2.json"))
                return $"{{\"id\":2,\"type\":\"story\",\"title\":\"Low\",\"url\":\"https://ex/low\",\"score\":10,\"time\":{now}}}";
            if (url.EndsWith("/item/11.json"))
                return "{\"id\":11,\"type\":\"comment\",\"text\":\"great insight\",\"by\":\"alice\"}";
            return null;
        });
        var scraper = Build(new HackerNewsOptions { MinScore = 100, FetchTopStories = 30, TopComments = 5 });

        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(items);                 // story 2 filtered out (score 10 < 100)
        Assert.Equal("Big AI", item.Title);
        Assert.Equal("https://ex/ai", item.Url);
        Assert.Contains("great insight", item.Description); // top comment folded in
    }

    [Fact]
    public async Task FetchAsync_FiltersStoriesOlderThanSince()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();
        Route(url =>
        {
            if (url.Contains("topstories")) return "[1]";
            if (url.EndsWith("/item/1.json"))
                return $"{{\"id\":1,\"type\":\"story\",\"title\":\"Old\",\"url\":\"https://ex/o\",\"score\":500,\"time\":{old}}}";
            return null;
        });
        var scraper = Build(new HackerNewsOptions { MinScore = 100 });
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        Assert.Empty(items);
    }

    [Fact]
    public async Task FetchAsync_SelfPost_UsesHnDiscussionUrl()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Route(url =>
        {
            if (url.Contains("topstories")) return "[5]";
            if (url.EndsWith("/item/5.json"))
                return $"{{\"id\":5,\"type\":\"story\",\"title\":\"Ask HN\",\"score\":150,\"time\":{now},\"text\":\"question?\"}}";
            return null;
        });
        var scraper = Build(new HackerNewsOptions { MinScore = 100 });
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal("https://news.ycombinator.com/item?id=5", Assert.Single(items).Url);
    }

    [Fact]
    public async Task FetchAsync_HttpError_ReturnsEmpty()
    {
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));
        var scraper = Build(new HackerNewsOptions());
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Empty(items);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~HackerNewsScraperTests" --nologo`
Expected: FAIL — `HackerNewsScraper` does not exist.

- [ ] **Step 4: Implement `HackerNewsScraper.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Services.Scrapers;

/// <summary>Scrapes top Hacker News stories (Firebase API, no auth) above a score threshold,
/// folding a few top comments into the description for content-angle context.</summary>
public sealed class HackerNewsScraper(
    HttpClient http,
    IOptions<HackerNewsOptions> options,
    ILogger<HackerNewsScraper> logger) : ISourceScraper
{
    private const string BaseUrl = "https://hacker-news.firebaseio.com/v0";
    private readonly HackerNewsOptions _options = options.Value;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default)
    {
        try
        {
            var ids = await http.GetFromJsonOrNullAsync<long[]>($"{BaseUrl}/topstories.json", ct)
                      ?? [];
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

                var description = BuildDescription(story.Text, comments);
                items.Add(new ScrapedItem(story.Title ?? "(untitled)", description, url, null, publishedAt));
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
        try { return await http.GetFromJsonOrNullAsync<HnItem>($"{BaseUrl}/item/{id}.json", ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { return null; }
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
```

Also create a small extension used above (shared helper). Create `src/PBA.Infrastructure/Services/Scrapers/HttpJsonExtensions.cs`:

```csharp
using System.Net.Http.Json;

namespace PBA.Infrastructure.Services.Scrapers;

internal static class HttpJsonExtensions
{
    /// <summary>GET + deserialize; returns null on a non-success status instead of throwing.</summary>
    public static async Task<T?> GetFromJsonOrNullAsync<T>(this HttpClient http, string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(ct);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~HackerNewsScraperTests" --nologo`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/PBA.Infrastructure/Configuration/HackerNewsOptions.cs src/PBA.Infrastructure/Services/Scrapers/HackerNewsScraper.cs src/PBA.Infrastructure/Services/Scrapers/HttpJsonExtensions.cs tests/PBA.Infrastructure.Tests/Services/Scrapers/HackerNewsScraperTests.cs
git commit -m "feat: add Hacker News scraper"
```

---

## Task 5: GitHubScraper

**Files:**
- Create: `src/PBA.Infrastructure/Configuration/GitHubScraperOptions.cs`
- Create: `src/PBA.Infrastructure/Services/Scrapers/GitHubScraper.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Scrapers/GitHubScraperTests.cs`

- [ ] **Step 1: Create `GitHubScraperOptions.cs`**

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class GitHubScraperOptions
{
    public const string SectionName = "GitHubScraper";
    /// <summary>Optional PAT; bound from config/env (GitHubScraper:Token or GITHUB_TOKEN). Raises rate limit.</summary>
    public string Token { get; init; } = "";
    public int MaxEventsPerSource { get; init; } = 30;
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Scrapers;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Scrapers;

public class GitHubScraperTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly List<HttpRequestMessage> _requests = new();

    private GitHubScraper Build(GitHubScraperOptions opts)
    {
        var http = new HttpClient(_handler.Object) { BaseAddress = new Uri("https://api.github.com") };
        return new GitHubScraper(http, Options.Create(opts), NullLogger<GitHubScraper>.Instance);
    }

    private void Route(Func<string, string?> responder)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var body = responder(req.RequestUri!.AbsolutePath);
                return body is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            });
    }

    private static IdeaSource Repo(string apiUrl) => new()
    { Name = "gh", Type = IdeaSourceType.GitHub, ApiUrl = apiUrl, Category = "Dev" };

    [Fact]
    public async Task FetchAsync_RepoReleases_MapsItems()
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        Route(path => path == "/repos/dotnet/runtime/releases"
            ? $"[{{\"html_url\":\"https://github.com/dotnet/runtime/releases/tag/v9\",\"name\":\"v9\",\"tag_name\":\"v9\",\"body\":\"notes\",\"published_at\":\"{now}\"}}]"
            : null);
        var scraper = Build(new GitHubScraperOptions());

        var items = await scraper.FetchAsync(Repo("github:repo:dotnet/runtime"), DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Contains("v9", item.Title);
        Assert.Equal("https://github.com/dotnet/runtime/releases/tag/v9", item.Url);
    }

    [Fact]
    public async Task FetchAsync_UserEvents_MapsItems()
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        Route(path => path == "/users/octocat/events/public"
            ? $"[{{\"id\":\"42\",\"type\":\"PushEvent\",\"created_at\":\"{now}\",\"repo\":{{\"name\":\"octocat/hello\"}}}}]"
            : null);
        var scraper = Build(new GitHubScraperOptions());

        var items = await scraper.FetchAsync(Repo("github:user:octocat"), DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Contains("octocat/hello", item.Title);
        Assert.Equal("https://github.com/octocat/hello", item.Url);
    }

    [Fact]
    public async Task FetchAsync_MalformedApiUrl_ReturnsEmpty()
    {
        Route(_ => "[]");
        var scraper = Build(new GitHubScraperOptions());
        Assert.Empty(await scraper.FetchAsync(Repo("not-a-github-url"), DateTimeOffset.UnixEpoch, CancellationToken.None));
        Assert.Empty(_requests); // never made a call
    }

    [Fact]
    public async Task FetchAsync_WithToken_SendsAuthHeader()
    {
        Route(_ => "[]");
        var scraper = Build(new GitHubScraperOptions { Token = "ghp_secret" });
        await scraper.FetchAsync(Repo("github:repo:a/b"), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Contains(_requests, r => r.Headers.Authorization?.Parameter == "ghp_secret");
    }

    [Fact]
    public async Task FetchAsync_NoToken_NoAuthHeader()
    {
        Route(_ => "[]");
        var scraper = Build(new GitHubScraperOptions { Token = "" });
        await scraper.FetchAsync(Repo("github:repo:a/b"), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.All(_requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task FetchAsync_FiltersOlderThanSince()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-30).ToString("o");
        Route(path => path == "/repos/a/b/releases"
            ? $"[{{\"html_url\":\"https://github.com/a/b/releases/tag/v1\",\"name\":\"v1\",\"tag_name\":\"v1\",\"body\":\"\",\"published_at\":\"{old}\"}}]"
            : null);
        var scraper = Build(new GitHubScraperOptions());
        var items = await scraper.FetchAsync(Repo("github:repo:a/b"), DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        Assert.Empty(items);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GitHubScraperTests" --nologo`
Expected: FAIL — `GitHubScraper` does not exist.

- [ ] **Step 4: Implement `GitHubScraper.cs`**

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Services.Scrapers;

/// <summary>Scrapes GitHub repo releases or user public events. The watched target comes from
/// <c>source.ApiUrl</c> as <c>github:repo:owner/name</c> or <c>github:user:username</c>; the value is
/// parsed into path segments only — it is never used as a raw request URL (no SSRF surface).</summary>
public sealed class GitHubScraper(
    HttpClient http,
    IOptions<GitHubScraperOptions> options,
    ILogger<GitHubScraper> logger) : ISourceScraper
{
    private readonly GitHubScraperOptions _options = options.Value;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

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
        [System.Text.Json.Serialization.JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")] public string? TagName { get; set; }
        public string? Body { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; set; }
    }

    private sealed class GhEvent
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        public GhRepo? Repo { get; set; }
    }

    private sealed class GhRepo { public string? Name { get; set; } }
}
```

Add to `HttpJsonExtensions.cs` (created in Task 4) a safe content reader:

```csharp
    public static async Task<T?> ReadFromJsonAsyncSafe<T>(this HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadFromJsonAsync<T>(ct); }
        catch (System.Text.Json.JsonException) { return default; }
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GitHubScraperTests" --nologo`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add src/PBA.Infrastructure/Configuration/GitHubScraperOptions.cs src/PBA.Infrastructure/Services/Scrapers/GitHubScraper.cs src/PBA.Infrastructure/Services/Scrapers/HttpJsonExtensions.cs tests/PBA.Infrastructure.Tests/Services/Scrapers/GitHubScraperTests.cs
git commit -m "feat: add GitHub scraper (repo releases + user events)"
```

---

## Task 6: Generalize RssPollingService → SourcePollingService

**Files:**
- Create: `src/PBA.Infrastructure/Services/SourcePollingService.cs`
- Delete: `src/PBA.Infrastructure/Services/RssPollingService.cs`
- Rename/rewrite test: `tests/PBA.Infrastructure.Tests/Services/RssPollingServiceTests.cs` → `SourcePollingServiceTests.cs`

- [ ] **Step 1: Read the existing `RssPollingService.cs` and `RssPollingServiceTests.cs`**

Note the existing dedup/Idea-creation/failure logic and the test harness (mock scope factory + in-memory DB). The new service keeps all of it; only the per-source fetch changes from `IRssFeedReader` to a keyed `ISourceScraper`, and it polls ALL enabled sources (not just RSS).

- [ ] **Step 2: Write the failing test `SourcePollingServiceTests.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class SourcePollingServiceTests
{
    private static (SourcePollingService svc, ApplicationDbContext db) Build(
        Dictionary<IdeaSourceType, ISourceScraper> scrapers, RssPollingOptions? opts = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        var keyed = provider.As<IKeyedServiceProvider>();
        foreach (var (type, scraper) in scrapers)
            keyed.Setup(p => p.GetKeyedService(typeof(ISourceScraper), type)).Returns(scraper);
        // unregistered types return null
        keyed.Setup(p => p.GetKeyedService(typeof(ISourceScraper),
            It.Is<object>(o => !scrapers.ContainsKey((IdeaSourceType)o)))).Returns(null!);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var monitor = new Mock<IOptionsMonitor<RssPollingOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(opts ?? new RssPollingOptions());

        return (new SourcePollingService(factory.Object, monitor.Object, NullLogger<SourcePollingService>.Instance), db);
    }

    private static ISourceScraper StubScraper(params ScrapedItem[] items)
    {
        var m = new Mock<ISourceScraper>();
        m.Setup(s => s.FetchAsync(It.IsAny<IdeaSource>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        return m.Object;
    }

    private static IdeaSource Src(IdeaSourceType type, string name = "s") =>
        new() { Name = name, Type = type, Category = "C", IsEnabled = true, FeedUrl = "https://x/f" };

    [Fact]
    public async Task PollAsync_DispatchesByType_AndCreatesIdeas()
    {
        var item = new ScrapedItem("HN Story", "desc", "https://ex/1", null, DateTimeOffset.UtcNow);
        var (svc, db) = Build(new() { [IdeaSourceType.HackerNews] = StubScraper(item) });
        db.IdeaSources.Add(Src(IdeaSourceType.HackerNews, "HN"));
        await db.SaveChangesAsync();

        await svc.PollAsync(CancellationToken.None);

        var idea = Assert.Single(db.Ideas);
        Assert.Equal("HN Story", idea.Title);
        Assert.Equal("HN", idea.SourceName);
        Assert.Equal("C", idea.Category);
    }

    [Fact]
    public async Task PollAsync_DedupsAcrossPolls()
    {
        var item = new ScrapedItem("Dup", "d", "https://ex/same", null, DateTimeOffset.UtcNow);
        var (svc, db) = Build(new() { [IdeaSourceType.HackerNews] = StubScraper(item) });
        db.IdeaSources.Add(Src(IdeaSourceType.HackerNews));
        await db.SaveChangesAsync();

        await svc.PollAsync(CancellationToken.None);
        await svc.PollAsync(CancellationToken.None);

        Assert.Single(db.Ideas); // second poll deduped
    }

    [Fact]
    public async Task PollAsync_NoScraperForType_SkipsWithoutThrowing()
    {
        var (svc, db) = Build(new()); // no scrapers registered
        db.IdeaSources.Add(Src(IdeaSourceType.GitHub));
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => svc.PollAsync(CancellationToken.None));
        Assert.Null(ex);
        Assert.Empty(db.Ideas);
    }

    [Fact]
    public async Task PollAsync_ScraperThrows_IncrementsFailureAndDisablesAtThreshold()
    {
        var throwing = new Mock<ISourceScraper>();
        throwing.Setup(s => s.FetchAsync(It.IsAny<IdeaSource>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var (svc, db) = Build(new() { [IdeaSourceType.HackerNews] = throwing.Object },
            new RssPollingOptions { MaxConsecutiveFailures = 1 });
        db.IdeaSources.Add(Src(IdeaSourceType.HackerNews));
        await db.SaveChangesAsync();

        await svc.PollAsync(CancellationToken.None);

        var src = Assert.Single(db.IdeaSources);
        Assert.False(src.IsEnabled);          // auto-disabled at threshold
        Assert.Equal(1, src.ConsecutiveFailures);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SourcePollingServiceTests" --nologo`
Expected: FAIL — `SourcePollingService` does not exist.

- [ ] **Step 4: Create `SourcePollingService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services;

/// <summary>Polls every enabled idea source, dispatching to the scraper registered (keyed DI) for the
/// source's type, then dedups and creates Ideas. Generalizes the former RSS-only polling service.</summary>
public class SourcePollingService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RssPollingOptions> options,
    ILogger<SourcePollingService> logger) : BackgroundService
{
    private const int LookbackHours = 48;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(options.CurrentValue.PollIntervalMinutes), stoppingToken);
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Source polling cycle failed");
            }
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sources = await db.IdeaSources.Where(s => s.IsEnabled).ToListAsync(ct);
        if (sources.Count == 0) return;

        var existingKeys = await db.Ideas
            .Where(i => i.DeduplicationKey != "")
            .Select(i => i.DeduplicationKey)
            .ToHashSetAsync(ct);

        var maxFailures = options.CurrentValue.MaxConsecutiveFailures;

        foreach (var source in sources)
        {
            var scraper = scope.ServiceProvider.GetKeyedService<ISourceScraper>(source.Type);
            if (scraper is null)
            {
                logger.LogWarning("No scraper registered for source type {Type} ({Name})", source.Type, source.Name);
                continue;
            }

            try
            {
                var since = source.LastSuccessAt ?? DateTimeOffset.UtcNow.AddHours(-LookbackHours);
                var items = await scraper.FetchAsync(source, since, ct);

                var newCount = 0;
                foreach (var item in items)
                {
                    var dedupKey = DeduplicationKeyGenerator.Generate(item.Url, item.Title);
                    if (!existingKeys.Add(dedupKey)) continue;

                    db.Ideas.Add(new Idea
                    {
                        Title = item.Title,
                        Description = Truncate(item.Description, 2000),
                        Url = item.Url,
                        SourceName = source.Name,
                        IdeaSourceId = source.Id,
                        ThumbnailUrl = item.ThumbnailUrl,
                        Category = source.Category,
                        Tags = [],
                        Status = IdeaStatus.New,
                        DetectedAt = item.PublishedAt,
                        DeduplicationKey = dedupKey,
                    });
                    newCount++;
                }

                source.LastPolledAt = DateTimeOffset.UtcNow;
                source.LastSuccessAt = DateTimeOffset.UtcNow;
                source.ConsecutiveFailures = 0;
                source.LastError = null;
                if (newCount > 0)
                    logger.LogInformation("Source {Name}: {Count} new ideas", source.Name, newCount);
            }
            catch (Exception ex)
            {
                source.ConsecutiveFailures++;
                source.LastError = ex.Message;
                source.LastPolledAt = DateTimeOffset.UtcNow;
                if (source.ConsecutiveFailures >= maxFailures)
                {
                    source.IsEnabled = false;
                    logger.LogError("Source {Name} disabled after {Count} consecutive failures",
                        source.Name, source.ConsecutiveFailures);
                }
                else
                {
                    logger.LogWarning(ex, "Source {Name} poll failed ({Count}/{Max})",
                        source.Name, source.ConsecutiveFailures, maxFailures);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? content, int maxLength) =>
        string.IsNullOrEmpty(content) || content.Length <= maxLength ? content : content[..maxLength];
}
```

- [ ] **Step 5: Delete the old service and its test**

```bash
git rm src/PBA.Infrastructure/Services/RssPollingService.cs tests/PBA.Infrastructure.Tests/Services/RssPollingServiceTests.cs
```
(If `RssPollingServiceTests.cs` covered RSS-specific feed parsing that now lives in `RssFeedReaderTests`/`RssScraperTests`, that coverage is preserved there. The dedup/failure logic is now covered by `SourcePollingServiceTests`.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SourcePollingServiceTests" --nologo`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add -A src/PBA.Infrastructure/Services tests/PBA.Infrastructure.Tests/Services/SourcePollingServiceTests.cs
git commit -m "refactor: generalize RssPollingService into type-dispatching SourcePollingService"
```

---

## Task 7: DI registration + appsettings

**Files:**
- Modify: `src/PBA.Infrastructure/DependencyInjection.cs`
- Modify: `src/PBA.Api/appsettings.json`

- [ ] **Step 1: Update DI registration**

In `DependencyInjection.cs`, find the RSS block (around lines 39-49):
```csharp
        services.Configure<RssPollingOptions>(configuration.GetSection(RssPollingOptions.SectionName));
        services.AddHttpClient<RssFeedReader>(client => { /* existing UA config */ });
        services.AddScoped<IRssFeedReader, RssFeedReader>();
        services.AddHostedService<RssPollingService>();
```
Replace the `AddHostedService<RssPollingService>()` line and add scraper registrations. The final block should be:
```csharp
        services.Configure<RssPollingOptions>(configuration.GetSection(RssPollingOptions.SectionName));
        services.Configure<HackerNewsOptions>(configuration.GetSection(HackerNewsOptions.SectionName));
        services.Configure<GitHubScraperOptions>(configuration.GetSection(GitHubScraperOptions.SectionName));

        services.AddHttpClient<RssFeedReader>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        });
        services.AddScoped<IRssFeedReader, RssFeedReader>();

        // Keyed source scrapers (one per IdeaSourceType). SourcePollingService dispatches by type.
        services.AddScoped<PBA.Infrastructure.Services.Scrapers.RssScraper>();
        services.AddKeyedScoped<ISourceScraper>(IdeaSourceType.RSS,
            (sp, _) => sp.GetRequiredService<PBA.Infrastructure.Services.Scrapers.RssScraper>());
        services.AddHttpClient<PBA.Infrastructure.Services.Scrapers.HackerNewsScraper>();
        services.AddKeyedScoped<ISourceScraper>(IdeaSourceType.HackerNews,
            (sp, _) => sp.GetRequiredService<PBA.Infrastructure.Services.Scrapers.HackerNewsScraper>());
        services.AddHttpClient<PBA.Infrastructure.Services.Scrapers.GitHubScraper>(client =>
        {
            client.BaseAddress = new Uri("https://api.github.com");
        });
        services.AddKeyedScoped<ISourceScraper>(IdeaSourceType.GitHub,
            (sp, _) => sp.GetRequiredService<PBA.Infrastructure.Services.Scrapers.GitHubScraper>());

        services.AddHostedService<SourcePollingService>();
```
Ensure `using PBA.Domain.Enums;` is present (it is — already used in the file for `Platform`/keyed registrations; add it if missing).

Note on `HackerNewsScraper` HttpClient: it uses absolute URLs, so no BaseAddress is required.

- [ ] **Step 2: Add appsettings sections**

In `src/PBA.Api/appsettings.json`, add after the `DigestDelivery` block (sibling key):
```jsonc
  "HackerNews": { "MinScore": 100, "FetchTopStories": 30, "TopComments": 5 },
  "GitHubScraper": { "Token": "", "MaxEventsPerSource": 30 }
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build -v q --nologo`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PBA.Infrastructure/DependencyInjection.cs src/PBA.Api/appsettings.json
git commit -m "feat: register keyed source scrapers and SourcePollingService"
```

---

## Task 8: Full verification + deploy

- [ ] **Step 1: Run the full Infrastructure test suite**

Run: `dotnet test tests/PBA.Infrastructure.Tests/PBA.Infrastructure.Tests.csproj --nologo`
Expected: all tests PASS (existing + new scraper/polling tests).

- [ ] **Step 2: Run the whole backend solution test suite**

Run: `dotnet test --nologo`
Expected: all projects green.

- [ ] **Step 3: Deploy to Mac Mini (api only — no web change, no DB migration)**

```bash
git push origin v2-rebuild
ssh matthewkruczek@matthews-mac-mini.tail2800e3.ts.net 'cd ~/personal-brand-assistant && git fetch origin v2-rebuild && git merge --ff-only origin/v2-rebuild && /usr/local/bin/docker compose up -d --build api'
```
Then verify: `curl -s http://localhost:5001/api/health` returns healthy and `docker logs pba-api --since 90s` shows no scraper errors (and "No scraper registered" only if a stray source type exists).

- [ ] **Step 4 (manual, optional): add a source to smoke-test**

Create one HN source and one GitHub source via the API (POST `/api/idea-sources`) — HN: `{name:"Hacker News", type:"HackerNews", category:"AI"}`; GitHub: `{name:"dotnet/runtime", type:"GitHub", apiUrl:"github:repo:dotnet/runtime", category:"Dev"}`. Watch the next poll cycle create ideas. (Optional because the feature ships inert.)

---

## Self-review notes

- **Spec coverage:** ISourceScraper + ScrapedItem (T2); RssScraper ignores `since` (T3); HackerNewsScraper story+comments+score/since filter+self-post URL+error handling (T4); GitHubScraper releases+events+ApiUrl parse+token header+since (T5); SourcePollingService dispatch/dedup/failure/skip-unregistered (T6); enum values (T1); keyed DI + options + secret token + appsettings (T7); verification + inert rollout + deploy (T8). All spec sections mapped.
- **Type consistency:** `ISourceScraper.FetchAsync(IdeaSource, DateTimeOffset, CancellationToken)` and `ScrapedItem(Title, Description?, Url?, ThumbnailUrl?, PublishedAt)` used identically across T3/T4/T5/T6. `RssPollingOptions` reused (PollIntervalMinutes/MaxConsecutiveFailures). Keyed lookup via `GetKeyedService<ISourceScraper>(source.Type)` matches registrations in T7. `HttpJsonExtensions` helpers (`GetFromJsonOrNullAsync`, `ReadFromJsonAsyncSafe`) defined in T4/T5 and used in T4/T5.
- **No DB migration:** `IdeaSourceType` persists as int; new values appended last (T1).
- **Secret:** GitHub token bound from `GitHubScraper:Token` (empty default in appsettings); set via env/user-secrets in deployment, never committed.
