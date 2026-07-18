diff --git a/src/PBA.Application/Common/Interfaces/ChannelPollResult.cs b/src/PBA.Application/Common/Interfaces/ChannelPollResult.cs
new file mode 100644
index 0000000..aab8de2
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/ChannelPollResult.cs
@@ -0,0 +1,11 @@
+namespace PBA.Application.Common.Interfaces;
+
+// The result of one poll: the current cumulative account snapshot plus recent-video snapshots. Every value
+// in a Metrics bag is an integer count (or whole seconds) — ratios/rates are computed at read time, never here.
+public record ChannelPollResult(AccountMetrics Account, IReadOnlyList<VideoMetrics> RecentVideos);
+
+// Provisional is always false for these platforms (cumulative counts are final at capture); it exists for
+// the poller's sentinel logic and is not persisted.
+public record AccountMetrics(IReadOnlyDictionary<string, long> Metrics, bool Provisional);
+
+public record VideoMetrics(string VideoId, string? Title, IReadOnlyDictionary<string, long> Metrics, bool Provisional);
diff --git a/src/PBA.Application/Common/Interfaces/IChannelAnalyticsService.cs b/src/PBA.Application/Common/Interfaces/IChannelAnalyticsService.cs
new file mode 100644
index 0000000..1a9e550
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IChannelAnalyticsService.cs
@@ -0,0 +1,16 @@
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Common.Interfaces;
+
+// One keyed service per analytics platform (YouTube, Instagram, TikTok). A thin facade over an injectable
+// HTTP/SDK client; PollAsync never throws (API failures -> Result.Fail). The credential carries the
+// decrypted-at-use access token (the poller ensures freshness before calling).
+public interface IChannelAnalyticsService
+{
+    Platform Platform { get; }
+
+    Task<Result<ChannelPollResult>> PollAsync(
+        PlatformCredential credential, int recentVideoCount, CancellationToken ct);
+}
diff --git a/src/PBA.Application/Common/Interfaces/IInstagramGraphClient.cs b/src/PBA.Application/Common/Interfaces/IInstagramGraphClient.cs
new file mode 100644
index 0000000..977a60a
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IInstagramGraphClient.cs
@@ -0,0 +1,17 @@
+namespace PBA.Application.Common.Interfaces;
+
+// Thin seam over graph.instagram.com (Instagram-Login). The facade defines the canonical metric list and
+// passes it in; the client requests those metrics and silently drops any the API rejects as unknown/
+// deprecated (maps by returned name), so a missing metric just doesn't appear in the returned bag.
+public interface IInstagramGraphClient
+{
+    // Account insights + follower_count merged into one metric-name -> value map.
+    Task<IReadOnlyDictionary<string, long>> GetAccountMetricsAsync(
+        string accessToken, IReadOnlyList<string> metrics, CancellationToken ct);
+
+    // Recent media with per-media insights, newest-first, up to max.
+    Task<IReadOnlyList<InstagramMediaMetrics>> GetRecentMediaAsync(
+        string accessToken, IReadOnlyList<string> mediaMetrics, int max, CancellationToken ct);
+}
+
+public record InstagramMediaMetrics(string MediaId, string? Caption, IReadOnlyDictionary<string, long> Metrics);
diff --git a/src/PBA.Application/Common/Interfaces/ITikTokDisplayClient.cs b/src/PBA.Application/Common/Interfaces/ITikTokDisplayClient.cs
new file mode 100644
index 0000000..a3b8be5
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/ITikTokDisplayClient.cs
@@ -0,0 +1,17 @@
+namespace PBA.Application.Common.Interfaces;
+
+// Thin seam over open.tiktokapis.com v2 Display API. /v2/video/list/ caps at 20/page, so the facade loops
+// on cursor/has_more (via GetVideoPageAsync) to accumulate up to N videos. No reach/demographics/retention
+// are available on TikTok — all TikTok trends are snapshot deltas only.
+public interface ITikTokDisplayClient
+{
+    // /v2/user/info/ — follower_count, following_count, likes_count, video_count as a name -> value map.
+    Task<IReadOnlyDictionary<string, long>> GetUserStatsAsync(string accessToken, CancellationToken ct);
+
+    // One page of /v2/video/list/ (<=20 videos) plus the cursor/has_more for the next page.
+    Task<TikTokVideoPage> GetVideoPageAsync(string accessToken, string? cursor, CancellationToken ct);
+}
+
+public record TikTokVideoPage(IReadOnlyList<TikTokVideo> Videos, string? NextCursor, bool HasMore);
+
+public record TikTokVideo(string Id, string? Title, long Views, long Likes, long Comments, long Shares);
diff --git a/src/PBA.Application/Common/Interfaces/IYouTubeApiClient.cs b/src/PBA.Application/Common/Interfaces/IYouTubeApiClient.cs
new file mode 100644
index 0000000..af0ef84
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IYouTubeApiClient.cs
@@ -0,0 +1,31 @@
+namespace PBA.Application.Common.Interfaces;
+
+// Thin seam over YouTube Data API v3 (poll path) + Analytics API v2 (live deep path). SDK types stay behind
+// this seam so the facade maps plain records and tests feed canned responses without the SDK. The facade
+// owns orchestration (uploads-playlist paging, <=50 videos.list batching, N cap); each method here is one call.
+public interface IYouTubeApiClient
+{
+    // channels.list?part=statistics,contentDetails — cumulative stats + the uploads playlist id.
+    Task<YouTubeChannelStats> GetChannelAsync(string accessToken, CancellationToken ct);
+
+    // playlistItems.list on the uploads playlist (1 unit, newest-first). NOT search.list (100 units, unreliable).
+    Task<YouTubePlaylistPage> GetUploadsPageAsync(string playlistId, string? pageToken, string accessToken, CancellationToken ct);
+
+    // videos.list?part=statistics,snippet for a single batch of <=50 ids (the facade chunks).
+    Task<IReadOnlyList<YouTubeVideoStat>> GetVideosBatchAsync(IReadOnlyList<string> ids, string accessToken, CancellationToken ct);
+
+    // Analytics v2 reports.query — live deep path, NOT snapshotted. Returns raw column headers + rows.
+    Task<YouTubeReportResult> RunAnalyticsReportAsync(YouTubeReportRequest request, string accessToken, CancellationToken ct);
+}
+
+public record YouTubeChannelStats(long Subscribers, long Views, long Videos, string UploadsPlaylistId);
+
+public record YouTubePlaylistPage(IReadOnlyList<string> VideoIds, string? NextPageToken);
+
+public record YouTubeVideoStat(string VideoId, string? Title, long Views, long Likes, long Comments);
+
+public record YouTubeReportRequest(
+    string Ids, string Dimensions, IReadOnlyList<string> Metrics, DateOnly StartDate, DateOnly EndDate);
+
+public record YouTubeReportResult(
+    IReadOnlyList<string> ColumnHeaders, IReadOnlyList<IReadOnlyList<string>> Rows);
diff --git a/src/PBA.Application/Features/Analytics/Dtos/YouTubeDeepAnalyticsSeries.cs b/src/PBA.Application/Features/Analytics/Dtos/YouTubeDeepAnalyticsSeries.cs
new file mode 100644
index 0000000..60b3e57
--- /dev/null
+++ b/src/PBA.Application/Features/Analytics/Dtos/YouTubeDeepAnalyticsSeries.cs
@@ -0,0 +1,7 @@
+namespace PBA.Application.Features.Analytics.Dtos;
+
+// One labeled time series per Analytics v2 metric column. Deep-path values may be fractional (e.g.
+// averageViewDuration), so this is a double — the integer-only invariant applies only to snapshot bags.
+public record YouTubeMetricSeries(string Metric, IReadOnlyList<YouTubeMetricPoint> Points);
+
+public record YouTubeMetricPoint(string Day, double Value);
diff --git a/src/PBA.Application/Features/Analytics/YouTubeDeepAnalyticsMapper.cs b/src/PBA.Application/Features/Analytics/YouTubeDeepAnalyticsMapper.cs
new file mode 100644
index 0000000..6ac665d
--- /dev/null
+++ b/src/PBA.Application/Features/Analytics/YouTubeDeepAnalyticsMapper.cs
@@ -0,0 +1,45 @@
+using System.Globalization;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Analytics.Dtos;
+
+namespace PBA.Application.Features.Analytics;
+
+// Maps a day-dimensioned Analytics v2 report (column headers + string rows) into one labeled series per
+// metric column. Section-06's live deep-analytics query wires the client call + this mapper into its DTO.
+public static class YouTubeDeepAnalyticsMapper
+{
+    public static IReadOnlyList<YouTubeMetricSeries> MapToSeries(YouTubeReportResult report)
+    {
+        var headers = report.ColumnHeaders;
+        var dayIndex = IndexOf(headers, "day");
+
+        var series = new List<YouTubeMetricSeries>();
+        for (var col = 0; col < headers.Count; col++)
+        {
+            if (col == dayIndex)
+                continue;
+
+            var points = new List<YouTubeMetricPoint>();
+            foreach (var row in report.Rows)
+            {
+                var day = dayIndex >= 0 && dayIndex < row.Count ? row[dayIndex] : string.Empty;
+                var value = col < row.Count
+                    && double.TryParse(row[col], NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
+                        ? d : 0d;
+                points.Add(new YouTubeMetricPoint(day, value));
+            }
+
+            series.Add(new YouTubeMetricSeries(headers[col], points));
+        }
+
+        return series;
+    }
+
+    private static int IndexOf(IReadOnlyList<string> headers, string name)
+    {
+        for (var i = 0; i < headers.Count; i++)
+            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
+                return i;
+        return -1;
+    }
+}
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index bb34c82..7e2d9a3 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -124,6 +124,20 @@ public static class DependencyInjection
         services.AddSingleton<ISearchConsoleClient, PBA.Infrastructure.Services.Analytics.SearchConsoleClient>();
         services.AddScoped<IGoogleAnalyticsService, PBA.Infrastructure.Services.Analytics.GoogleAnalyticsService>();
 
+        // Channel analytics thin clients (SDK/HTTP seams) + keyed per-platform facades.
+        services.AddScoped<IYouTubeApiClient, PBA.Infrastructure.Services.Analytics.YouTubeApiClient>();
+        services.AddHttpClient<IInstagramGraphClient, PBA.Infrastructure.Services.Analytics.InstagramGraphClient>(
+            client => client.BaseAddress = new Uri("https://graph.instagram.com/"));
+        services.AddHttpClient<ITikTokDisplayClient, PBA.Infrastructure.Services.Analytics.TikTokDisplayClient>(
+            client => client.BaseAddress = new Uri("https://open.tiktokapis.com/"));
+
+        services.AddKeyedScoped<IChannelAnalyticsService,
+            PBA.Infrastructure.Services.Analytics.YouTubeAnalyticsService>(Platform.YouTube);
+        services.AddKeyedScoped<IChannelAnalyticsService,
+            PBA.Infrastructure.Services.Analytics.InstagramAnalyticsService>(Platform.Instagram);
+        services.AddKeyedScoped<IChannelAnalyticsService,
+            PBA.Infrastructure.Services.Analytics.TikTokAnalyticsService>(Platform.TikTok);
+
         return services;
     }
 
