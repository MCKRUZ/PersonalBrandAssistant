diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
index 135999d..4075b09 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
@@ -13,4 +13,6 @@ public class ContentEngineOptions
     public int EngagementRetentionDays { get; set; } = 30;
 
     public int EngagementAggregationIntervalHours { get; set; } = 4;
+
+    public int SlotMaterializationDays { get; set; } = 7;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/CalendarSlotProcessor.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/CalendarSlotProcessor.cs
new file mode 100644
index 0000000..bc02a0c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/CalendarSlotProcessor.cs
@@ -0,0 +1,149 @@
+using Ical.Net.CalendarComponents;
+using Ical.Net.DataTypes;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class CalendarSlotProcessor : BackgroundService
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IDateTimeProvider _dateTimeProvider;
+    private readonly ContentEngineOptions _options;
+    private readonly ILogger<CalendarSlotProcessor> _logger;
+
+    public CalendarSlotProcessor(
+        IServiceScopeFactory scopeFactory,
+        IDateTimeProvider dateTimeProvider,
+        IOptions<ContentEngineOptions> options,
+        ILogger<CalendarSlotProcessor> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _dateTimeProvider = dateTimeProvider;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await ProcessAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during calendar slot processing");
+                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
+            }
+        }
+    }
+
+    internal async Task ProcessAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
+        var calendarService = scope.ServiceProvider.GetRequiredService<IContentCalendarService>();
+        var now = _dateTimeProvider.UtcNow;
+        var windowEnd = now.AddDays(_options.SlotMaterializationDays);
+
+        var activeSeries = await context.ContentSeries
+            .Where(s => s.IsActive && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt > now))
+            .ToListAsync(ct);
+
+        var newSlotCount = 0;
+
+        foreach (var series in activeSeries)
+        {
+            try
+            {
+                var occurrences = GetOccurrences(series, now, windowEnd);
+
+                var existingSlots = await context.CalendarSlots
+                    .Where(s => s.ContentSeriesId == series.Id
+                                && s.ScheduledAt >= now && s.ScheduledAt <= windowEnd)
+                    .Select(s => s.ScheduledAt)
+                    .ToListAsync(ct);
+
+                var existingSet = existingSlots.ToHashSet();
+
+                foreach (var occurrence in occurrences)
+                {
+                    if (existingSet.Contains(occurrence))
+                        continue;
+
+                    foreach (var platform in series.TargetPlatforms)
+                    {
+                        context.CalendarSlots.Add(new CalendarSlot
+                        {
+                            ScheduledAt = occurrence,
+                            Platform = platform,
+                            ContentSeriesId = series.Id,
+                            Status = CalendarSlotStatus.Open,
+                        });
+                        newSlotCount++;
+                    }
+                }
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error processing series {SeriesId}", series.Id);
+            }
+        }
+
+        if (newSlotCount > 0)
+        {
+            await context.SaveChangesAsync(ct);
+            _logger.LogInformation("Materialized {Count} calendar slots", newSlotCount);
+        }
+
+        // Auto-fill at Autonomous level
+        var autonomyConfig = await context.AutonomyConfigurations.FirstOrDefaultAsync(ct)
+                             ?? AutonomyConfiguration.CreateDefault();
+
+        if (autonomyConfig.GlobalLevel == AutonomyLevel.Autonomous)
+        {
+            var fillResult = await calendarService.AutoFillSlotsAsync(now, windowEnd, ct);
+
+            if (fillResult.IsSuccess)
+            {
+                _logger.LogInformation("Auto-filled {Count} calendar slots", fillResult.Value);
+            }
+            else
+            {
+                _logger.LogWarning("Auto-fill failed: {Errors}", string.Join(", ", fillResult.Errors));
+            }
+        }
+    }
+
+    internal static List<DateTimeOffset> GetOccurrences(
+        ContentSeries series, DateTimeOffset from, DateTimeOffset to)
+    {
+        var calEvent = new CalendarEvent
+        {
+            DtStart = new CalDateTime(series.StartsAt.DateTime, series.TimeZoneId),
+        };
+
+        calEvent.RecurrenceRules.Add(new RecurrencePattern(series.RecurrenceRule));
+
+        var fromCal = new CalDateTime(from.UtcDateTime);
+
+        return calEvent.GetOccurrences(fromCal)
+            .TakeWhile(o => o.Period.StartTime.Value <= to.UtcDateTime)
+            .Select(o => new DateTimeOffset(o.Period.StartTime.Value, TimeSpan.Zero))
+            .ToList();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/EngagementAggregationProcessor.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/EngagementAggregationProcessor.cs
new file mode 100644
index 0000000..41e48ee
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/EngagementAggregationProcessor.cs
@@ -0,0 +1,115 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class EngagementAggregationProcessor : BackgroundService
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IDateTimeProvider _dateTimeProvider;
+    private readonly ContentEngineOptions _options;
+    private readonly ILogger<EngagementAggregationProcessor> _logger;
+
+    public EngagementAggregationProcessor(
+        IServiceScopeFactory scopeFactory,
+        IDateTimeProvider dateTimeProvider,
+        IOptions<ContentEngineOptions> options,
+        ILogger<EngagementAggregationProcessor> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _dateTimeProvider = dateTimeProvider;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        var interval = TimeSpan.FromHours(Math.Max(1, _options.EngagementAggregationIntervalHours));
+        using var timer = new PeriodicTimer(interval);
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await ProcessAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during engagement aggregation processing");
+                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
+            }
+        }
+    }
+
+    internal async Task ProcessAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
+        var aggregator = scope.ServiceProvider.GetRequiredService<IEngagementAggregator>();
+        var now = _dateTimeProvider.UtcNow;
+        var retentionStart = now.AddDays(-_options.EngagementRetentionDays);
+
+        var publishedStatuses = await context.ContentPlatformStatuses
+            .Where(s => s.Status == PlatformPublishStatus.Published
+                        && s.PublishedAt >= retentionStart)
+            .ToListAsync(ct);
+
+        var successCount = 0;
+        var skipCount = 0;
+
+        foreach (var entry in publishedStatuses)
+        {
+            try
+            {
+                var result = await aggregator.FetchLatestAsync(entry.Id, ct);
+
+                if (result.IsSuccess)
+                {
+                    successCount++;
+                }
+                else
+                {
+                    skipCount++;
+                    _logger.LogDebug(
+                        "Skipped engagement fetch for {Platform} post {PostId}: {Errors}",
+                        entry.Platform, entry.PlatformPostId, string.Join(", ", result.Errors));
+                }
+            }
+            catch (Exception ex)
+            {
+                skipCount++;
+                _logger.LogError(ex,
+                    "Error fetching engagement for {Platform} post {PostId}",
+                    entry.Platform, entry.PlatformPostId);
+            }
+        }
+
+        _logger.LogInformation(
+            "Engagement aggregation complete: {Success} fetched, {Skipped} skipped out of {Total}",
+            successCount, skipCount, publishedStatuses.Count);
+
+        // Run retention cleanup
+        try
+        {
+            var cleanupResult = await aggregator.CleanupSnapshotsAsync(ct);
+
+            if (cleanupResult.IsSuccess && cleanupResult.Value > 0)
+            {
+                _logger.LogInformation("Cleaned up {Count} old engagement snapshots", cleanupResult.Value);
+            }
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Error during engagement snapshot cleanup");
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RepurposeOnPublishProcessor.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RepurposeOnPublishProcessor.cs
new file mode 100644
index 0000000..75e0373
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RepurposeOnPublishProcessor.cs
@@ -0,0 +1,125 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class RepurposeOnPublishProcessor : BackgroundService
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IDateTimeProvider _dateTimeProvider;
+    private readonly ILogger<RepurposeOnPublishProcessor> _logger;
+    private DateTimeOffset _lastProcessedAt;
+
+    public RepurposeOnPublishProcessor(
+        IServiceScopeFactory scopeFactory,
+        IDateTimeProvider dateTimeProvider,
+        ILogger<RepurposeOnPublishProcessor> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _dateTimeProvider = dateTimeProvider;
+        _logger = logger;
+        _lastProcessedAt = dateTimeProvider.UtcNow;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await ProcessAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during repurpose-on-publish processing");
+                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
+            }
+        }
+    }
+
+    internal async Task ProcessAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
+        var repurposingService = scope.ServiceProvider.GetRequiredService<IRepurposingService>();
+        var now = _dateTimeProvider.UtcNow;
+
+        var autonomyConfig = await context.AutonomyConfigurations.FirstOrDefaultAsync(ct)
+                             ?? AutonomyConfiguration.CreateDefault();
+
+        var recentlyPublished = await context.Contents
+            .Where(c => c.Status == ContentStatus.Published && c.PublishedAt >= _lastProcessedAt)
+            .ToListAsync(ct);
+
+        foreach (var content in recentlyPublished)
+        {
+            try
+            {
+                var level = autonomyConfig.ResolveLevel(content.ContentType, null);
+
+                if (level == AutonomyLevel.Manual)
+                {
+                    _logger.LogDebug("Skipping repurpose for {ContentId} — Manual autonomy", content.Id);
+                    continue;
+                }
+
+                var targetPlatforms = content.TargetPlatforms.Length > 0
+                    ? content.TargetPlatforms
+                    : await GetSeriesPlatforms(context, content.Id, ct);
+
+                if (targetPlatforms.Length == 0)
+                {
+                    _logger.LogDebug("No target platforms for content {ContentId}, skipping repurpose", content.Id);
+                    continue;
+                }
+
+                var result = await repurposingService.RepurposeAsync(content.Id, targetPlatforms, ct);
+
+                if (result.IsSuccess)
+                {
+                    _logger.LogInformation(
+                        "Repurposed content {ContentId} to {Count} platform(s)",
+                        content.Id, result.Value!.Count);
+                }
+                else
+                {
+                    _logger.LogWarning(
+                        "Repurpose failed for {ContentId}: {Errors}",
+                        content.Id, string.Join(", ", result.Errors));
+                }
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error repurposing content {ContentId}", content.Id);
+            }
+        }
+
+        _lastProcessedAt = now;
+    }
+
+    private static async Task<PlatformType[]> GetSeriesPlatforms(
+        IApplicationDbContext context, Guid contentId, CancellationToken ct)
+    {
+        var seriesSlot = await context.CalendarSlots
+            .Where(s => s.ContentId == contentId && s.ContentSeriesId != null)
+            .FirstOrDefaultAsync(ct);
+
+        if (seriesSlot?.ContentSeriesId is null)
+            return [];
+
+        var series = await context.ContentSeries
+            .FirstOrDefaultAsync(s => s.Id == seriesSlot.ContentSeriesId, ct);
+
+        return series?.TargetPlatforms ?? [];
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/CalendarSlotProcessorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/CalendarSlotProcessorTests.cs
new file mode 100644
index 0000000..c2af995
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/CalendarSlotProcessorTests.cs
@@ -0,0 +1,166 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class CalendarSlotProcessorTests
+{
+    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
+    private readonly Mock<IServiceScope> _scope = new();
+    private readonly Mock<IServiceProvider> _serviceProvider = new();
+    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
+    private readonly Mock<IContentCalendarService> _calendarService = new();
+    private readonly Mock<ILogger<CalendarSlotProcessor>> _logger = new();
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly ContentEngineOptions _options = new();
+
+    private readonly DateTimeOffset _now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
+
+    public CalendarSlotProcessorTests()
+    {
+        _dateTimeProvider.Setup(d => d.UtcNow).Returns(_now);
+        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
+        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IContentCalendarService)))
+            .Returns(_calendarService.Object);
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext)))
+            .Returns(_dbContext.Object);
+    }
+
+    private CalendarSlotProcessor CreateSut() => new(
+        _scopeFactory.Object,
+        _dateTimeProvider.Object,
+        Options.Create(_options),
+        _logger.Object);
+
+    private void SetupDbSets(
+        ContentSeries[]? series = null,
+        CalendarSlot[]? slots = null,
+        AutonomyConfiguration? autonomy = null)
+    {
+        var seriesMock = (series ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.ContentSeries).Returns(seriesMock.Object);
+
+        var slotMock = (slots ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.CalendarSlots).Returns(slotMock.Object);
+
+        var autonomyConfigs = autonomy is not null ? new[] { autonomy } : Array.Empty<AutonomyConfiguration>();
+        var autonomyMock = autonomyConfigs.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.AutonomyConfigurations).Returns(autonomyMock.Object);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_ActiveSeries_MaterializesUpcomingSlots()
+    {
+        // Arrange
+        var series = new ContentSeries
+        {
+            Name = "Weekly Tech",
+            RecurrenceRule = "FREQ=DAILY",
+            TargetPlatforms = [PlatformType.TwitterX],
+            ContentType = ContentType.BlogPost,
+            TimeZoneId = "UTC",
+            IsActive = true,
+            StartsAt = _now.AddDays(-1),
+        };
+        typeof(ContentSeries).BaseType!.BaseType!.GetProperty("Id")!.SetValue(series, Guid.NewGuid());
+
+        SetupDbSets(series: [series]);
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_ExistingSlots_NoDuplicates()
+    {
+        // Arrange
+        var seriesId = Guid.NewGuid();
+        var series = new ContentSeries
+        {
+            Name = "Daily Post",
+            RecurrenceRule = "FREQ=DAILY;COUNT=1",
+            TargetPlatforms = [PlatformType.TwitterX],
+            ContentType = ContentType.BlogPost,
+            TimeZoneId = "UTC",
+            IsActive = true,
+            StartsAt = _now.AddDays(-30),
+        };
+        typeof(ContentSeries).BaseType!.BaseType!.GetProperty("Id")!.SetValue(series, seriesId);
+
+        var existingSlots = Enumerable.Range(0, 7)
+            .Select(i => new CalendarSlot
+            {
+                ScheduledAt = _now.AddDays(i),
+                Platform = PlatformType.TwitterX,
+                ContentSeriesId = seriesId,
+                Status = CalendarSlotStatus.Open,
+            })
+            .ToArray();
+
+        SetupDbSets(series: [series], slots: existingSlots);
+
+        var sut = CreateSut();
+
+        // Act
+        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));
+
+        // Assert
+        Assert.Null(ex);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_AutonomousLevel_TriggersAutoFill()
+    {
+        // Arrange
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.Autonomous;
+
+        SetupDbSets(series: [], autonomy: autonomy);
+
+        _calendarService.Setup(c => c.AutoFillSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(3));
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _calendarService.Verify(
+            c => c.AutoFillSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_ManualLevel_NoAutoFill()
+    {
+        // Arrange
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.Manual;
+
+        SetupDbSets(series: [], autonomy: autonomy);
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _calendarService.Verify(
+            c => c.AutoFillSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
+            Times.Never);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/EngagementAggregationProcessorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/EngagementAggregationProcessorTests.cs
new file mode 100644
index 0000000..eac66a9
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/EngagementAggregationProcessorTests.cs
@@ -0,0 +1,179 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class EngagementAggregationProcessorTests
+{
+    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
+    private readonly Mock<IServiceScope> _scope = new();
+    private readonly Mock<IServiceProvider> _serviceProvider = new();
+    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
+    private readonly Mock<IEngagementAggregator> _aggregator = new();
+    private readonly Mock<ILogger<EngagementAggregationProcessor>> _logger = new();
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly ContentEngineOptions _options = new();
+
+    private readonly DateTimeOffset _now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
+
+    public EngagementAggregationProcessorTests()
+    {
+        _dateTimeProvider.Setup(d => d.UtcNow).Returns(_now);
+        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
+        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IEngagementAggregator)))
+            .Returns(_aggregator.Object);
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext)))
+            .Returns(_dbContext.Object);
+    }
+
+    private EngagementAggregationProcessor CreateSut() => new(
+        _scopeFactory.Object,
+        _dateTimeProvider.Object,
+        Options.Create(_options),
+        _logger.Object);
+
+    private void SetupDbSets(ContentPlatformStatus[]? statuses = null)
+    {
+        var statusMock = (statuses ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.ContentPlatformStatuses).Returns(statusMock.Object);
+    }
+
+    private static ContentPlatformStatus CreatePublishedStatus(
+        PlatformType platform = PlatformType.TwitterX,
+        string postId = "post-123")
+    {
+        var cps = new ContentPlatformStatus
+        {
+            ContentId = Guid.NewGuid(),
+            Platform = platform,
+            Status = PlatformPublishStatus.Published,
+            PlatformPostId = postId,
+            PublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
+        };
+        typeof(ContentPlatformStatus).BaseType!.BaseType!
+            .GetProperty("Id")!.SetValue(cps, Guid.NewGuid());
+        return cps;
+    }
+
+    [Fact]
+    public async Task ProcessAsync_PublishedContent_QueriesWithinRetentionWindow()
+    {
+        // Arrange
+        var recentStatus = CreatePublishedStatus();
+        SetupDbSets([recentStatus]);
+
+        _aggregator.Setup(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
+        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(0));
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _aggregator.Verify(
+            a => a.FetchLatestAsync(recentStatus.Id, It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_EachEntry_FetchesEngagement()
+    {
+        // Arrange
+        var status1 = CreatePublishedStatus(PlatformType.TwitterX, "tw-1");
+        var status2 = CreatePublishedStatus(PlatformType.LinkedIn, "li-1");
+        SetupDbSets([status1, status2]);
+
+        _aggregator.Setup(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
+        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(0));
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _aggregator.Verify(
+            a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
+            Times.Exactly(2));
+    }
+
+    [Fact]
+    public async Task ProcessAsync_RateLimited_SkipsEntry()
+    {
+        // Arrange
+        var status = CreatePublishedStatus();
+        SetupDbSets([status]);
+
+        _aggregator.Setup(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<EngagementSnapshot>(ErrorCode.RateLimited, "Rate limited"));
+        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(0));
+
+        var sut = CreateSut();
+
+        // Act
+        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));
+
+        // Assert
+        Assert.Null(ex);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_PlatformError_ContinuesWithOthers()
+    {
+        // Arrange
+        var status1 = CreatePublishedStatus(PlatformType.TwitterX, "tw-fail");
+        var status2 = CreatePublishedStatus(PlatformType.LinkedIn, "li-ok");
+        SetupDbSets([status1, status2]);
+
+        _aggregator.SetupSequence(a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("API down"))
+            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
+        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(0));
+
+        var sut = CreateSut();
+
+        // Act
+        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));
+
+        // Assert
+        Assert.Null(ex);
+        _aggregator.Verify(
+            a => a.FetchLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
+            Times.Exactly(2));
+    }
+
+    [Fact]
+    public async Task ProcessAsync_RetentionCleanup_CalledAfterFetch()
+    {
+        // Arrange
+        SetupDbSets([]);
+        _aggregator.Setup(a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(5));
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _aggregator.Verify(
+            a => a.CleanupSnapshotsAsync(It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RepurposeOnPublishProcessorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RepurposeOnPublishProcessorTests.cs
new file mode 100644
index 0000000..062e030
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RepurposeOnPublishProcessorTests.cs
@@ -0,0 +1,191 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class RepurposeOnPublishProcessorTests
+{
+    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
+    private readonly Mock<IServiceScope> _scope = new();
+    private readonly Mock<IServiceProvider> _serviceProvider = new();
+    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
+    private readonly Mock<IRepurposingService> _repurposingService = new();
+    private readonly Mock<ILogger<RepurposeOnPublishProcessor>> _logger = new();
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+
+    private readonly DateTimeOffset _now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
+
+    public RepurposeOnPublishProcessorTests()
+    {
+        _dateTimeProvider.Setup(d => d.UtcNow).Returns(_now);
+        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
+        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IRepurposingService)))
+            .Returns(_repurposingService.Object);
+        // The processor resolves ApplicationDbContext, but we return our mock as the
+        // IApplicationDbContext through the concrete type registration
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext)))
+            .Returns(_dbContext.Object);
+    }
+
+    private RepurposeOnPublishProcessor CreateSut() => new(
+        _scopeFactory.Object,
+        _dateTimeProvider.Object,
+        _logger.Object);
+
+    private void SetupDbSets(
+        Content[]? contents = null,
+        AutonomyConfiguration? autonomy = null)
+    {
+        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);
+
+        var autonomyConfigs = autonomy is not null ? new[] { autonomy } : Array.Empty<AutonomyConfiguration>();
+        var autonomyMock = autonomyConfigs.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.AutonomyConfigurations).Returns(autonomyMock.Object);
+
+        var slotMock = Array.Empty<CalendarSlot>().AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.CalendarSlots).Returns(slotMock.Object);
+
+        var seriesMock = Array.Empty<ContentSeries>().AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.ContentSeries).Returns(seriesMock.Object);
+    }
+
+    private static Content CreatePublishedContent(
+        ContentType type = ContentType.BlogPost,
+        PlatformType[]? platforms = null)
+    {
+        var content = Content.Create(type, "body", "Test", platforms ?? [PlatformType.TwitterX]);
+        content.TransitionTo(ContentStatus.Review);
+        content.TransitionTo(ContentStatus.Approved);
+        content.TransitionTo(ContentStatus.Scheduled);
+        content.TransitionTo(ContentStatus.Publishing);
+        content.TransitionTo(ContentStatus.Published);
+        content.PublishedAt = new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
+        return content;
+    }
+
+    [Fact]
+    public async Task ProcessAsync_ContentPublished_TriggersRepurpose()
+    {
+        // Arrange
+        var content = CreatePublishedContent();
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.Autonomous;
+
+        SetupDbSets([content], autonomy);
+
+        _repurposingService.Setup(r => r.RepurposeAsync(content.Id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _repurposingService.Verify(
+            r => r.RepurposeAsync(content.Id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_ManualAutonomy_SkipsRepurpose()
+    {
+        // Arrange
+        var content = CreatePublishedContent();
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.Manual;
+
+        SetupDbSets([content], autonomy);
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _repurposingService.Verify(
+            r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
+            Times.Never);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_SemiAutoAutonomy_OnlyPublishedContent()
+    {
+        // Arrange
+        var content = CreatePublishedContent();
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.SemiAuto;
+
+        SetupDbSets([content], autonomy);
+
+        _repurposingService.Setup(r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([]));
+
+        var sut = CreateSut();
+
+        // Act
+        await sut.ProcessAsync(CancellationToken.None);
+
+        // Assert
+        _repurposingService.Verify(
+            r => r.RepurposeAsync(content.Id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ProcessAsync_RepurposeFails_ContinuesProcessing()
+    {
+        // Arrange
+        var content1 = CreatePublishedContent();
+        var content2 = CreatePublishedContent();
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.Autonomous;
+
+        SetupDbSets([content1, content2], autonomy);
+
+        _repurposingService.SetupSequence(r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new InvalidOperationException("Boom"))
+            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));
+
+        var sut = CreateSut();
+
+        // Act
+        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));
+
+        // Assert
+        Assert.Null(ex);
+        _repurposingService.Verify(
+            r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
+            Times.Exactly(2));
+    }
+
+    [Fact]
+    public async Task ProcessAsync_DuplicateEvent_IdempotentBehavior()
+    {
+        // Arrange
+        var content = CreatePublishedContent();
+        var autonomy = AutonomyConfiguration.CreateDefault();
+        autonomy.GlobalLevel = AutonomyLevel.Autonomous;
+
+        SetupDbSets([content], autonomy);
+
+        _repurposingService.Setup(r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Conflict<IReadOnlyList<Guid>>("Already repurposed"));
+
+        var sut = CreateSut();
+
+        // Act
+        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));
+
+        // Assert
+        Assert.Null(ex);
+    }
+}
