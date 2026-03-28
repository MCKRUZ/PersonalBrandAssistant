diff --git a/src/PersonalBrandAssistant.Api/Endpoints/IntegrationEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/IntegrationEndpoints.cs
new file mode 100644
index 0000000..557459d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/IntegrationEndpoints.cs
@@ -0,0 +1,48 @@
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class IntegrationEndpoints
+{
+    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder app)
+    {
+        var contentGroup = app.MapGroup("/api/content").WithTags("Integration");
+        contentGroup.MapGet("/queue-status", GetQueueStatus);
+        contentGroup.MapGet("/pipeline-health", GetPipelineHealth);
+
+        var analyticsGroup = app.MapGroup("/api/analytics").WithTags("Integration");
+        analyticsGroup.MapGet("/engagement-summary", GetEngagementSummary);
+
+        var briefingGroup = app.MapGroup("/api/briefing").WithTags("Integration");
+        briefingGroup.MapGet("/summary", GetBriefingSummary);
+    }
+
+    private static async Task<IResult> GetQueueStatus(
+        IIntegrationMonitorService service, CancellationToken ct)
+    {
+        var result = await service.GetQueueStatusAsync(ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetPipelineHealth(
+        IIntegrationMonitorService service, CancellationToken ct)
+    {
+        var result = await service.GetPipelineHealthAsync(ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetEngagementSummary(
+        IIntegrationMonitorService service, CancellationToken ct)
+    {
+        var result = await service.GetEngagementSummaryAsync(ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetBriefingSummary(
+        IIntegrationMonitorService service, CancellationToken ct)
+    {
+        var result = await service.GetBriefingSummaryAsync(ct);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index 654531f..621337b 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -67,6 +67,8 @@ app.MapBrandVoiceEndpoints();
 app.MapTrendEndpoints();
 app.MapAnalyticsEndpoints();
 app.MapSocialEndpoints();
+app.MapContentIdeaEndpoints();
+app.MapIntegrationEndpoints();
 
 app.Run();
 
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IIntegrationMonitorService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IIntegrationMonitorService.cs
new file mode 100644
index 0000000..08f61a6
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IIntegrationMonitorService.cs
@@ -0,0 +1,12 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Common;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IIntegrationMonitorService
+{
+    Task<Result<QueueStatusResponse>> GetQueueStatusAsync(CancellationToken ct);
+    Task<Result<PipelineHealthResponse>> GetPipelineHealthAsync(CancellationToken ct);
+    Task<Result<EngagementSummaryResponse>> GetEngagementSummaryAsync(CancellationToken ct);
+    Task<Result<BriefingSummaryResponse>> GetBriefingSummaryAsync(CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/MonitorDtos.cs b/src/PersonalBrandAssistant.Application/Common/Models/MonitorDtos.cs
new file mode 100644
index 0000000..f806350
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/MonitorDtos.cs
@@ -0,0 +1,65 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public sealed record QueueStatusResponse(
+    int QueueDepth,
+    ScheduledPostInfo? NextScheduledPost,
+    int PostsLast24h,
+    IReadOnlyDictionary<string, int> ItemsByStage);
+
+public sealed record ScheduledPostInfo(
+    Guid ContentId,
+    string Platform,
+    DateTimeOffset ScheduledAt);
+
+public sealed record PipelineHealthResponse(
+    IReadOnlyList<StuckItemInfo> StuckItems,
+    int FailedGenerations24h,
+    double ErrorRate,
+    int ActiveCount);
+
+public sealed record StuckItemInfo(
+    Guid ContentId,
+    string Stage,
+    DateTimeOffset StuckSince,
+    double HoursStuck);
+
+public sealed record EngagementSummaryResponse(
+    int Rolling7DayEngagement,
+    double AverageEngagement,
+    IReadOnlyList<EngagementAnomaly> Anomalies,
+    IReadOnlyDictionary<string, int> PlatformBreakdown);
+
+public sealed record EngagementAnomaly(
+    Guid ContentId,
+    string Platform,
+    string Metric,
+    int Value,
+    double Average,
+    double Multiplier,
+    string Direction,
+    double Confidence);
+
+public sealed record BriefingSummaryResponse(
+    IReadOnlyList<ScheduledContentInfo> ScheduledToday,
+    IReadOnlyList<EngagementHighlight> EngagementHighlights,
+    IReadOnlyList<TrendingTopicInfo> TrendingTopics,
+    int QueueDepth,
+    ScheduledPostInfo? NextPublish,
+    int PendingApprovals);
+
+public sealed record ScheduledContentInfo(
+    Guid ContentId,
+    string Platform,
+    DateTimeOffset Time,
+    string? Title);
+
+public sealed record EngagementHighlight(
+    Guid ContentId,
+    string Platform,
+    string Metric,
+    int Value);
+
+public sealed record TrendingTopicInfo(
+    string Topic,
+    float RelevanceScore,
+    string Source);
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index f4bef21..edd7eb5 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -17,6 +17,7 @@ using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
 using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
 using PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;
 using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+using PersonalBrandAssistant.Infrastructure.Services.IntegrationServices;
 using PersonalBrandAssistant.Infrastructure.Services.SocialServices;
 
 namespace PersonalBrandAssistant.Infrastructure;
@@ -45,11 +46,14 @@ public static class DependencyInjection
         services.AddScoped<IApplicationDbContext>(sp =>
             sp.GetRequiredService<ApplicationDbContext>());
 
-        services.AddDataProtection()
-            .PersistKeysToFileSystem(new DirectoryInfo(
-                configuration["DataProtection:KeyPath"] ?? "data-protection-keys"))
+        var dpBuilder = services.AddDataProtection()
             .SetApplicationName("PersonalBrandAssistant");
 
+        var keyPath = configuration["DataProtection:KeyPath"];
+        if (!string.IsNullOrWhiteSpace(keyPath) && IsDirectoryWritable(keyPath))
+            dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
+        // else: ephemeral (in-memory) keys — fine for dev/single-instance
+
         services.AddSingleton<IEncryptionService, EncryptionService>();
 
         // Agent orchestration
@@ -122,6 +126,7 @@ public static class DependencyInjection
         });
         services.AddScoped<IArticleScraper, FirecrawlScraper>();
         services.AddScoped<IArticleAnalyzer, ArticleAnalyzer>();
+        services.AddScoped<IContentIdeaService, ContentIdeaService>();
 
         // Platform integration options
         services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
@@ -203,6 +208,7 @@ public static class DependencyInjection
         services.AddScoped<IHumanScheduler, HumanScheduler>();
         services.AddScoped<ISocialEngagementService, SocialEngagementService>();
         services.AddScoped<ISocialInboxService, SocialInboxService>();
+        services.AddScoped<IIntegrationMonitorService, IntegrationMonitorService>();
 
         // Background services
         services.AddHostedService<DataSeeder>();
@@ -230,4 +236,17 @@ public static class DependencyInjection
 
         return services;
     }
+
+    private static bool IsDirectoryWritable(string path)
+    {
+        try
+        {
+            Directory.CreateDirectory(path);
+            var probe = System.IO.Path.Combine(path, ".write-test");
+            File.WriteAllText(probe, "");
+            File.Delete(probe);
+            return true;
+        }
+        catch { return false; }
+    }
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/IntegrationServices/IntegrationMonitorService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/IntegrationServices/IntegrationMonitorService.cs
new file mode 100644
index 0000000..03b9723
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/IntegrationServices/IntegrationMonitorService.cs
@@ -0,0 +1,258 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.IntegrationServices;
+
+public sealed class IntegrationMonitorService : IIntegrationMonitorService
+{
+    private static readonly ContentStatus[] TerminalStatuses =
+        [ContentStatus.Published, ContentStatus.Archived];
+
+    private static readonly ContentStatus[] ActiveStatuses =
+        [ContentStatus.Draft, ContentStatus.Review, ContentStatus.Approved,
+         ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Failed];
+
+    private readonly IApplicationDbContext _dbContext;
+    private readonly IDateTimeProvider _dateTime;
+    private readonly ILogger<IntegrationMonitorService> _logger;
+
+    public IntegrationMonitorService(
+        IApplicationDbContext dbContext,
+        IDateTimeProvider dateTime,
+        ILogger<IntegrationMonitorService> logger)
+    {
+        _dbContext = dbContext;
+        _dateTime = dateTime;
+        _logger = logger;
+    }
+
+    public async Task<Result<QueueStatusResponse>> GetQueueStatusAsync(CancellationToken ct)
+    {
+        var now = _dateTime.UtcNow;
+        var last24h = now.AddHours(-24);
+
+        var activeContents = await _dbContext.Contents
+            .AsNoTracking()
+            .Where(c => !TerminalStatuses.Contains(c.Status))
+            .ToListAsync(ct);
+
+        var queueDepth = activeContents.Count;
+
+        var itemsByStage = activeContents
+            .GroupBy(c => c.Status.ToString())
+            .ToDictionary(g => g.Key, g => g.Count())
+            as IReadOnlyDictionary<string, int>;
+
+        var nextScheduled = await _dbContext.Contents
+            .AsNoTracking()
+            .Where(c => c.Status == ContentStatus.Scheduled && c.ScheduledAt != null && c.ScheduledAt > now)
+            .OrderBy(c => c.ScheduledAt)
+            .Select(c => new ScheduledPostInfo(
+                c.Id,
+                c.TargetPlatforms.Length > 0 ? c.TargetPlatforms[0].ToString() : "Unknown",
+                c.ScheduledAt!.Value))
+            .FirstOrDefaultAsync(ct);
+
+        var postsLast24h = await _dbContext.Contents
+            .AsNoTracking()
+            .CountAsync(c => c.PublishedAt != null && c.PublishedAt >= last24h, ct);
+
+        return Result<QueueStatusResponse>.Success(new QueueStatusResponse(
+            queueDepth, nextScheduled, postsLast24h, itemsByStage));
+    }
+
+    public async Task<Result<PipelineHealthResponse>> GetPipelineHealthAsync(CancellationToken ct)
+    {
+        var now = _dateTime.UtcNow;
+        var stuckThreshold = now.AddHours(-2);
+        var last24h = now.AddHours(-24);
+
+        var stuckItems = await _dbContext.Contents
+            .AsNoTracking()
+            .Where(c => !TerminalStatuses.Contains(c.Status)
+                     && c.Status != ContentStatus.Failed
+                     && c.UpdatedAt < stuckThreshold)
+            .Select(c => new StuckItemInfo(
+                c.Id,
+                c.Status.ToString(),
+                c.UpdatedAt,
+                (now - c.UpdatedAt).TotalHours))
+            .ToListAsync(ct);
+
+        var failedCount = await _dbContext.WorkflowTransitionLogs
+            .AsNoTracking()
+            .CountAsync(w => w.ToStatus == ContentStatus.Failed && w.Timestamp >= last24h, ct);
+
+        var publishedCount = await _dbContext.WorkflowTransitionLogs
+            .AsNoTracking()
+            .CountAsync(w => w.ToStatus == ContentStatus.Published && w.Timestamp >= last24h, ct);
+
+        var totalCompletions = failedCount + publishedCount;
+        var errorRate = totalCompletions > 0 ? (double)failedCount / totalCompletions : 0.0;
+
+        var activeCount = await _dbContext.Contents
+            .AsNoTracking()
+            .CountAsync(c => !TerminalStatuses.Contains(c.Status), ct);
+
+        return Result<PipelineHealthResponse>.Success(new PipelineHealthResponse(
+            stuckItems, failedCount, errorRate, activeCount));
+    }
+
+    public async Task<Result<EngagementSummaryResponse>> GetEngagementSummaryAsync(CancellationToken ct)
+    {
+        var now = _dateTime.UtcNow;
+        var sevenDaysAgo = now.AddDays(-7);
+
+        var snapshots = await _dbContext.EngagementSnapshots
+            .AsNoTracking()
+            .Where(s => s.FetchedAt >= sevenDaysAgo)
+            .Join(
+                _dbContext.ContentPlatformStatuses.AsNoTracking(),
+                s => s.ContentPlatformStatusId,
+                cps => cps.Id,
+                (s, cps) => new
+                {
+                    s.Likes,
+                    s.Comments,
+                    s.Shares,
+                    cps.ContentId,
+                    Platform = cps.Platform.ToString()
+                })
+            .ToListAsync(ct);
+
+        var rolling7Day = snapshots.Sum(s => s.Likes + s.Comments + s.Shares);
+        var averageDaily = rolling7Day / 7.0;
+
+        var platformBreakdown = snapshots
+            .GroupBy(s => s.Platform)
+            .ToDictionary(g => g.Key, g => g.Sum(s => s.Likes + s.Comments + s.Shares))
+            as IReadOnlyDictionary<string, int>;
+
+        var anomalies = new List<EngagementAnomaly>();
+
+        if (snapshots.Count > 0)
+        {
+            var perContent = snapshots
+                .GroupBy(s => new { s.ContentId, s.Platform })
+                .Select(g => new
+                {
+                    g.Key.ContentId,
+                    g.Key.Platform,
+                    TotalEngagement = g.Sum(s => s.Likes + s.Comments + s.Shares),
+                    TopMetric = g.Sum(s => s.Likes) >= g.Sum(s => s.Comments) && g.Sum(s => s.Likes) >= g.Sum(s => s.Shares) ? "likes"
+                        : g.Sum(s => s.Comments) >= g.Sum(s => s.Shares) ? "comments" : "shares",
+                    TopValue = Math.Max(g.Sum(s => s.Likes), Math.Max(g.Sum(s => s.Comments), g.Sum(s => s.Shares)))
+                })
+                .ToList();
+
+            if (perContent.Count > 1)
+            {
+                var avgPerItem = perContent.Average(p => (double)p.TotalEngagement);
+
+                foreach (var item in perContent)
+                {
+                    if (avgPerItem <= 0) continue;
+                    var multiplier = item.TotalEngagement / avgPerItem;
+
+                    if (multiplier > 2.0)
+                    {
+                        var confidence = Math.Min(1.0, Math.Abs(multiplier - 1.0) / 3.0);
+                        anomalies.Add(new EngagementAnomaly(
+                            item.ContentId, item.Platform, item.TopMetric,
+                            item.TopValue, avgPerItem, multiplier, "positive", confidence));
+                    }
+                    else if (multiplier < 0.5)
+                    {
+                        var confidence = Math.Min(1.0, Math.Abs(multiplier - 1.0) / 3.0);
+                        anomalies.Add(new EngagementAnomaly(
+                            item.ContentId, item.Platform, item.TopMetric,
+                            item.TopValue, avgPerItem, multiplier, "negative", confidence));
+                    }
+                }
+            }
+        }
+
+        return Result<EngagementSummaryResponse>.Success(new EngagementSummaryResponse(
+            rolling7Day, averageDaily, anomalies, platformBreakdown));
+    }
+
+    public async Task<Result<BriefingSummaryResponse>> GetBriefingSummaryAsync(CancellationToken ct)
+    {
+        var now = _dateTime.UtcNow;
+        var todayStart = new DateTimeOffset(now.Date, TimeSpan.Zero);
+        var todayEnd = todayStart.AddDays(1);
+        var last24h = now.AddHours(-24);
+
+        var scheduledToday = await _dbContext.CalendarSlots
+            .AsNoTracking()
+            .Where(s => s.ScheduledAt >= todayStart && s.ScheduledAt < todayEnd && s.ContentId != null)
+            .Join(
+                _dbContext.Contents.AsNoTracking(),
+                slot => slot.ContentId,
+                content => content.Id,
+                (slot, content) => new ScheduledContentInfo(
+                    content.Id,
+                    slot.Platform.ToString(),
+                    slot.ScheduledAt,
+                    content.Title))
+            .OrderBy(s => s.Time)
+            .ToListAsync(ct);
+
+        var recentSnapshots = await _dbContext.EngagementSnapshots
+            .AsNoTracking()
+            .Where(s => s.FetchedAt >= last24h)
+            .Join(
+                _dbContext.ContentPlatformStatuses.AsNoTracking(),
+                s => s.ContentPlatformStatusId,
+                cps => cps.Id,
+                (s, cps) => new
+                {
+                    cps.ContentId,
+                    Platform = cps.Platform.ToString(),
+                    Total = s.Likes + s.Comments + s.Shares,
+                    TopMetric = s.Likes >= s.Comments && s.Likes >= s.Shares ? "likes"
+                        : s.Comments >= s.Shares ? "comments" : "shares",
+                    TopValue = Math.Max(s.Likes, Math.Max(s.Comments, s.Shares))
+                })
+            .OrderByDescending(s => s.Total)
+            .Take(3)
+            .ToListAsync(ct);
+
+        var engagementHighlights = recentSnapshots
+            .Select(s => new EngagementHighlight(s.ContentId, s.Platform, s.TopMetric, s.TopValue))
+            .ToList();
+
+        var trendingTopics = await _dbContext.TrendSuggestions
+            .AsNoTracking()
+            .Where(t => t.Status == TrendSuggestionStatus.Pending)
+            .OrderByDescending(t => t.RelevanceScore)
+            .Take(5)
+            .Select(t => new TrendingTopicInfo(t.Topic, t.RelevanceScore, "Trends"))
+            .ToListAsync(ct);
+
+        var queueDepth = await _dbContext.Contents
+            .AsNoTracking()
+            .CountAsync(c => !TerminalStatuses.Contains(c.Status), ct);
+
+        var nextPublish = await _dbContext.Contents
+            .AsNoTracking()
+            .Where(c => c.Status == ContentStatus.Scheduled && c.ScheduledAt != null && c.ScheduledAt > now)
+            .OrderBy(c => c.ScheduledAt)
+            .Select(c => new ScheduledPostInfo(
+                c.Id,
+                c.TargetPlatforms.Length > 0 ? c.TargetPlatforms[0].ToString() : "Unknown",
+                c.ScheduledAt!.Value))
+            .FirstOrDefaultAsync(ct);
+
+        var pendingApprovals = await _dbContext.Contents
+            .AsNoTracking()
+            .CountAsync(c => c.Status == ContentStatus.Review || c.Status == ContentStatus.Approved, ct);
+
+        return Result<BriefingSummaryResponse>.Success(new BriefingSummaryResponse(
+            scheduledToday, engagementHighlights, trendingTopics,
+            queueDepth, nextPublish, pendingApprovals));
+    }
+}
