diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs
new file mode 100644
index 0000000..4b6f5ec
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs
@@ -0,0 +1,12 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface ITrendMonitor
+{
+    Task<Result<IReadOnlyList<TrendSuggestion>>> GetSuggestionsAsync(int limit, CancellationToken ct);
+    Task<Result<MediatR.Unit>> DismissSuggestionAsync(Guid suggestionId, CancellationToken ct);
+    Task<Result<Guid>> AcceptSuggestionAsync(Guid suggestionId, CancellationToken ct);
+    Task<Result<MediatR.Unit>> RefreshTrendsAsync(CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/TrendMonitoringOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/TrendMonitoringOptions.cs
new file mode 100644
index 0000000..7712482
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/TrendMonitoringOptions.cs
@@ -0,0 +1,17 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class TrendMonitoringOptions
+{
+    public const string SectionName = "TrendMonitoring";
+    public int AggregationIntervalMinutes { get; set; } = 30;
+    public string TrendRadarApiUrl { get; set; } = "http://trendradar:8000/api";
+    public string FreshRssApiUrl { get; set; } = "http://freshrss:80/api";
+    public string[] RedditSubreddits { get; set; } = ["programming", "dotnet", "webdev"];
+    public string HackerNewsApiUrl { get; set; } = "https://hacker-news.firebaseio.com/v0";
+    public float RelevanceScoreThreshold { get; set; } = 0.6f;
+    public float TitleSimilarityThreshold { get; set; } = 0.85f;
+    public int MaxSuggestionsPerCycle { get; set; } = 10;
+    public int MaxAutoAcceptPerCycle { get; set; } = 1;
+    public int HackerNewsTopN { get; set; } = 30;
+    public int HackerNewsConcurrency { get; set; } = 5;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TrendAggregationProcessor.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TrendAggregationProcessor.cs
new file mode 100644
index 0000000..515c8dd
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TrendAggregationProcessor.cs
@@ -0,0 +1,68 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class TrendAggregationProcessor : BackgroundService
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly TrendMonitoringOptions _options;
+    private readonly ILogger<TrendAggregationProcessor> _logger;
+
+    public TrendAggregationProcessor(
+        IServiceScopeFactory scopeFactory,
+        IOptions<TrendMonitoringOptions> options,
+        ILogger<TrendAggregationProcessor> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        var interval = TimeSpan.FromMinutes(_options.AggregationIntervalMinutes);
+        using var timer = new PeriodicTimer(interval);
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await ProcessCycleAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during trend aggregation cycle");
+            }
+        }
+    }
+
+    internal async Task ProcessCycleAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var trendMonitor = scope.ServiceProvider.GetRequiredService<ITrendMonitor>();
+
+        try
+        {
+            var result = await trendMonitor.RefreshTrendsAsync(ct);
+
+            if (!result.IsSuccess)
+            {
+                _logger.LogWarning("Trend refresh failed: {Errors}",
+                    string.Join(", ", result.Errors));
+            }
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Unhandled error in trend aggregation cycle");
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendDeduplicator.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendDeduplicator.cs
new file mode 100644
index 0000000..bc0d5e8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendDeduplicator.cs
@@ -0,0 +1,151 @@
+using System.Security.Cryptography;
+using System.Text;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+internal static class TrendDeduplicator
+{
+    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
+    {
+        "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term",
+        "ref", "source", "fbclid", "gclid",
+    };
+
+    public static string CanonicalizeUrl(string url)
+    {
+        if (string.IsNullOrWhiteSpace(url))
+            return string.Empty;
+
+        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
+            return url.Trim().ToLowerInvariant();
+
+        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
+        var keysToRemove = query.AllKeys
+            .Where(k => k is not null && (TrackingParams.Contains(k) || k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)))
+            .ToList();
+
+        foreach (var key in keysToRemove)
+            query.Remove(key);
+
+        var builder = new UriBuilder(uri)
+        {
+            Query = query.Count > 0 ? query.ToString() : string.Empty,
+            Fragment = string.Empty,
+        };
+
+        var canonical = builder.Uri.GetLeftPart(UriPartial.Query).ToLowerInvariant();
+        return canonical.TrimEnd('/');
+    }
+
+    public static string ComputeDeduplicationKey(string canonicalUrl)
+    {
+        if (string.IsNullOrWhiteSpace(canonicalUrl))
+            return string.Empty;
+
+        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl));
+        return Convert.ToHexStringLower(bytes);
+    }
+
+    public static float ComputeTitleSimilarity(string title1, string title2)
+    {
+        if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
+            return 0f;
+
+        var words1 = Tokenize(title1);
+        var words2 = Tokenize(title2);
+
+        if (words1.Count == 0 && words2.Count == 0)
+            return 1f;
+        if (words1.Count == 0 || words2.Count == 0)
+            return 0f;
+
+        var intersection = words1.Intersect(words2).Count();
+        var union = words1.Union(words2).Count();
+
+        return union == 0 ? 0f : (float)intersection / union;
+    }
+
+    public static IReadOnlyList<TrendItem> Deduplicate(
+        IEnumerable<TrendItem> items, float titleSimilarityThreshold)
+    {
+        var itemList = items.ToList();
+        var result = new List<TrendItem>();
+        var usedIndices = new HashSet<int>();
+
+        // First pass: group by URL deduplication key
+        var urlGroups = new Dictionary<string, List<int>>();
+        for (var i = 0; i < itemList.Count; i++)
+        {
+            var item = itemList[i];
+            if (!string.IsNullOrWhiteSpace(item.Url))
+            {
+                var canonical = CanonicalizeUrl(item.Url);
+                var key = ComputeDeduplicationKey(canonical);
+                item.DeduplicationKey = key;
+
+                if (!urlGroups.TryGetValue(key, out var group))
+                {
+                    group = [];
+                    urlGroups[key] = group;
+                }
+                group.Add(i);
+            }
+        }
+
+        // Merge URL-based duplicates: keep earliest DetectedAt
+        foreach (var group in urlGroups.Values)
+        {
+            var best = group.OrderBy(i => itemList[i].DetectedAt).First();
+            result.Add(itemList[best]);
+            foreach (var idx in group)
+                usedIndices.Add(idx);
+        }
+
+        // Second pass: items without URLs, compare titles against existing results
+        for (var i = 0; i < itemList.Count; i++)
+        {
+            if (usedIndices.Contains(i))
+                continue;
+
+            var item = itemList[i];
+            var isDuplicate = false;
+
+            foreach (var existing in result)
+            {
+                if (ComputeTitleSimilarity(item.Title, existing.Title) >= titleSimilarityThreshold)
+                {
+                    isDuplicate = true;
+                    break;
+                }
+            }
+
+            if (!isDuplicate)
+            {
+                // Also check against remaining unprocessed items with URLs via title similarity
+                foreach (var existing in result)
+                {
+                    if (ComputeTitleSimilarity(item.Title, existing.Title) >= titleSimilarityThreshold)
+                    {
+                        isDuplicate = true;
+                        break;
+                    }
+                }
+
+                if (!isDuplicate)
+                    result.Add(item);
+            }
+        }
+
+        return result;
+    }
+
+    private static HashSet<string> Tokenize(string text)
+    {
+        return text
+            .ToLowerInvariant()
+            .Split([' ', '\t', '\n', '\r', ',', '.', '!', '?', ':', ';', '-', '(', ')', '[', ']'],
+                StringSplitOptions.RemoveEmptyEntries)
+            .ToHashSet();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendMonitor.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendMonitor.cs
new file mode 100644
index 0000000..c4fa07d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendMonitor.cs
@@ -0,0 +1,575 @@
+using System.Text.Json;
+using System.Text.Json.Serialization;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+public sealed class TrendMonitor : ITrendMonitor
+{
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNameCaseInsensitive = true,
+    };
+
+    private readonly IApplicationDbContext _dbContext;
+    private readonly ISidecarClient _sidecar;
+    private readonly IHttpClientFactory _httpClientFactory;
+    private readonly TrendMonitoringOptions _options;
+    private readonly ILogger<TrendMonitor> _logger;
+
+    public TrendMonitor(
+        IApplicationDbContext dbContext,
+        ISidecarClient sidecar,
+        IHttpClientFactory httpClientFactory,
+        IOptions<TrendMonitoringOptions> options,
+        ILogger<TrendMonitor> logger)
+    {
+        _dbContext = dbContext;
+        _sidecar = sidecar;
+        _httpClientFactory = httpClientFactory;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task<Result<IReadOnlyList<TrendSuggestion>>> GetSuggestionsAsync(
+        int limit, CancellationToken ct)
+    {
+        var suggestions = await _dbContext.TrendSuggestions
+            .Where(s => s.Status == TrendSuggestionStatus.Pending)
+            .OrderByDescending(s => s.RelevanceScore)
+            .Take(limit)
+            .Include(s => s.RelatedTrends)
+            .ToListAsync(ct);
+
+        return Result<IReadOnlyList<TrendSuggestion>>.Success(suggestions);
+    }
+
+    public async Task<Result<MediatR.Unit>> DismissSuggestionAsync(
+        Guid suggestionId, CancellationToken ct)
+    {
+        var suggestion = await _dbContext.TrendSuggestions
+            .FindAsync([suggestionId], ct);
+
+        if (suggestion is null)
+            return Result<MediatR.Unit>.NotFound($"Suggestion {suggestionId} not found");
+
+        suggestion.Status = TrendSuggestionStatus.Dismissed;
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+    }
+
+    public async Task<Result<Guid>> AcceptSuggestionAsync(
+        Guid suggestionId, CancellationToken ct)
+    {
+        var suggestion = await _dbContext.TrendSuggestions
+            .FindAsync([suggestionId], ct);
+
+        if (suggestion is null)
+            return Result<Guid>.NotFound($"Suggestion {suggestionId} not found");
+
+        if (suggestion.Status == TrendSuggestionStatus.Accepted)
+            return Result<Guid>.Conflict($"Suggestion {suggestionId} is already accepted");
+
+        suggestion.Status = TrendSuggestionStatus.Accepted;
+
+        var content = Content.Create(
+            suggestion.SuggestedContentType,
+            body: "",
+            title: suggestion.Topic,
+            targetPlatforms: suggestion.SuggestedPlatforms);
+
+        await _dbContext.Contents.AddAsync(content, ct);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<Guid>.Success(content.Id);
+    }
+
+    public async Task<Result<MediatR.Unit>> RefreshTrendsAsync(CancellationToken ct)
+    {
+        var sources = await _dbContext.TrendSources
+            .Where(s => s.IsEnabled)
+            .ToListAsync(ct);
+
+        var allItems = new List<TrendItem>();
+
+        foreach (var source in sources)
+        {
+            try
+            {
+                var items = await PollSourceAsync(source, ct);
+                allItems.AddRange(items);
+            }
+            catch (Exception ex)
+            {
+                _logger.LogWarning(ex,
+                    "Failed to poll trend source {SourceName} ({SourceType})",
+                    source.Name, source.Type);
+            }
+        }
+
+        if (allItems.Count == 0)
+        {
+            _logger.LogInformation("No trend items collected from any source");
+            return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+        }
+
+        var deduplicated = TrendDeduplicator.Deduplicate(allItems, _options.TitleSimilarityThreshold);
+
+        await ScoreItemsAsync(deduplicated, ct);
+
+        var suggestions = ClusterAndCreateSuggestions(deduplicated);
+
+        foreach (var suggestion in suggestions.Take(_options.MaxSuggestionsPerCycle))
+        {
+            await _dbContext.TrendSuggestions.AddAsync(suggestion, ct);
+        }
+
+        foreach (var item in deduplicated)
+        {
+            await _dbContext.TrendItems.AddAsync(item, ct);
+        }
+
+        await _dbContext.SaveChangesAsync(ct);
+
+        _logger.LogInformation(
+            "Trend refresh complete: {ItemCount} items, {SuggestionCount} suggestions",
+            deduplicated.Count, Math.Min(suggestions.Count, _options.MaxSuggestionsPerCycle));
+
+        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+    }
+
+    private async Task<List<TrendItem>> PollSourceAsync(TrendSource source, CancellationToken ct)
+    {
+        return source.Type switch
+        {
+            TrendSourceType.TrendRadar => await PollTrendRadarAsync(source, ct),
+            TrendSourceType.FreshRSS => await PollFreshRssAsync(source, ct),
+            TrendSourceType.Reddit => await PollRedditAsync(ct),
+            TrendSourceType.HackerNews => await PollHackerNewsAsync(ct),
+            _ => [],
+        };
+    }
+
+    private async Task<List<TrendItem>> PollTrendRadarAsync(TrendSource source, CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("TrendRadar");
+        var response = await client.GetAsync($"{_options.TrendRadarApiUrl}/trends", ct);
+        response.EnsureSuccessStatusCode();
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var trends = JsonSerializer.Deserialize<List<TrendRadarItem>>(json, JsonOptions) ?? [];
+
+        return trends.Select(t => new TrendItem
+        {
+            Title = t.Title ?? "",
+            Description = t.Description,
+            Url = t.Url,
+            SourceName = source.Name,
+            SourceType = TrendSourceType.TrendRadar,
+            TrendSourceId = source.Id,
+            DetectedAt = t.DetectedAt ?? DateTimeOffset.UtcNow,
+        }).ToList();
+    }
+
+    private async Task<List<TrendItem>> PollFreshRssAsync(TrendSource source, CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("FreshRSS");
+        var url = $"{_options.FreshRssApiUrl}/greader.php/reader/api/0/stream/contents/reading-list";
+        var response = await client.GetAsync(url, ct);
+        response.EnsureSuccessStatusCode();
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var feed = JsonSerializer.Deserialize<FreshRssFeed>(json, JsonOptions);
+
+        return (feed?.Items ?? []).Select(item => new TrendItem
+        {
+            Title = item.Title ?? "",
+            Description = item.Summary,
+            Url = item.Url,
+            SourceName = source.Name,
+            SourceType = TrendSourceType.FreshRSS,
+            TrendSourceId = source.Id,
+            DetectedAt = item.Published ?? DateTimeOffset.UtcNow,
+        }).ToList();
+    }
+
+    private async Task<List<TrendItem>> PollRedditAsync(CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("Reddit");
+        var results = new List<TrendItem>();
+
+        foreach (var subreddit in _options.RedditSubreddits)
+        {
+            try
+            {
+                var response = await client.GetAsync($"/r/{subreddit}/hot.json?limit=25", ct);
+                response.EnsureSuccessStatusCode();
+
+                var json = await response.Content.ReadAsStringAsync(ct);
+                var listing = JsonSerializer.Deserialize<RedditListing>(json, JsonOptions);
+
+                if (listing?.Data?.Children is not null)
+                {
+                    results.AddRange(listing.Data.Children.Select(child => new TrendItem
+                    {
+                        Title = child.Data?.Title ?? "",
+                        Description = child.Data?.Selftext,
+                        Url = child.Data?.Url,
+                        SourceName = $"r/{subreddit}",
+                        SourceType = TrendSourceType.Reddit,
+                        DetectedAt = DateTimeOffset.UtcNow,
+                    }));
+                }
+            }
+            catch (Exception ex)
+            {
+                _logger.LogWarning(ex, "Failed to poll r/{Subreddit}", subreddit);
+            }
+        }
+
+        return results;
+    }
+
+    private async Task<List<TrendItem>> PollHackerNewsAsync(CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("HackerNews");
+        var response = await client.GetAsync($"{_options.HackerNewsApiUrl}/topstories.json", ct);
+        response.EnsureSuccessStatusCode();
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var ids = JsonSerializer.Deserialize<int[]>(json, JsonOptions) ?? [];
+
+        var topIds = ids.Take(_options.HackerNewsTopN);
+        var semaphore = new SemaphoreSlim(_options.HackerNewsConcurrency);
+        var results = new List<TrendItem>();
+
+        var tasks = topIds.Select(async id =>
+        {
+            await semaphore.WaitAsync(ct);
+            try
+            {
+                var itemResponse = await client.GetAsync(
+                    $"{_options.HackerNewsApiUrl}/item/{id}.json", ct);
+                if (!itemResponse.IsSuccessStatusCode)
+                    return null;
+
+                var itemJson = await itemResponse.Content.ReadAsStringAsync(ct);
+                var hnItem = JsonSerializer.Deserialize<HackerNewsItem>(itemJson, JsonOptions);
+
+                if (hnItem is null)
+                    return null;
+
+                return new TrendItem
+                {
+                    Title = hnItem.Title ?? "",
+                    Description = hnItem.Text,
+                    Url = hnItem.Url,
+                    SourceName = "HackerNews",
+                    SourceType = TrendSourceType.HackerNews,
+                    DetectedAt = hnItem.Time is > 0
+                        ? DateTimeOffset.FromUnixTimeSeconds(hnItem.Time.Value)
+                        : DateTimeOffset.UtcNow,
+                };
+            }
+            catch (Exception ex)
+            {
+                _logger.LogWarning(ex, "Failed to fetch HN item {ItemId}", id);
+                return null;
+            }
+            finally
+            {
+                semaphore.Release();
+            }
+        });
+
+        var fetched = await Task.WhenAll(tasks);
+        results.AddRange(fetched.Where(i => i is not null)!);
+
+        return results;
+    }
+
+    private async Task ScoreItemsAsync(IReadOnlyList<TrendItem> items, CancellationToken ct)
+    {
+        var profile = await _dbContext.BrandProfiles
+            .FirstOrDefaultAsync(p => p.IsActive, ct);
+
+        if (profile is null)
+        {
+            _logger.LogWarning("No active brand profile found for relevance scoring");
+            return;
+        }
+
+        var prompt = BuildRelevanceScoringPrompt(items, profile);
+        var (responseText, error) = await ConsumeEventStreamAsync(prompt, ct);
+
+        if (error is not null)
+        {
+            _logger.LogWarning("Sidecar scoring failed: {Error}", error);
+            return;
+        }
+
+        var scores = ParseRelevanceScores(responseText ?? "");
+        foreach (var (index, score) in scores)
+        {
+            if (index >= 0 && index < items.Count)
+            {
+                // Store score in Description metadata for now (TrendItem has no RelevanceScore field)
+                // The score is used for clustering and suggestion creation
+                items[index].Description =
+                    $"[relevance:{score:F2}] {items[index].Description}";
+            }
+        }
+    }
+
+    private static string BuildRelevanceScoringPrompt(
+        IReadOnlyList<TrendItem> items, BrandProfile profile)
+    {
+        var itemLines = string.Join("\n", items.Select((item, i) =>
+            $"{i}. {item.Title} - {item.Description?.Substring(0, Math.Min(item.Description?.Length ?? 0, 200))}"));
+
+        return $$"""
+            You are a brand relevance scorer. Score each item's relevance to the brand profile.
+            Return ONLY valid JSON array, no markdown fencing.
+
+            Brand Profile:
+            - Topics: {{string.Join(", ", profile.Topics)}}
+            - Persona: {{profile.PersonaDescription}}
+
+            Items to score:
+            {{itemLines}}
+
+            Expected JSON: [{"index": 0, "score": 0.0}, ...]
+            Score is 0.0-1.0 where 1.0 is highly relevant.
+            """;
+    }
+
+    private async Task<(string? Text, string? Error)> ConsumeEventStreamAsync(
+        string prompt, CancellationToken ct)
+    {
+        try
+        {
+            var textParts = new List<string>();
+            await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
+            {
+                switch (evt)
+                {
+                    case ChatEvent { Text: not null } chat:
+                        textParts.Add(chat.Text);
+                        break;
+                    case ErrorEvent error:
+                        return (null, error.Message);
+                }
+            }
+
+            return (string.Join("", textParts), null);
+        }
+        catch (Exception ex)
+        {
+            _logger.LogWarning(ex, "Error consuming sidecar event stream");
+            return (null, ex.Message);
+        }
+    }
+
+    private static List<(int Index, float Score)> ParseRelevanceScores(string text)
+    {
+        var cleaned = text.Trim();
+        if (cleaned.StartsWith("```"))
+        {
+            var firstNewline = cleaned.IndexOf('\n');
+            if (firstNewline >= 0)
+                cleaned = cleaned[(firstNewline + 1)..];
+            if (cleaned.EndsWith("```"))
+                cleaned = cleaned[..^3];
+            cleaned = cleaned.Trim();
+        }
+
+        try
+        {
+            var items = JsonSerializer.Deserialize<List<RelevanceScoreDto>>(cleaned, JsonOptions);
+            return items?.Select(i => (i.Index, i.Score)).ToList() ?? [];
+        }
+        catch (JsonException)
+        {
+            return [];
+        }
+    }
+
+    private List<TrendSuggestion> ClusterAndCreateSuggestions(
+        IReadOnlyList<TrendItem> items)
+    {
+        // Extract relevance scores from description metadata
+        var scoredItems = items
+            .Select(item =>
+            {
+                var score = 0f;
+                if (item.Description?.StartsWith("[relevance:") == true)
+                {
+                    var end = item.Description.IndexOf(']');
+                    if (end > 11 && float.TryParse(
+                            item.Description[11..end],
+                            System.Globalization.NumberStyles.Float,
+                            System.Globalization.CultureInfo.InvariantCulture,
+                            out var parsed))
+                    {
+                        score = parsed;
+                    }
+                }
+                return (Item: item, Score: score);
+            })
+            .Where(x => x.Score >= _options.RelevanceScoreThreshold)
+            .OrderByDescending(x => x.Score)
+            .ToList();
+
+        var clusters = new List<(TrendItem Centroid, float MaxScore, List<TrendItem> Members)>();
+
+        foreach (var (item, score) in scoredItems)
+        {
+            var merged = false;
+            foreach (var cluster in clusters)
+            {
+                if (TrendDeduplicator.ComputeTitleSimilarity(
+                        item.Title, cluster.Centroid.Title) >= _options.TitleSimilarityThreshold)
+                {
+                    cluster.Members.Add(item);
+                    merged = true;
+                    break;
+                }
+            }
+
+            if (!merged)
+            {
+                clusters.Add((item, score, [item]));
+            }
+        }
+
+        return clusters.Select(cluster => CreateSuggestion(cluster.Centroid, cluster.MaxScore, cluster.Members))
+            .ToList();
+    }
+
+    private static TrendSuggestion CreateSuggestion(
+        TrendItem centroid, float maxScore, List<TrendItem> members)
+    {
+        var suggestion = new TrendSuggestion
+        {
+            Topic = centroid.Title,
+            Rationale = $"Detected across {members.Count} source(s) with relevance score {maxScore:F2}",
+            RelevanceScore = maxScore,
+            SuggestedContentType = ContentType.BlogPost,
+            SuggestedPlatforms = [PlatformType.LinkedIn],
+            Status = TrendSuggestionStatus.Pending,
+        };
+
+        foreach (var member in members)
+        {
+            suggestion.RelatedTrends.Add(new TrendSuggestionItem
+            {
+                TrendSuggestionId = suggestion.Id,
+                TrendItemId = member.Id,
+                SimilarityScore = TrendDeduplicator.ComputeTitleSimilarity(
+                    centroid.Title, member.Title),
+            });
+        }
+
+        return suggestion;
+    }
+
+    // --- DTO types for API responses ---
+
+    private sealed class TrendRadarItem
+    {
+        [JsonPropertyName("title")]
+        public string? Title { get; set; }
+
+        [JsonPropertyName("description")]
+        public string? Description { get; set; }
+
+        [JsonPropertyName("url")]
+        public string? Url { get; set; }
+
+        [JsonPropertyName("detectedAt")]
+        public DateTimeOffset? DetectedAt { get; set; }
+    }
+
+    private sealed class FreshRssFeed
+    {
+        [JsonPropertyName("items")]
+        public List<FreshRssItem>? Items { get; set; }
+    }
+
+    private sealed class FreshRssItem
+    {
+        [JsonPropertyName("title")]
+        public string? Title { get; set; }
+
+        [JsonPropertyName("summary")]
+        public string? Summary { get; set; }
+
+        [JsonPropertyName("url")]
+        public string? Url { get; set; }
+
+        [JsonPropertyName("published")]
+        public DateTimeOffset? Published { get; set; }
+    }
+
+    private sealed class RedditListing
+    {
+        [JsonPropertyName("data")]
+        public RedditListingData? Data { get; set; }
+    }
+
+    private sealed class RedditListingData
+    {
+        [JsonPropertyName("children")]
+        public List<RedditChild>? Children { get; set; }
+    }
+
+    private sealed class RedditChild
+    {
+        [JsonPropertyName("data")]
+        public RedditPost? Data { get; set; }
+    }
+
+    private sealed class RedditPost
+    {
+        [JsonPropertyName("title")]
+        public string? Title { get; set; }
+
+        [JsonPropertyName("selftext")]
+        public string? Selftext { get; set; }
+
+        [JsonPropertyName("url")]
+        public string? Url { get; set; }
+    }
+
+    private sealed class HackerNewsItem
+    {
+        [JsonPropertyName("title")]
+        public string? Title { get; set; }
+
+        [JsonPropertyName("text")]
+        public string? Text { get; set; }
+
+        [JsonPropertyName("url")]
+        public string? Url { get; set; }
+
+        [JsonPropertyName("time")]
+        public long? Time { get; set; }
+    }
+
+    private sealed class RelevanceScoreDto
+    {
+        [JsonPropertyName("index")]
+        public int Index { get; set; }
+
+        [JsonPropertyName("score")]
+        public float Score { get; set; }
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Common/Models/TrendMonitoringOptionsTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Common/Models/TrendMonitoringOptionsTests.cs
new file mode 100644
index 0000000..4e9cc57
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Common/Models/TrendMonitoringOptionsTests.cs
@@ -0,0 +1,56 @@
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Tests.Common.Models;
+
+public class TrendMonitoringOptionsTests
+{
+    [Fact]
+    public void TrendMonitoringOptions_BindsFromConfiguration_AllPropertiesSet()
+    {
+        var config = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["TrendMonitoring:AggregationIntervalMinutes"] = "60",
+                ["TrendMonitoring:TrendRadarApiUrl"] = "http://custom:9000/api",
+                ["TrendMonitoring:FreshRssApiUrl"] = "http://rss:8080/api",
+                ["TrendMonitoring:RedditSubreddits:0"] = "csharp",
+                ["TrendMonitoring:RedditSubreddits:1"] = "aspnetcore",
+                ["TrendMonitoring:HackerNewsApiUrl"] = "https://hn.custom/v0",
+                ["TrendMonitoring:RelevanceScoreThreshold"] = "0.7",
+                ["TrendMonitoring:TitleSimilarityThreshold"] = "0.9",
+                ["TrendMonitoring:MaxSuggestionsPerCycle"] = "20",
+            })
+            .Build();
+
+        var services = new ServiceCollection();
+        services.Configure<TrendMonitoringOptions>(config.GetSection(TrendMonitoringOptions.SectionName));
+        var provider = services.BuildServiceProvider();
+        var options = provider.GetRequiredService<IOptions<TrendMonitoringOptions>>().Value;
+
+        Assert.Equal(60, options.AggregationIntervalMinutes);
+        Assert.Equal("http://custom:9000/api", options.TrendRadarApiUrl);
+        Assert.Equal("http://rss:8080/api", options.FreshRssApiUrl);
+        Assert.Contains("csharp", options.RedditSubreddits);
+        Assert.Contains("aspnetcore", options.RedditSubreddits);
+        Assert.Equal("https://hn.custom/v0", options.HackerNewsApiUrl);
+        Assert.Equal(0.7f, options.RelevanceScoreThreshold);
+        Assert.Equal(0.9f, options.TitleSimilarityThreshold);
+        Assert.Equal(20, options.MaxSuggestionsPerCycle);
+    }
+
+    [Fact]
+    public void TrendMonitoringOptions_Defaults_AreReasonable()
+    {
+        var options = new TrendMonitoringOptions();
+
+        Assert.Equal(30, options.AggregationIntervalMinutes);
+        Assert.NotNull(options.RedditSubreddits);
+        Assert.NotEmpty(options.RedditSubreddits);
+        Assert.Equal(0.6f, options.RelevanceScoreThreshold);
+        Assert.Equal(0.85f, options.TitleSimilarityThreshold);
+        Assert.Equal(10, options.MaxSuggestionsPerCycle);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TrendAggregationProcessorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TrendAggregationProcessorTests.cs
new file mode 100644
index 0000000..21b96c3
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TrendAggregationProcessorTests.cs
@@ -0,0 +1,73 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class TrendAggregationProcessorTests
+{
+    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
+    private readonly Mock<IServiceScope> _scope = new();
+    private readonly Mock<IServiceProvider> _serviceProvider = new();
+    private readonly Mock<ITrendMonitor> _trendMonitor = new();
+    private readonly Mock<ILogger<TrendAggregationProcessor>> _logger = new();
+    private readonly TrendMonitoringOptions _options = new();
+
+    public TrendAggregationProcessorTests()
+    {
+        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
+        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
+        _serviceProvider.Setup(sp => sp.GetService(typeof(ITrendMonitor)))
+            .Returns(_trendMonitor.Object);
+    }
+
+    private TrendAggregationProcessor CreateSut() => new(
+        _scopeFactory.Object,
+        Options.Create(_options),
+        _logger.Object);
+
+    [Fact]
+    public async Task ProcessCycleAsync_CallsRefreshTrendsAsync()
+    {
+        _trendMonitor.Setup(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
+
+        var sut = CreateSut();
+        await sut.ProcessCycleAsync(CancellationToken.None);
+
+        _trendMonitor.Verify(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ProcessCycleAsync_HandlesRefreshFailure_DoesNotThrow()
+    {
+        _trendMonitor.Setup(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Failure(
+                Application.Common.Errors.ErrorCode.ValidationFailed, "Source unavailable"));
+
+        var sut = CreateSut();
+
+        var exception = await Record.ExceptionAsync(
+            () => sut.ProcessCycleAsync(CancellationToken.None));
+
+        Assert.Null(exception);
+    }
+
+    [Fact]
+    public async Task ProcessCycleAsync_HandlesException_DoesNotThrow()
+    {
+        _trendMonitor.Setup(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("Network error"));
+
+        var sut = CreateSut();
+
+        var exception = await Record.ExceptionAsync(
+            () => sut.ProcessCycleAsync(CancellationToken.None));
+
+        Assert.Null(exception);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendDeduplicationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendDeduplicationTests.cs
new file mode 100644
index 0000000..b55e92c
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendDeduplicationTests.cs
@@ -0,0 +1,124 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;
+
+public class TrendDeduplicationTests
+{
+    [Fact]
+    public void Deduplicate_SameUrl_DifferentSources_MergesIntoSingleItem()
+    {
+        var items = new[]
+        {
+            CreateItem("Article Title", "https://example.com/post", TrendSourceType.Reddit,
+                DateTimeOffset.UtcNow),
+            CreateItem("Article Title", "https://example.com/post", TrendSourceType.HackerNews,
+                DateTimeOffset.UtcNow.AddMinutes(-5)),
+        };
+
+        var result = TrendDeduplicator.Deduplicate(items, 0.85f);
+
+        Assert.Single(result);
+        Assert.Equal(TrendSourceType.HackerNews, result[0].SourceType); // earliest DetectedAt
+    }
+
+    [Fact]
+    public void Deduplicate_UrlCanonicalization_RemovesTrailingSlashAndQueryParams()
+    {
+        var items = new[]
+        {
+            CreateItem("Post A", "https://example.com/post?utm_source=x", TrendSourceType.Reddit,
+                DateTimeOffset.UtcNow),
+            CreateItem("Post B", "https://example.com/post/", TrendSourceType.FreshRSS,
+                DateTimeOffset.UtcNow.AddMinutes(-1)),
+        };
+
+        var result = TrendDeduplicator.Deduplicate(items, 0.85f);
+
+        Assert.Single(result);
+    }
+
+    [Fact]
+    public void Deduplicate_FuzzyTitleMatch_AboveThreshold_MergesItems()
+    {
+        // Items without URLs, titles above threshold
+        var items = new[]
+        {
+            CreateItem("Building AI Agents with .NET 10", null, TrendSourceType.TrendRadar,
+                DateTimeOffset.UtcNow),
+            CreateItem("Building AI Agents with .NET 10 Preview", null, TrendSourceType.HackerNews,
+                DateTimeOffset.UtcNow),
+        };
+
+        // Jaccard: intersection={"building","ai","agents","with",".net","10"} (6)
+        //          union={"building","ai","agents","with",".net","10","preview"} (7)
+        //          similarity = 6/7 = 0.857 > 0.85
+        var result = TrendDeduplicator.Deduplicate(items, 0.85f);
+
+        Assert.Single(result);
+    }
+
+    [Fact]
+    public void Deduplicate_FuzzyTitleMatch_BelowThreshold_KeepsBothItems()
+    {
+        var items = new[]
+        {
+            CreateItem("Kubernetes Best Practices for Production", null, TrendSourceType.Reddit,
+                DateTimeOffset.UtcNow),
+            CreateItem("Getting Started with Docker Compose", null, TrendSourceType.HackerNews,
+                DateTimeOffset.UtcNow),
+        };
+
+        var result = TrendDeduplicator.Deduplicate(items, 0.85f);
+
+        Assert.Equal(2, result.Count);
+    }
+
+    [Fact]
+    public void DeduplicationKey_IsDeterministic_ForSameUrl()
+    {
+        var url = "https://example.com/article?utm_source=twitter&id=42";
+        var key1 = TrendDeduplicator.ComputeDeduplicationKey(TrendDeduplicator.CanonicalizeUrl(url));
+        var key2 = TrendDeduplicator.ComputeDeduplicationKey(TrendDeduplicator.CanonicalizeUrl(url));
+
+        Assert.Equal(key1, key2);
+        Assert.NotEmpty(key1);
+    }
+
+    [Fact]
+    public void CanonicalizeUrl_RemovesAllTrackingParams()
+    {
+        var url = "https://example.com/post?utm_source=x&utm_medium=y&ref=abc&id=42";
+        var canonical = TrendDeduplicator.CanonicalizeUrl(url);
+
+        Assert.DoesNotContain("utm_source", canonical);
+        Assert.DoesNotContain("utm_medium", canonical);
+        Assert.DoesNotContain("ref=", canonical);
+        Assert.Contains("id=42", canonical);
+    }
+
+    [Fact]
+    public void ComputeTitleSimilarity_IdenticalTitles_ReturnsOne()
+    {
+        var similarity = TrendDeduplicator.ComputeTitleSimilarity("Hello World", "Hello World");
+        Assert.Equal(1f, similarity);
+    }
+
+    [Fact]
+    public void ComputeTitleSimilarity_EmptyTitle_ReturnsZero()
+    {
+        var similarity = TrendDeduplicator.ComputeTitleSimilarity("Hello", "");
+        Assert.Equal(0f, similarity);
+    }
+
+    private static TrendItem CreateItem(string title, string? url, TrendSourceType sourceType,
+        DateTimeOffset detectedAt) => new()
+    {
+        Title = title,
+        Url = url,
+        SourceType = sourceType,
+        SourceName = sourceType.ToString(),
+        DetectedAt = detectedAt,
+    };
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendMonitorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendMonitorTests.cs
new file mode 100644
index 0000000..aad3520
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendMonitorTests.cs
@@ -0,0 +1,257 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;
+
+public class TrendMonitorTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<ISidecarClient> _sidecar = new();
+    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
+    private readonly Mock<ILogger<TrendMonitor>> _logger = new();
+    private readonly TrendMonitoringOptions _options = new();
+
+    private TrendMonitor CreateSut() => new(
+        _dbContext.Object,
+        _sidecar.Object,
+        _httpClientFactory.Object,
+        Options.Create(_options),
+        _logger.Object);
+
+    private void SetupDbSets(
+        TrendSuggestion[]? suggestions = null,
+        TrendItem[]? items = null,
+        TrendSource[]? sources = null,
+        BrandProfile[]? profiles = null)
+    {
+        var suggestionMock = (suggestions ?? []).AsQueryable().BuildMockDbSet();
+        suggestionMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .Returns<object[], CancellationToken>((keys, _) =>
+                ValueTask.FromResult((suggestions ?? []).FirstOrDefault(s => s.Id == (Guid)keys[0])));
+        _dbContext.Setup(d => d.TrendSuggestions).Returns(suggestionMock.Object);
+
+        var itemMock = (items ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.TrendItems).Returns(itemMock.Object);
+
+        var sourceMock = (sources ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.TrendSources).Returns(sourceMock.Object);
+
+        var profileMock = (profiles ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.BrandProfiles).Returns(profileMock.Object);
+
+        var contentList = new List<Domain.Entities.Content>();
+        var contentMock = contentList.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);
+
+        var suggestionItemList = new List<TrendSuggestionItem>();
+        var suggestionItemMock = suggestionItemList.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.TrendSuggestionItems).Returns(suggestionItemMock.Object);
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateAsyncEnumerable(
+        params SidecarEvent[] events)
+    {
+        foreach (var e in events)
+        {
+            yield return e;
+            await Task.CompletedTask;
+        }
+    }
+
+    // --- GetSuggestionsAsync ---
+
+    [Fact]
+    public async Task GetSuggestionsAsync_WithSuggestions_ReturnsSuggestionsOrderedByRelevanceDescending()
+    {
+        var suggestions = new[]
+        {
+            new TrendSuggestion
+            {
+                Topic = "Low", RelevanceScore = 0.3f,
+                Status = TrendSuggestionStatus.Pending, Rationale = "test",
+            },
+            new TrendSuggestion
+            {
+                Topic = "High", RelevanceScore = 0.9f,
+                Status = TrendSuggestionStatus.Pending, Rationale = "test",
+            },
+            new TrendSuggestion
+            {
+                Topic = "Mid", RelevanceScore = 0.6f,
+                Status = TrendSuggestionStatus.Pending, Rationale = "test",
+            },
+        };
+        SetupDbSets(suggestions: suggestions);
+
+        var sut = CreateSut();
+        var result = await sut.GetSuggestionsAsync(10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(3, result.Value!.Count);
+        Assert.Equal("High", result.Value[0].Topic);
+        Assert.Equal("Mid", result.Value[1].Topic);
+        Assert.Equal("Low", result.Value[2].Topic);
+    }
+
+    [Fact]
+    public async Task GetSuggestionsAsync_RespectsLimit()
+    {
+        var suggestions = Enumerable.Range(0, 5)
+            .Select(i => new TrendSuggestion
+            {
+                Topic = $"Topic {i}", RelevanceScore = i * 0.2f,
+                Status = TrendSuggestionStatus.Pending, Rationale = "test",
+            })
+            .ToArray();
+        SetupDbSets(suggestions: suggestions);
+
+        var sut = CreateSut();
+        var result = await sut.GetSuggestionsAsync(2, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Count);
+    }
+
+    [Fact]
+    public async Task GetSuggestionsAsync_NoSuggestions_ReturnsEmptyList()
+    {
+        SetupDbSets();
+
+        var sut = CreateSut();
+        var result = await sut.GetSuggestionsAsync(10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!);
+    }
+
+    [Fact]
+    public async Task GetSuggestionsAsync_OnlyReturnsPendingSuggestions()
+    {
+        var suggestions = new[]
+        {
+            new TrendSuggestion
+            {
+                Topic = "Pending", RelevanceScore = 0.9f,
+                Status = TrendSuggestionStatus.Pending, Rationale = "test",
+            },
+            new TrendSuggestion
+            {
+                Topic = "Accepted", RelevanceScore = 0.8f,
+                Status = TrendSuggestionStatus.Accepted, Rationale = "test",
+            },
+            new TrendSuggestion
+            {
+                Topic = "Dismissed", RelevanceScore = 0.7f,
+                Status = TrendSuggestionStatus.Dismissed, Rationale = "test",
+            },
+        };
+        SetupDbSets(suggestions: suggestions);
+
+        var sut = CreateSut();
+        var result = await sut.GetSuggestionsAsync(10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+        Assert.Equal("Pending", result.Value![0].Topic);
+    }
+
+    // --- DismissSuggestionAsync ---
+
+    [Fact]
+    public async Task DismissSuggestionAsync_ValidId_SetsStatusToDismissed()
+    {
+        var suggestion = new TrendSuggestion
+        {
+            Topic = "Test", RelevanceScore = 0.5f,
+            Status = TrendSuggestionStatus.Pending, Rationale = "test",
+        };
+        SetupDbSets(suggestions: [suggestion]);
+
+        var sut = CreateSut();
+        var result = await sut.DismissSuggestionAsync(suggestion.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(TrendSuggestionStatus.Dismissed, suggestion.Status);
+        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task DismissSuggestionAsync_InvalidId_ReturnsNotFound()
+    {
+        SetupDbSets();
+
+        var sut = CreateSut();
+        var result = await sut.DismissSuggestionAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // --- AcceptSuggestionAsync ---
+
+    [Fact]
+    public async Task AcceptSuggestionAsync_ValidId_SetsStatusAndCreatesContent()
+    {
+        var suggestion = new TrendSuggestion
+        {
+            Topic = "AI Trends in 2026",
+            RelevanceScore = 0.9f,
+            Status = TrendSuggestionStatus.Pending,
+            Rationale = "Highly relevant",
+            SuggestedContentType = ContentType.BlogPost,
+            SuggestedPlatforms = [PlatformType.LinkedIn],
+        };
+        SetupDbSets(suggestions: [suggestion]);
+
+        var sut = CreateSut();
+        var result = await sut.AcceptSuggestionAsync(suggestion.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotEqual(Guid.Empty, result.Value);
+        Assert.Equal(TrendSuggestionStatus.Accepted, suggestion.Status);
+        _dbContext.Verify(d => d.Contents.AddAsync(
+            It.Is<Domain.Entities.Content>(c => c.Title == "AI Trends in 2026"),
+            It.IsAny<CancellationToken>()), Times.Once);
+        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task AcceptSuggestionAsync_AlreadyAccepted_ReturnsConflict()
+    {
+        var suggestion = new TrendSuggestion
+        {
+            Topic = "Already Done",
+            RelevanceScore = 0.9f,
+            Status = TrendSuggestionStatus.Accepted,
+            Rationale = "test",
+        };
+        SetupDbSets(suggestions: [suggestion]);
+
+        var sut = CreateSut();
+        var result = await sut.AcceptSuggestionAsync(suggestion.Id, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task AcceptSuggestionAsync_InvalidId_ReturnsNotFound()
+    {
+        SetupDbSets();
+
+        var sut = CreateSut();
+        var result = await sut.AcceptSuggestionAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+}