diff --git a/src/PBA.Infrastructure/PBA.Infrastructure.csproj b/src/PBA.Infrastructure/PBA.Infrastructure.csproj
index cdf8b69..44fad94 100644
--- a/src/PBA.Infrastructure/PBA.Infrastructure.csproj
+++ b/src/PBA.Infrastructure/PBA.Infrastructure.csproj
@@ -13,6 +13,8 @@
   <ItemGroup>
     <PackageReference Include="Google.Analytics.Data.V1Beta" Version="2.0.0-beta10" />
     <PackageReference Include="Google.Apis.SearchConsole.v1" Version="1.74.0.3847" />
+    <PackageReference Include="Google.Apis.YouTube.v3" Version="1.75.0.4207" />
+    <PackageReference Include="Google.Apis.YouTubeAnalytics.v2" Version="1.74.0.3106" />
     <PackageReference Include="MailKit" Version="4.17.0" />
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.7" />
     <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.7" />
diff --git a/src/PBA.Infrastructure/Services/Analytics/InstagramAnalyticsService.cs b/src/PBA.Infrastructure/Services/Analytics/InstagramAnalyticsService.cs
new file mode 100644
index 0000000..3a5bbc7
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/InstagramAnalyticsService.cs
@@ -0,0 +1,53 @@
+using Microsoft.Extensions.Logging;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// Instagram analytics facade over IInstagramGraphClient. The requested metric lists are data-driven constants;
+// the client silently drops any metric the API rejects as deprecated, so a dropped metric just doesn't appear
+// in the bag (the poll still succeeds). `impressions` is intentionally absent — `views` replaces it.
+public sealed class InstagramAnalyticsService(
+    IInstagramGraphClient client,
+    ITokenEncryptor encryptor,
+    ILogger<InstagramAnalyticsService> logger) : IChannelAnalyticsService
+{
+    // "followers" is added by the client from follower_count; the rest are insight metric names.
+    private static readonly string[] AccountInsightMetrics =
+    [
+        "reach", "views", "accounts_engaged", "total_interactions",
+        "likes", "comments", "saves", "shares", "profile_links_taps"
+    ];
+
+    private static readonly string[] MediaMetrics =
+        ["reach", "views", "likes", "comments", "saves", "shares"];
+
+    public Platform Platform => Platform.Instagram;
+
+    public async Task<Result<ChannelPollResult>> PollAsync(
+        PlatformCredential credential, int recentVideoCount, CancellationToken ct)
+    {
+        try
+        {
+            var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);
+
+            var accountMetrics = await client.GetAccountMetricsAsync(accessToken, AccountInsightMetrics, ct);
+            var account = new AccountMetrics(accountMetrics, Provisional: false);
+
+            var media = await client.GetRecentMediaAsync(accessToken, MediaMetrics, recentVideoCount, ct);
+            var videos = media
+                .Take(recentVideoCount)
+                .Select(m => new VideoMetrics(m.MediaId, m.Caption, m.Metrics, Provisional: false))
+                .ToList();
+
+            return Result<ChannelPollResult>.Success(new ChannelPollResult(account, videos));
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "Instagram poll failed for credential {CredentialId}", credential.Id);
+            return Result<ChannelPollResult>.Fail($"Instagram poll failed: {ex.Message}");
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Services/Analytics/InstagramGraphClient.cs b/src/PBA.Infrastructure/Services/Analytics/InstagramGraphClient.cs
new file mode 100644
index 0000000..37166b6
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/InstagramGraphClient.cs
@@ -0,0 +1,105 @@
+using System.Text.Json;
+using Microsoft.Extensions.Logging;
+using PBA.Application.Common.Interfaces;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// Thin seam over graph.instagram.com (Instagram-Login). Maps insight metrics by RETURNED name, so any metric
+// the API omits/deprecates simply doesn't appear in the bag (logged at Debug). Untested by design (the facade
+// mocks IInstagramGraphClient); needs a real-credential smoke test before prod. BaseAddress is set in DI.
+public sealed class InstagramGraphClient(HttpClient http, ILogger<InstagramGraphClient> logger)
+    : IInstagramGraphClient
+{
+    public async Task<IReadOnlyDictionary<string, long>> GetAccountMetricsAsync(
+        string accessToken, IReadOnlyList<string> metrics, CancellationToken ct)
+    {
+        var userId = await GetUserIdAsync(accessToken, ct);
+        var result = new Dictionary<string, long>();
+
+        // follower_count via the user node.
+        var followers = await GetJsonAsync($"{userId}?fields=followers_count&access_token={Enc(accessToken)}", ct);
+        if (followers.TryGetProperty("followers_count", out var fc) && fc.ValueKind == JsonValueKind.Number)
+            result["followers"] = fc.GetInt64();
+
+        // Account insights (metric_type=total_value, period=day). Map by returned name; dropped metrics absent.
+        var metricCsv = string.Join(",", metrics);
+        var insights = await GetJsonAsync(
+            $"{userId}/insights?metric={Enc(metricCsv)}&metric_type=total_value&period=day&access_token={Enc(accessToken)}", ct);
+
+        if (insights.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
+        {
+            foreach (var item in data.EnumerateArray())
+            {
+                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
+                if (name is null)
+                    continue;
+                if (item.TryGetProperty("total_value", out var tv) && tv.TryGetProperty("value", out var val)
+                    && val.ValueKind == JsonValueKind.Number)
+                    result[name] = val.GetInt64();
+                else
+                    logger.LogDebug("Instagram metric {Metric} returned no total_value; skipped", name);
+            }
+        }
+
+        return result;
+    }
+
+    public async Task<IReadOnlyList<InstagramMediaMetrics>> GetRecentMediaAsync(
+        string accessToken, IReadOnlyList<string> mediaMetrics, int max, CancellationToken ct)
+    {
+        var list = await GetJsonAsync($"me/media?fields=id,caption&limit={max}&access_token={Enc(accessToken)}", ct);
+        var media = new List<InstagramMediaMetrics>();
+
+        if (!list.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
+            return media;
+
+        var metricCsv = string.Join(",", mediaMetrics);
+        foreach (var item in data.EnumerateArray().Take(max))
+        {
+            var mediaId = item.TryGetProperty("id", out var id) ? id.GetString() : null;
+            if (mediaId is null)
+                continue;
+            var caption = item.TryGetProperty("caption", out var cap) ? cap.GetString() : null;
+
+            var metrics = new Dictionary<string, long>();
+            var insights = await GetJsonAsync($"{mediaId}/insights?metric={Enc(metricCsv)}&access_token={Enc(accessToken)}", ct);
+            if (insights.TryGetProperty("data", out var mData) && mData.ValueKind == JsonValueKind.Array)
+            {
+                foreach (var m in mData.EnumerateArray())
+                {
+                    var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
+                    if (name is null)
+                        continue;
+                    if (m.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
+                        && values.EnumerateArray().FirstOrDefault() is { } first
+                        && first.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number)
+                        metrics[name] = val.GetInt64();
+                }
+            }
+
+            media.Add(new InstagramMediaMetrics(mediaId, caption, metrics));
+        }
+
+        return media;
+    }
+
+    private async Task<string> GetUserIdAsync(string accessToken, CancellationToken ct)
+    {
+        var me = await GetJsonAsync($"me?fields=user_id&access_token={Enc(accessToken)}", ct);
+        if (me.TryGetProperty("user_id", out var uid) && uid.ValueKind == JsonValueKind.String)
+            return uid.GetString()!;
+        if (me.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
+            return id.GetString()!;
+        throw new InvalidOperationException("Instagram /me returned no user id");
+    }
+
+    private async Task<JsonElement> GetJsonAsync(string relativeUrl, CancellationToken ct)
+    {
+        using var response = await http.GetAsync(relativeUrl, ct);
+        response.EnsureSuccessStatusCode();
+        var json = await response.Content.ReadAsStringAsync(ct);
+        return JsonSerializer.Deserialize<JsonElement>(json);
+    }
+
+    private static string Enc(string value) => Uri.EscapeDataString(value);
+}
diff --git a/src/PBA.Infrastructure/Services/Analytics/TikTokAnalyticsService.cs b/src/PBA.Infrastructure/Services/Analytics/TikTokAnalyticsService.cs
new file mode 100644
index 0000000..0a75b5c
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/TikTokAnalyticsService.cs
@@ -0,0 +1,67 @@
+using Microsoft.Extensions.Logging;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// TikTok analytics facade over ITikTokDisplayClient. /v2/video/list/ caps at 20/page, so the facade loops on
+// cursor/has_more to accumulate up to N videos. Ceiling: no reach/demographics/retention on TikTok — every
+// TikTok trend is a snapshot delta only.
+public sealed class TikTokAnalyticsService(
+    ITikTokDisplayClient client,
+    ITokenEncryptor encryptor,
+    ILogger<TikTokAnalyticsService> logger) : IChannelAnalyticsService
+{
+    public Platform Platform => Platform.TikTok;
+
+    public async Task<Result<ChannelPollResult>> PollAsync(
+        PlatformCredential credential, int recentVideoCount, CancellationToken ct)
+    {
+        try
+        {
+            var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);
+
+            var stats = await client.GetUserStatsAsync(accessToken, ct);
+            var account = new AccountMetrics(stats, Provisional: false);
+
+            var videos = await AccumulateVideosAsync(accessToken, recentVideoCount, ct);
+
+            return Result<ChannelPollResult>.Success(new ChannelPollResult(account, videos));
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "TikTok poll failed for credential {CredentialId}", credential.Id);
+            return Result<ChannelPollResult>.Fail($"TikTok poll failed: {ex.Message}");
+        }
+    }
+
+    // Loop on cursor/has_more (20/page) until N videos are collected or there are no more pages.
+    private async Task<IReadOnlyList<VideoMetrics>> AccumulateVideosAsync(
+        string accessToken, int count, CancellationToken ct)
+    {
+        var videos = new List<VideoMetrics>();
+        string? cursor = null;
+        bool hasMore;
+        do
+        {
+            var page = await client.GetVideoPageAsync(accessToken, cursor, ct);
+            foreach (var v in page.Videos)
+            {
+                videos.Add(new VideoMetrics(v.Id, v.Title, new Dictionary<string, long>
+                {
+                    ["views"] = v.Views,
+                    ["likes"] = v.Likes,
+                    ["comments"] = v.Comments,
+                    ["shares"] = v.Shares
+                }, Provisional: false));
+            }
+            cursor = page.NextCursor;
+            hasMore = page.HasMore;
+        }
+        while (videos.Count < count && hasMore && cursor is not null);
+
+        return videos.Take(count).ToList();
+    }
+}
diff --git a/src/PBA.Infrastructure/Services/Analytics/TikTokDisplayClient.cs b/src/PBA.Infrastructure/Services/Analytics/TikTokDisplayClient.cs
new file mode 100644
index 0000000..9f06b54
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/TikTokDisplayClient.cs
@@ -0,0 +1,95 @@
+using System.Net.Http.Headers;
+using System.Net.Http.Json;
+using System.Text.Json;
+using Microsoft.Extensions.Logging;
+using PBA.Application.Common.Interfaces;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// Thin seam over open.tiktokapis.com v2 Display API. One page per GetVideoPageAsync (the facade loops on the
+// cursor/has_more). Untested by design (the facade mocks ITikTokDisplayClient); needs a real-credential smoke
+// test before prod. BaseAddress is set in DI.
+public sealed class TikTokDisplayClient(HttpClient http, ILogger<TikTokDisplayClient> logger)
+    : ITikTokDisplayClient
+{
+    private const int PageSize = 20;
+
+    public async Task<IReadOnlyDictionary<string, long>> GetUserStatsAsync(string accessToken, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            "v2/user/info/?fields=follower_count,following_count,likes_count,video_count");
+        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
+
+        var user = (await SendAsync(request, ct)).GetProperty("data").GetProperty("user");
+
+        var result = new Dictionary<string, long>();
+        AddIfPresent(result, "followers", user, "follower_count");
+        AddIfPresent(result, "following", user, "following_count");
+        AddIfPresent(result, "likes", user, "likes_count");
+        AddIfPresent(result, "videos", user, "video_count");
+        return result;
+    }
+
+    public async Task<TikTokVideoPage> GetVideoPageAsync(string accessToken, string? cursor, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Post,
+            "v2/video/list/?fields=id,title,view_count,like_count,comment_count,share_count");
+        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
+        request.Content = JsonContent.Create(cursor is not null && long.TryParse(cursor, out var c)
+            ? new { max_count = PageSize, cursor = c }
+            : (object)new { max_count = PageSize });
+
+        var data = (await SendAsync(request, ct)).GetProperty("data");
+
+        var videos = new List<TikTokVideo>();
+        if (data.TryGetProperty("videos", out var arr) && arr.ValueKind == JsonValueKind.Array)
+        {
+            foreach (var v in arr.EnumerateArray())
+            {
+                videos.Add(new TikTokVideo(
+                    Id: v.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
+                    Title: v.TryGetProperty("title", out var t) ? t.GetString() : null,
+                    Views: ReadLong(v, "view_count"),
+                    Likes: ReadLong(v, "like_count"),
+                    Comments: ReadLong(v, "comment_count"),
+                    Shares: ReadLong(v, "share_count")));
+            }
+        }
+
+        var nextCursor = data.TryGetProperty("cursor", out var cur) && cur.ValueKind == JsonValueKind.Number
+            ? cur.GetInt64().ToString()
+            : null;
+        var hasMore = data.TryGetProperty("has_more", out var hm) && hm.ValueKind is JsonValueKind.True or JsonValueKind.False
+            && hm.GetBoolean();
+
+        return new TikTokVideoPage(videos, nextCursor, hasMore);
+    }
+
+    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken ct)
+    {
+        using var response = await http.SendAsync(request, ct);
+        response.EnsureSuccessStatusCode();
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var root = JsonSerializer.Deserialize<JsonElement>(json);
+
+        // TikTok reports errors in the body even on 200; error.code == "ok" means success.
+        if (root.TryGetProperty("error", out var error)
+            && error.TryGetProperty("code", out var code)
+            && code.GetString() is { } c && c != "ok")
+        {
+            logger.LogWarning("TikTok Display API error: {Code}", c);
+            throw new InvalidOperationException($"TikTok Display API error: {c}");
+        }
+
+        return root;
+    }
+
+    private static void AddIfPresent(Dictionary<string, long> bag, string key, JsonElement obj, string field)
+    {
+        if (obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number)
+            bag[key] = v.GetInt64();
+    }
+
+    private static long ReadLong(JsonElement obj, string field) =>
+        obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
+}
diff --git a/src/PBA.Infrastructure/Services/Analytics/YouTubeAnalyticsService.cs b/src/PBA.Infrastructure/Services/Analytics/YouTubeAnalyticsService.cs
new file mode 100644
index 0000000..4a20cf2
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/YouTubeAnalyticsService.cs
@@ -0,0 +1,77 @@
+using Microsoft.Extensions.Logging;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// YouTube analytics facade over IYouTubeApiClient. Owns orchestration: cumulative channel stats, recent-video
+// discovery via the uploads playlist (never search), <=50-id videos.list batching, and the N cap.
+public sealed class YouTubeAnalyticsService(
+    IYouTubeApiClient client,
+    ITokenEncryptor encryptor,
+    ILogger<YouTubeAnalyticsService> logger) : IChannelAnalyticsService
+{
+    private const int MaxVideosPerBatch = 50;
+
+    public Platform Platform => Platform.YouTube;
+
+    public async Task<Result<ChannelPollResult>> PollAsync(
+        PlatformCredential credential, int recentVideoCount, CancellationToken ct)
+    {
+        try
+        {
+            var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);
+
+            var channel = await client.GetChannelAsync(accessToken, ct);
+            var account = new AccountMetrics(new Dictionary<string, long>
+            {
+                ["subscribers"] = channel.Subscribers,
+                ["views"] = channel.Views,
+                ["videos"] = channel.Videos
+            }, Provisional: false);
+
+            var recentIds = await DiscoverRecentVideoIdsAsync(channel.UploadsPlaylistId, recentVideoCount, accessToken, ct);
+
+            var videos = new List<VideoMetrics>();
+            foreach (var chunk in recentIds.Chunk(MaxVideosPerBatch))
+            {
+                var batch = await client.GetVideosBatchAsync(chunk, accessToken, ct);
+                foreach (var v in batch)
+                {
+                    videos.Add(new VideoMetrics(v.VideoId, v.Title, new Dictionary<string, long>
+                    {
+                        ["views"] = v.Views,
+                        ["likes"] = v.Likes,
+                        ["comments"] = v.Comments
+                    }, Provisional: false));
+                }
+            }
+
+            return Result<ChannelPollResult>.Success(new ChannelPollResult(account, videos));
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "YouTube poll failed for credential {CredentialId}", credential.Id);
+            return Result<ChannelPollResult>.Fail($"YouTube poll failed: {ex.Message}");
+        }
+    }
+
+    // Page the uploads playlist newest-first until we have N ids (or run out of pages), then take N.
+    private async Task<IReadOnlyList<string>> DiscoverRecentVideoIdsAsync(
+        string uploadsPlaylistId, int count, string accessToken, CancellationToken ct)
+    {
+        var ids = new List<string>();
+        string? pageToken = null;
+        do
+        {
+            var page = await client.GetUploadsPageAsync(uploadsPlaylistId, pageToken, accessToken, ct);
+            ids.AddRange(page.VideoIds);
+            pageToken = page.NextPageToken;
+        }
+        while (ids.Count < count && pageToken is not null);
+
+        return ids.Take(count).ToList();
+    }
+}
diff --git a/src/PBA.Infrastructure/Services/Analytics/YouTubeApiClient.cs b/src/PBA.Infrastructure/Services/Analytics/YouTubeApiClient.cs
new file mode 100644
index 0000000..78930ba
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/YouTubeApiClient.cs
@@ -0,0 +1,114 @@
+using Google.Apis.Auth.OAuth2;
+using Google.Apis.Services;
+using Google.Apis.Util;
+using Google.Apis.YouTube.v3;
+using PBA.Application.Common.Interfaces;
+using YouTubeAnalyticsSdk = Google.Apis.YouTubeAnalytics.v2;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// Thin seam over the YouTube Data v3 + Analytics v2 SDKs, authorized per-call with the user's OAuth access
+// token. SDK types never escape this class — every method returns the plain records the facade maps. Untested
+// by design (the facade mocks IYouTubeApiClient); needs a real-credential smoke test before prod.
+public sealed class YouTubeApiClient : IYouTubeApiClient
+{
+    private const string AppName = "PersonalBrandAssistant";
+
+    public async Task<YouTubeChannelStats> GetChannelAsync(string accessToken, CancellationToken ct)
+    {
+        using var service = BuildDataService(accessToken);
+        var request = service.Channels.List("statistics,contentDetails");
+        request.Mine = true;
+
+        var response = await request.ExecuteAsync(ct);
+        var channel = response.Items?.FirstOrDefault();
+        if (channel is null)
+            return new YouTubeChannelStats(0, 0, 0, string.Empty);
+
+        return new YouTubeChannelStats(
+            Subscribers: (long)(channel.Statistics?.SubscriberCount ?? 0),
+            Views: (long)(channel.Statistics?.ViewCount ?? 0),
+            Videos: (long)(channel.Statistics?.VideoCount ?? 0),
+            UploadsPlaylistId: channel.ContentDetails?.RelatedPlaylists?.Uploads ?? string.Empty);
+    }
+
+    public async Task<YouTubePlaylistPage> GetUploadsPageAsync(
+        string playlistId, string? pageToken, string accessToken, CancellationToken ct)
+    {
+        using var service = BuildDataService(accessToken);
+        var request = service.PlaylistItems.List("contentDetails");
+        request.PlaylistId = playlistId;
+        request.MaxResults = 50;
+        request.PageToken = pageToken;
+
+        var response = await request.ExecuteAsync(ct);
+        var ids = (response.Items ?? [])
+            .Select(i => i.ContentDetails?.VideoId)
+            .Where(id => !string.IsNullOrEmpty(id))
+            .Select(id => id!)
+            .ToList();
+
+        return new YouTubePlaylistPage(ids, response.NextPageToken);
+    }
+
+    public async Task<IReadOnlyList<YouTubeVideoStat>> GetVideosBatchAsync(
+        IReadOnlyList<string> ids, string accessToken, CancellationToken ct)
+    {
+        if (ids.Count == 0)
+            return [];
+
+        using var service = BuildDataService(accessToken);
+        var request = service.Videos.List("statistics,snippet");
+        request.Id = new Repeatable<string>(ids);
+        request.MaxResults = 50;
+
+        var response = await request.ExecuteAsync(ct);
+        return (response.Items ?? [])
+            .Select(v => new YouTubeVideoStat(
+                VideoId: v.Id,
+                Title: v.Snippet?.Title,
+                Views: (long)(v.Statistics?.ViewCount ?? 0),
+                Likes: (long)(v.Statistics?.LikeCount ?? 0),
+                Comments: (long)(v.Statistics?.CommentCount ?? 0)))
+            .ToList();
+    }
+
+    public async Task<YouTubeReportResult> RunAnalyticsReportAsync(
+        YouTubeReportRequest request, string accessToken, CancellationToken ct)
+    {
+        using var service = BuildAnalyticsService(accessToken);
+        var query = service.Reports.Query();
+        query.Ids = request.Ids;
+        query.Dimensions = request.Dimensions;
+        query.Metrics = string.Join(",", request.Metrics);
+        query.StartDate = request.StartDate.ToString("yyyy-MM-dd");
+        query.EndDate = request.EndDate.ToString("yyyy-MM-dd");
+
+        var response = await query.ExecuteAsync(ct);
+
+        var headers = (response.ColumnHeaders ?? [])
+            .Select(h => h.Name ?? string.Empty)
+            .ToList();
+        var rows = (response.Rows ?? [])
+            .Select(row => (IReadOnlyList<string>)row
+                .Select(cell => cell?.ToString() ?? string.Empty)
+                .ToList())
+            .ToList();
+
+        return new YouTubeReportResult(headers, rows);
+    }
+
+    private static YouTubeService BuildDataService(string accessToken) =>
+        new(new BaseClientService.Initializer
+        {
+            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
+            ApplicationName = AppName
+        });
+
+    private static YouTubeAnalyticsSdk.YouTubeAnalyticsService BuildAnalyticsService(string accessToken) =>
+        new(new BaseClientService.Initializer
+        {
+            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
+            ApplicationName = AppName
+        });
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Analytics/AnalyticsServicesDiTests.cs b/tests/PBA.Infrastructure.Tests/Services/Analytics/AnalyticsServicesDiTests.cs
new file mode 100644
index 0000000..ca03494
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Analytics/AnalyticsServicesDiTests.cs
@@ -0,0 +1,35 @@
+using Microsoft.Extensions.DependencyInjection;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Services.Analytics;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Analytics;
+
+// Boundary test: exactly one IChannelAnalyticsService resolves per analytics platform via keyed DI. Mirrors
+// the keyed registrations in DependencyInjection.cs.
+public class AnalyticsServicesDiTests
+{
+    [Fact]
+    public void AnalyticsServices_ResolveByPlatformKey_ViaKeyedDI()
+    {
+        var services = new ServiceCollection();
+        services.AddSingleton(Mock.Of<IYouTubeApiClient>());
+        services.AddSingleton(Mock.Of<IInstagramGraphClient>());
+        services.AddSingleton(Mock.Of<ITikTokDisplayClient>());
+        services.AddSingleton(Mock.Of<ITokenEncryptor>());
+        services.AddLogging();
+
+        services.AddKeyedScoped<IChannelAnalyticsService, YouTubeAnalyticsService>(Platform.YouTube);
+        services.AddKeyedScoped<IChannelAnalyticsService, InstagramAnalyticsService>(Platform.Instagram);
+        services.AddKeyedScoped<IChannelAnalyticsService, TikTokAnalyticsService>(Platform.TikTok);
+
+        using var provider = services.BuildServiceProvider();
+
+        Assert.IsType<YouTubeAnalyticsService>(provider.GetKeyedService<IChannelAnalyticsService>(Platform.YouTube));
+        Assert.IsType<InstagramAnalyticsService>(provider.GetKeyedService<IChannelAnalyticsService>(Platform.Instagram));
+        Assert.IsType<TikTokAnalyticsService>(provider.GetKeyedService<IChannelAnalyticsService>(Platform.TikTok));
+        Assert.Null(provider.GetKeyedService<IChannelAnalyticsService>(Platform.LinkedIn));
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Analytics/InstagramAnalyticsServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Analytics/InstagramAnalyticsServiceTests.cs
new file mode 100644
index 0000000..7f86e39
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Analytics/InstagramAnalyticsServiceTests.cs
@@ -0,0 +1,110 @@
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Services.Analytics;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Analytics;
+
+public class InstagramAnalyticsServiceTests
+{
+    private readonly Mock<IInstagramGraphClient> _client = new();
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+
+    public InstagramAnalyticsServiceTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
+    }
+
+    private InstagramAnalyticsService CreateService() =>
+        new(_client.Object, _encryptor.Object, NullLogger<InstagramAnalyticsService>.Instance);
+
+    private static PlatformCredential Credential() =>
+        new() { Platform = Platform.Instagram, EncryptedAccessToken = "token" };
+
+    private void SetupAccount(IReadOnlyDictionary<string, long> metrics) =>
+        _client.Setup(c => c.GetAccountMetricsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(metrics);
+
+    private void SetupMedia(IReadOnlyList<InstagramMediaMetrics> media) =>
+        _client.Setup(c => c.GetRecentMediaAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(media);
+
+    [Fact]
+    public async Task PollAsync_MapsAccountInsights_ToCanonicalKeys()
+    {
+        SetupAccount(new Dictionary<string, long>
+        {
+            ["followers"] = 5000, ["reach"] = 1200, ["views"] = 3400, ["likes"] = 210, ["comments"] = 15
+        });
+        SetupMedia([]);
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        var account = result.Value!.Account.Metrics;
+        Assert.Equal(5000, account["followers"]);
+        Assert.Equal(1200, account["reach"]);
+        Assert.Equal(3400, account["views"]);
+    }
+
+    [Fact]
+    public async Task PollAsync_DropsUnknownDeprecatedMetric_WithoutFailing()
+    {
+        // The API dropped "impressions" (deprecated) — the client returns a bag simply lacking it.
+        SetupAccount(new Dictionary<string, long> { ["followers"] = 100, ["views"] = 50 });
+        SetupMedia([]);
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.False(result.Value!.Account.Metrics.ContainsKey("impressions"));
+        Assert.True(result.Value.Account.Metrics.ContainsKey("views"));
+    }
+
+    [Fact]
+    public async Task PollAsync_MapsPerMediaInsights()
+    {
+        SetupAccount(new Dictionary<string, long> { ["followers"] = 100 });
+        SetupMedia([
+            new InstagramMediaMetrics("m1", "caption", new Dictionary<string, long>
+            {
+                ["reach"] = 300, ["views"] = 900, ["likes"] = 40, ["comments"] = 5, ["saves"] = 8, ["shares"] = 2
+            })
+        ]);
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        var media = result.Value!.RecentVideos.Single();
+        Assert.Equal("m1", media.VideoId);
+        Assert.Equal("caption", media.Title);
+        Assert.Equal(900, media.Metrics["views"]);
+        Assert.Equal(8, media.Metrics["saves"]);
+    }
+
+    [Fact]
+    public async Task PollAsync_ApiError_ReturnsResultFail_NotThrow()
+    {
+        _client.Setup(c => c.GetAccountMetricsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("boom"));
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics()
+    {
+        SetupAccount(new Dictionary<string, long>());
+        SetupMedia([]);
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!.Account.Metrics);
+        Assert.Empty(result.Value.RecentVideos);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Analytics/TikTokAnalyticsServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Analytics/TikTokAnalyticsServiceTests.cs
new file mode 100644
index 0000000..fb902d9
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Analytics/TikTokAnalyticsServiceTests.cs
@@ -0,0 +1,112 @@
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Services.Analytics;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Analytics;
+
+public class TikTokAnalyticsServiceTests
+{
+    private readonly Mock<ITikTokDisplayClient> _client = new();
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+
+    public TikTokAnalyticsServiceTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
+    }
+
+    private TikTokAnalyticsService CreateService() =>
+        new(_client.Object, _encryptor.Object, NullLogger<TikTokAnalyticsService>.Instance);
+
+    private static PlatformCredential Credential() =>
+        new() { Platform = Platform.TikTok, EncryptedAccessToken = "token" };
+
+    private void SetupUser(long followers, long following, long likes, long videos) =>
+        _client.Setup(c => c.GetUserStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new Dictionary<string, long>
+            {
+                ["followers"] = followers, ["following"] = following, ["likes"] = likes, ["videos"] = videos
+            });
+
+    private static TikTokVideoPage Page(int count, string prefix, string? nextCursor, bool hasMore) =>
+        new(Enumerable.Range(0, count).Select(i => new TikTokVideo($"{prefix}{i}", "t", 100, 10, 2, 1)).ToList(),
+            nextCursor, hasMore);
+
+    [Fact]
+    public async Task PollAsync_MapsUserInfoStats_ToAccountKeys()
+    {
+        SetupUser(9000, 120, 45000, 300);
+        _client.Setup(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Page(0, "v", null, false));
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        var account = result.Value!.Account.Metrics;
+        Assert.Equal(9000, account["followers"]);
+        Assert.Equal(120, account["following"]);
+        Assert.Equal(45000, account["likes"]);
+        Assert.Equal(300, account["videos"]);
+    }
+
+    [Fact]
+    public async Task PollAsync_PaginatesVideoList_ToReachN()
+    {
+        SetupUser(1, 1, 1, 1);
+        // 20/page: page 1 (has_more=true) then page 2 (has_more=false) accumulate to N=40.
+        _client.SetupSequence(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Page(20, "a", "cursor1", hasMore: true))
+            .ReturnsAsync(Page(20, "b", null, hasMore: false));
+
+        var result = await CreateService().PollAsync(Credential(), 40, CancellationToken.None);
+
+        Assert.Equal(40, result.Value!.RecentVideos.Count);
+        _client.Verify(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
+    }
+
+    [Fact]
+    public async Task PollAsync_MapsPerVideoCounts()
+    {
+        SetupUser(1, 1, 1, 1);
+        _client.Setup(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new TikTokVideoPage(
+                [new TikTokVideo("vid1", "My Clip", 5000, 400, 30, 12)], null, false));
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        var video = result.Value!.RecentVideos.Single();
+        Assert.Equal("vid1", video.VideoId);
+        Assert.Equal(5000, video.Metrics["views"]);
+        Assert.Equal(400, video.Metrics["likes"]);
+        Assert.Equal(30, video.Metrics["comments"]);
+        Assert.Equal(12, video.Metrics["shares"]);
+    }
+
+    [Fact]
+    public async Task PollAsync_ApiError_ReturnsResultFail_NotThrow()
+    {
+        _client.Setup(c => c.GetUserStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("boom"));
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics()
+    {
+        _client.Setup(c => c.GetUserStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new Dictionary<string, long>());
+        _client.Setup(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Page(0, "v", null, false));
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!.RecentVideos);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Analytics/YouTubeAnalyticsServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Analytics/YouTubeAnalyticsServiceTests.cs
new file mode 100644
index 0000000..b5b27db
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Analytics/YouTubeAnalyticsServiceTests.cs
@@ -0,0 +1,142 @@
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Services.Analytics;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Analytics;
+
+public class YouTubeAnalyticsServiceTests
+{
+    private readonly Mock<IYouTubeApiClient> _client = new();
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+
+    public YouTubeAnalyticsServiceTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
+    }
+
+    private YouTubeAnalyticsService CreateService() =>
+        new(_client.Object, _encryptor.Object, NullLogger<YouTubeAnalyticsService>.Instance);
+
+    private static PlatformCredential Credential() =>
+        new() { Platform = Platform.YouTube, EncryptedAccessToken = "token" };
+
+    private void SetupChannel(long subs, long views, long videos, string uploadsPlaylistId = "UP1") =>
+        _client.Setup(c => c.GetChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new YouTubeChannelStats(subs, views, videos, uploadsPlaylistId));
+
+    private void SetupUploads(IReadOnlyList<string> ids, string? nextPageToken = null) =>
+        _client.Setup(c => c.GetUploadsPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new YouTubePlaylistPage(ids, nextPageToken));
+
+    [Fact]
+    public async Task PollAsync_MapsChannelStatistics_ToCumulativeAccountKeys()
+    {
+        SetupChannel(1000, 50000, 42);
+        SetupUploads([]);
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        var account = result.Value!.Account.Metrics;
+        Assert.Equal(1000, account["subscribers"]);
+        Assert.Equal(50000, account["views"]);
+        Assert.Equal(42, account["videos"]);
+        Assert.Equal(3, account.Count);   // integer-only bag with exactly the canonical keys
+    }
+
+    [Fact]
+    public async Task PollAsync_DiscoversRecentVideos_ViaUploadsPlaylist_NotSearch()
+    {
+        SetupChannel(1, 1, 2);
+        SetupUploads(["v1", "v2"]);
+        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((IReadOnlyList<string> ids, string _, CancellationToken _) =>
+                ids.Select(id => new YouTubeVideoStat(id, "t", 10, 2, 1)).ToList());
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        // There is NO search seam on IYouTubeApiClient — discovery structurally uses the uploads playlist.
+        _client.Verify(c => c.GetUploadsPageAsync("UP1", null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
+        Assert.Equal(2, result.Value!.RecentVideos.Count);
+    }
+
+    [Fact]
+    public async Task PollAsync_BatchesVideosList_MaxFiftyIds()
+    {
+        SetupChannel(1, 1, 120);
+        var ids = Enumerable.Range(0, 120).Select(i => $"v{i}").ToList();
+        SetupUploads(ids);
+
+        var batchSizes = new List<int>();
+        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((IReadOnlyList<string> batch, string _, CancellationToken _) =>
+            {
+                batchSizes.Add(batch.Count);
+                return batch.Select(id => new YouTubeVideoStat(id, "t", 1, 1, 1)).ToList();
+            });
+
+        await CreateService().PollAsync(Credential(), 120, CancellationToken.None);
+
+        Assert.All(batchSizes, size => Assert.True(size <= 50, $"batch of {size} exceeds 50"));
+        Assert.Equal(120, batchSizes.Sum());
+    }
+
+    [Fact]
+    public async Task PollAsync_CapsRecentVideos_AtN()
+    {
+        SetupChannel(1, 1, 100);
+        SetupUploads(Enumerable.Range(0, 100).Select(i => $"v{i}").ToList());
+        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((IReadOnlyList<string> batch, string _, CancellationToken _) =>
+                batch.Select(id => new YouTubeVideoStat(id, "t", 1, 1, 1)).ToList());
+
+        var result = await CreateService().PollAsync(Credential(), 5, CancellationToken.None);
+
+        Assert.Equal(5, result.Value!.RecentVideos.Count);
+    }
+
+    [Fact]
+    public async Task PollAsync_MapsPerVideoCounts_ToIntegerKeys()
+    {
+        SetupChannel(1, 1, 1);
+        SetupUploads(["v1"]);
+        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync([new YouTubeVideoStat("v1", "My Video", 999, 88, 7)]);
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        var video = result.Value!.RecentVideos.Single();
+        Assert.Equal("v1", video.VideoId);
+        Assert.Equal("My Video", video.Title);
+        Assert.Equal(999, video.Metrics["views"]);
+        Assert.Equal(88, video.Metrics["likes"]);
+        Assert.Equal(7, video.Metrics["comments"]);
+    }
+
+    [Fact]
+    public async Task PollAsync_ApiError_ReturnsResultFail_NotThrow()
+    {
+        _client.Setup(c => c.GetChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("boom"));
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics()
+    {
+        SetupChannel(0, 0, 0);
+        SetupUploads([]);
+
+        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!.RecentVideos);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Analytics/YouTubeDeepAnalyticsMapperTests.cs b/tests/PBA.Infrastructure.Tests/Services/Analytics/YouTubeDeepAnalyticsMapperTests.cs
new file mode 100644
index 0000000..e837133
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Analytics/YouTubeDeepAnalyticsMapperTests.cs
@@ -0,0 +1,35 @@
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Analytics;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Analytics;
+
+public class YouTubeDeepAnalyticsMapperTests
+{
+    [Fact]
+    public void MapsReportsRows_ToLabeledSeries()
+    {
+        // Day-dimensioned Analytics v2 report: one series per non-day column, points labeled by day.
+        var report = new YouTubeReportResult(
+            ColumnHeaders: ["day", "views", "averageViewDuration"],
+            Rows:
+            [
+                ["2026-07-15", "100", "42.5"],
+                ["2026-07-16", "150", "48.0"]
+            ]);
+
+        var series = YouTubeDeepAnalyticsMapper.MapToSeries(report);
+
+        Assert.Equal(2, series.Count);   // views + averageViewDuration (day is the label, not a series)
+
+        var views = series.Single(s => s.Metric == "views");
+        Assert.Equal("2026-07-15", views.Points[0].Day);
+        Assert.Equal(100, views.Points[0].Value);
+        Assert.Equal(150, views.Points[1].Value);
+
+        // Deep-path values may be fractional (unlike snapshot bags).
+        var avg = series.Single(s => s.Metric == "averageViewDuration");
+        Assert.Equal(42.5, avg.Points[0].Value);
+        Assert.Equal(48.0, avg.Points[1].Value);
+    }
+}
