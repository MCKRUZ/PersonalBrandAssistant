diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IContentCalendarService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IContentCalendarService.cs
new file mode 100644
index 0000000..0d2026d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IContentCalendarService.cs
@@ -0,0 +1,13 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IContentCalendarService
+{
+    Task<Result<IReadOnlyList<CalendarSlot>>> GetSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+    Task<Result<Guid>> CreateSeriesAsync(ContentSeriesRequest request, CancellationToken ct);
+    Task<Result<Guid>> CreateManualSlotAsync(CalendarSlotRequest request, CancellationToken ct);
+    Task<Result<MediatR.Unit>> AssignContentAsync(Guid slotId, Guid contentId, CancellationToken ct);
+    Task<Result<int>> AutoFillSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/CalendarSlotRequest.cs b/src/PersonalBrandAssistant.Application/Common/Models/CalendarSlotRequest.cs
new file mode 100644
index 0000000..35d4c67
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/CalendarSlotRequest.cs
@@ -0,0 +1,7 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record CalendarSlotRequest(
+    DateTimeOffset ScheduledAt,
+    PlatformType Platform);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentSeriesRequest.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentSeriesRequest.cs
new file mode 100644
index 0000000..ddb43a0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentSeriesRequest.cs
@@ -0,0 +1,14 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record ContentSeriesRequest(
+    string Name,
+    string? Description,
+    string RecurrenceRule,
+    PlatformType[] TargetPlatforms,
+    ContentType ContentType,
+    List<string> ThemeTags,
+    string TimeZoneId,
+    DateTimeOffset StartsAt,
+    DateTimeOffset? EndsAt);
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index f465d6a..ef50eab 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -81,6 +81,7 @@ public static class DependencyInjection
         services.AddScoped<IBrandVoiceService, StubBrandVoiceService>();
         services.AddScoped<IContentPipeline, ContentPipeline>();
         services.AddScoped<IRepurposingService, RepurposingService>();
+        services.AddScoped<IContentCalendarService, ContentCalendarService>();
 
         // Platform integration options
         services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
diff --git a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
index 4675c46..861013f 100644
--- a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
+++ b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
@@ -12,6 +12,7 @@
   <ItemGroup>
     <PackageReference Include="Anthropic" Version="12.8.0" />
     <PackageReference Include="Fluid.Core" Version="2.31.0" />
+    <PackageReference Include="Ical.Net" Version="5.2.1" />
     <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="10.0.5" />
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
     <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentCalendarService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentCalendarService.cs
new file mode 100644
index 0000000..fae540e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentCalendarService.cs
@@ -0,0 +1,262 @@
+using Ical.Net;
+using Ical.Net.CalendarComponents;
+using Ical.Net.DataTypes;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+public sealed class ContentCalendarService : IContentCalendarService
+{
+    private readonly IApplicationDbContext _dbContext;
+    private readonly ILogger<ContentCalendarService> _logger;
+
+    public ContentCalendarService(
+        IApplicationDbContext dbContext,
+        ILogger<ContentCalendarService> logger)
+    {
+        _dbContext = dbContext;
+        _logger = logger;
+    }
+
+    public async Task<Result<IReadOnlyList<CalendarSlot>>> GetSlotsAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        var activeSeries = await _dbContext.ContentSeries
+            .Where(s => s.IsActive && s.StartsAt <= to && (s.EndsAt == null || s.EndsAt >= from))
+            .ToListAsync(ct);
+
+        var materializedSlots = await _dbContext.CalendarSlots
+            .Where(s => s.ScheduledAt >= from && s.ScheduledAt <= to)
+            .ToListAsync(ct);
+
+        var result = new List<CalendarSlot>();
+
+        // Add generated occurrences from active series
+        foreach (var series in activeSeries)
+        {
+            var occurrences = GenerateOccurrences(series, from, to);
+
+            foreach (var occurrence in occurrences)
+            {
+                // Check if already materialized
+                var existing = materializedSlots.FirstOrDefault(s =>
+                    s.ContentSeriesId == series.Id &&
+                    Math.Abs((s.ScheduledAt - occurrence).TotalMinutes) < 1);
+
+                if (existing is not null)
+                {
+                    result.Add(existing);
+                    materializedSlots.Remove(existing);
+                }
+                else
+                {
+                    // Create transient slot
+                    result.Add(new CalendarSlot
+                    {
+                        ScheduledAt = occurrence,
+                        Platform = series.TargetPlatforms.Length > 0
+                            ? series.TargetPlatforms[0]
+                            : PlatformType.TwitterX,
+                        ContentSeriesId = series.Id,
+                        Status = CalendarSlotStatus.Open,
+                    });
+                }
+            }
+        }
+
+        // Add remaining materialized slots (manual slots or orphaned)
+        result.AddRange(materializedSlots);
+
+        return Result<IReadOnlyList<CalendarSlot>>.Success(
+            result.OrderBy(s => s.ScheduledAt).ToList());
+    }
+
+    public async Task<Result<Guid>> CreateSeriesAsync(ContentSeriesRequest request, CancellationToken ct)
+    {
+        if (!TryParseRRule(request.RecurrenceRule, out _))
+        {
+            return Result<Guid>.Failure(ErrorCode.ValidationFailed, "Invalid RRULE format");
+        }
+
+        var series = new ContentSeries
+        {
+            Name = request.Name,
+            Description = request.Description,
+            RecurrenceRule = request.RecurrenceRule,
+            TargetPlatforms = request.TargetPlatforms,
+            ContentType = request.ContentType,
+            ThemeTags = request.ThemeTags,
+            TimeZoneId = request.TimeZoneId,
+            IsActive = true,
+            StartsAt = request.StartsAt,
+            EndsAt = request.EndsAt,
+        };
+
+        _dbContext.ContentSeries.Add(series);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<Guid>.Success(series.Id);
+    }
+
+    public async Task<Result<Guid>> CreateManualSlotAsync(CalendarSlotRequest request, CancellationToken ct)
+    {
+        var slot = new CalendarSlot
+        {
+            ScheduledAt = request.ScheduledAt,
+            Platform = request.Platform,
+            ContentSeriesId = null,
+            Status = CalendarSlotStatus.Open,
+        };
+
+        _dbContext.CalendarSlots.Add(slot);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<Guid>.Success(slot.Id);
+    }
+
+    public async Task<Result<MediatR.Unit>> AssignContentAsync(
+        Guid slotId, Guid contentId, CancellationToken ct)
+    {
+        var slot = await _dbContext.CalendarSlots.FindAsync([slotId], ct);
+        if (slot is null)
+        {
+            return Result<MediatR.Unit>.NotFound($"Slot {slotId} not found");
+        }
+
+        if (slot.Status != CalendarSlotStatus.Open)
+        {
+            return Result<MediatR.Unit>.Conflict($"Slot is already {slot.Status}");
+        }
+
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found");
+        }
+
+        slot.ContentId = contentId;
+        slot.Status = CalendarSlotStatus.Filled;
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+    }
+
+    public async Task<Result<int>> AutoFillSlotsAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        var openSlots = await _dbContext.CalendarSlots
+            .Where(s => s.ScheduledAt >= from && s.ScheduledAt <= to && s.Status == CalendarSlotStatus.Open)
+            .ToListAsync(ct);
+
+        // Load all series for theme tag matching
+        var seriesIds = openSlots
+            .Where(s => s.ContentSeriesId.HasValue)
+            .Select(s => s.ContentSeriesId!.Value)
+            .Distinct()
+            .ToList();
+        var seriesMap = await _dbContext.ContentSeries
+            .Where(s => seriesIds.Contains(s.Id))
+            .ToDictionaryAsync(s => s.Id, ct);
+
+        // Load approved content not yet assigned to any slot
+        var assignedContentIds = await _dbContext.CalendarSlots
+            .Where(s => s.ContentId != null)
+            .Select(s => s.ContentId!.Value)
+            .ToListAsync(ct);
+
+        var candidates = await _dbContext.Contents
+            .Where(c => c.Status == ContentStatus.Approved && !assignedContentIds.Contains(c.Id))
+            .OrderBy(c => c.CreatedAt)
+            .ToListAsync(ct);
+
+        var filled = 0;
+        var usedContentIds = new HashSet<Guid>();
+
+        foreach (var slot in openSlots.OrderBy(s => s.ScheduledAt))
+        {
+            var matching = candidates
+                .Where(c => c.TargetPlatforms.Contains(slot.Platform) && !usedContentIds.Contains(c.Id))
+                .ToList();
+
+            if (matching.Count == 0) continue;
+
+            // Score by theme tag affinity
+            Content? best = null;
+            var bestScore = -1;
+
+            if (slot.ContentSeriesId.HasValue &&
+                seriesMap.TryGetValue(slot.ContentSeriesId.Value, out var series))
+            {
+                foreach (var candidate in matching)
+                {
+                    var score = series.ThemeTags
+                        .Count(tag => candidate.Metadata.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
+                    if (score > bestScore)
+                    {
+                        bestScore = score;
+                        best = candidate;
+                    }
+                }
+            }
+
+            best ??= matching[0];
+
+            slot.ContentId = best.Id;
+            slot.Status = CalendarSlotStatus.Filled;
+            usedContentIds.Add(best.Id);
+            filled++;
+        }
+
+        if (filled > 0)
+        {
+            await _dbContext.SaveChangesAsync(ct);
+        }
+
+        return Result<int>.Success(filled);
+    }
+
+    private static List<DateTimeOffset> GenerateOccurrences(
+        ContentSeries series, DateTimeOffset from, DateTimeOffset to)
+    {
+        try
+        {
+            var calEvent = new CalendarEvent
+            {
+                DtStart = new CalDateTime(series.StartsAt.DateTime, series.TimeZoneId),
+            };
+
+            calEvent.RecurrenceRules.Add(new RecurrencePattern(series.RecurrenceRule));
+
+            var fromCal = new CalDateTime(from.UtcDateTime);
+
+            return calEvent.GetOccurrences(fromCal)
+                .TakeWhile(o => o.Period.StartTime.Value <= to.UtcDateTime)
+                .Select(o => new DateTimeOffset(o.Period.StartTime.Value, TimeSpan.Zero))
+                .ToList();
+        }
+        catch
+        {
+            return [];
+        }
+    }
+
+    private static bool TryParseRRule(string rrule, out RecurrencePattern? pattern)
+    {
+        try
+        {
+            pattern = new RecurrencePattern(rrule);
+            return true;
+        }
+        catch
+        {
+            pattern = null;
+            return false;
+        }
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/ContentCalendarServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/ContentCalendarServiceTests.cs
new file mode 100644
index 0000000..34982ef
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/ContentCalendarServiceTests.cs
@@ -0,0 +1,347 @@
+using Microsoft.Extensions.Logging;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;
+
+public class ContentCalendarServiceTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<ILogger<ContentCalendarService>> _logger = new();
+
+    private ContentCalendarService CreateSut() => new(_dbContext.Object, _logger.Object);
+
+    private void SetupDbSets(
+        ContentSeries[]? series = null,
+        CalendarSlot[]? slots = null,
+        Content[]? contents = null)
+    {
+        var seriesMock = (series ?? []).AsQueryable().BuildMockDbSet();
+        seriesMock.Setup(d => d.Add(It.IsAny<ContentSeries>()));
+        _dbContext.Setup(d => d.ContentSeries).Returns(seriesMock.Object);
+
+        var slotMock = (slots ?? []).AsQueryable().BuildMockDbSet();
+        slotMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .Returns<object[], CancellationToken>((keys, _) =>
+                ValueTask.FromResult((slots ?? []).FirstOrDefault(s => s.Id == (Guid)keys[0])));
+        slotMock.Setup(d => d.Add(It.IsAny<CalendarSlot>()));
+        _dbContext.Setup(d => d.CalendarSlots).Returns(slotMock.Object);
+
+        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
+        contentMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .Returns<object[], CancellationToken>((keys, _) =>
+                ValueTask.FromResult((contents ?? []).FirstOrDefault(c => c.Id == (Guid)keys[0])));
+        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);
+    }
+
+    // --- CreateSeriesAsync ---
+
+    [Fact]
+    public async Task CreateSeriesAsync_WithValidRRule_CreatesSeriesEntity()
+    {
+        SetupDbSets();
+
+        var request = new ContentSeriesRequest(
+            "Weekly LinkedIn Posts", null,
+            "FREQ=WEEKLY;BYDAY=TU",
+            [PlatformType.LinkedIn], ContentType.SocialPost,
+            ["dotnet"], "America/New_York",
+            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
+
+        var sut = CreateSut();
+        var result = await sut.CreateSeriesAsync(request, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotEqual(Guid.Empty, result.Value);
+        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task CreateSeriesAsync_WithInvalidRRule_ReturnsValidationFailure()
+    {
+        SetupDbSets();
+
+        var request = new ContentSeriesRequest(
+            "Bad Series", null,
+            "NOT_A_VALID_RRULE",
+            [PlatformType.TwitterX], ContentType.Thread,
+            [], "UTC",
+            DateTimeOffset.UtcNow, null);
+
+        var sut = CreateSut();
+        var result = await sut.CreateSeriesAsync(request, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    // --- CreateManualSlotAsync ---
+
+    [Fact]
+    public async Task CreateManualSlotAsync_CreatesSlotWithNoSeriesReference()
+    {
+        SetupDbSets();
+
+        var request = new CalendarSlotRequest(
+            new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
+            PlatformType.Instagram);
+
+        var sut = CreateSut();
+        var result = await sut.CreateManualSlotAsync(request, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _dbContext.Verify(d => d.CalendarSlots.Add(
+            It.Is<CalendarSlot>(s => s.ContentSeriesId == null && s.Status == CalendarSlotStatus.Open)),
+            Times.Once);
+    }
+
+    // --- AssignContentAsync ---
+
+    [Fact]
+    public async Task AssignContentAsync_WithOpenSlot_FillsAndChangesStatus()
+    {
+        var slot = new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
+            Platform = PlatformType.TwitterX,
+            Status = CalendarSlotStatus.Open,
+        };
+        var content = Content.Create(ContentType.Thread, "tweet thread", targetPlatforms: [PlatformType.TwitterX]);
+        SetupDbSets(slots: [slot], contents: [content]);
+
+        var sut = CreateSut();
+        var result = await sut.AssignContentAsync(slot.Id, content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(content.Id, slot.ContentId);
+        Assert.Equal(CalendarSlotStatus.Filled, slot.Status);
+    }
+
+    [Fact]
+    public async Task AssignContentAsync_WithAlreadyFilledSlot_ReturnsConflict()
+    {
+        var slot = new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
+            Platform = PlatformType.TwitterX,
+            Status = CalendarSlotStatus.Filled,
+            ContentId = Guid.NewGuid(),
+        };
+        SetupDbSets(slots: [slot]);
+
+        var sut = CreateSut();
+        var result = await sut.AssignContentAsync(slot.Id, Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task AssignContentAsync_SlotNotFound_ReturnsNotFound()
+    {
+        SetupDbSets();
+
+        var sut = CreateSut();
+        var result = await sut.AssignContentAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // --- GetSlotsAsync ---
+
+    [Fact]
+    public async Task GetSlotsAsync_IncludesManualSlotsWithNoSeriesReference()
+    {
+        var manualSlot = new CalendarSlot
+        {
+            ScheduledAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
+            Platform = PlatformType.Instagram,
+            ContentSeriesId = null,
+            Status = CalendarSlotStatus.Open,
+        };
+        SetupDbSets(slots: [manualSlot]);
+
+        var sut = CreateSut();
+        var result = await sut.GetSlotsAsync(
+            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
+            new DateTimeOffset(2026, 3, 31, 23, 59, 59, TimeSpan.Zero),
+            CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+        Assert.Null(result.Value![0].ContentSeriesId);
+    }
+
+    [Fact]
+    public async Task GetSlotsAsync_MergesMaterializedSlotsWithGeneratedOccurrences()
+    {
+        var series = new ContentSeries
+        {
+            Name = "Weekly",
+            RecurrenceRule = "FREQ=WEEKLY;BYDAY=TU",
+            TargetPlatforms = [PlatformType.LinkedIn],
+            ContentType = ContentType.SocialPost,
+            ThemeTags = [],
+            TimeZoneId = "UTC",
+            IsActive = true,
+            StartsAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
+        };
+
+        var materializedSlot = new CalendarSlot
+        {
+            ScheduledAt = new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero), // Tuesday
+            Platform = PlatformType.LinkedIn,
+            ContentSeriesId = series.Id,
+            ContentId = Guid.NewGuid(),
+            Status = CalendarSlotStatus.Filled,
+        };
+
+        SetupDbSets(series: [series], slots: [materializedSlot]);
+
+        var sut = CreateSut();
+        var result = await sut.GetSlotsAsync(
+            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
+            new DateTimeOffset(2026, 3, 7, 23, 59, 59, TimeSpan.Zero),
+            CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        // Should contain the materialized slot (Filled), not a duplicate generated one
+        var filledSlots = result.Value!.Where(s => s.Status == CalendarSlotStatus.Filled).ToList();
+        Assert.Single(filledSlots);
+        Assert.Equal(materializedSlot.ContentId, filledSlots[0].ContentId);
+    }
+
+    // --- AutoFillSlotsAsync ---
+
+    [Fact]
+    public async Task AutoFillSlotsAsync_MatchesContentToSlotsByPlatform()
+    {
+        var twitterSlot = new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
+            Platform = PlatformType.TwitterX,
+            Status = CalendarSlotStatus.Open,
+        };
+
+        var twitterContent = Content.Create(ContentType.Thread, "twitter thread", targetPlatforms: [PlatformType.TwitterX]);
+        // Simulate Approved status via reflection or factory
+        typeof(Content).GetProperty("Status")!.SetValue(twitterContent, ContentStatus.Approved);
+
+        var linkedInContent = Content.Create(ContentType.SocialPost, "linkedin post", targetPlatforms: [PlatformType.LinkedIn]);
+        typeof(Content).GetProperty("Status")!.SetValue(linkedInContent, ContentStatus.Approved);
+
+        SetupDbSets(slots: [twitterSlot], contents: [twitterContent, linkedInContent]);
+
+        var sut = CreateSut();
+        var result = await sut.AutoFillSlotsAsync(
+            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(1, result.Value);
+        Assert.Equal(twitterContent.Id, twitterSlot.ContentId);
+    }
+
+    [Fact]
+    public async Task AutoFillSlotsAsync_SkipsAlreadyFilledSlots()
+    {
+        var openSlot = new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
+            Platform = PlatformType.TwitterX,
+            Status = CalendarSlotStatus.Open,
+        };
+        var filledSlot = new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(2),
+            Platform = PlatformType.TwitterX,
+            Status = CalendarSlotStatus.Filled,
+            ContentId = Guid.NewGuid(),
+        };
+
+        var content = Content.Create(ContentType.Thread, "thread", targetPlatforms: [PlatformType.TwitterX]);
+        typeof(Content).GetProperty("Status")!.SetValue(content, ContentStatus.Approved);
+
+        SetupDbSets(slots: [openSlot, filledSlot], contents: [content]);
+
+        var sut = CreateSut();
+        var result = await sut.AutoFillSlotsAsync(
+            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);
+
+        Assert.Equal(1, result.Value);
+    }
+
+    [Fact]
+    public async Task AutoFillSlotsAsync_ReturnsCountOfSlotsFilled()
+    {
+        var slots = Enumerable.Range(1, 3).Select(i => new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(i),
+            Platform = PlatformType.LinkedIn,
+            Status = CalendarSlotStatus.Open,
+        }).ToArray();
+
+        var contents = Enumerable.Range(1, 2).Select(i =>
+        {
+            var c = Content.Create(ContentType.SocialPost, $"post {i}", targetPlatforms: [PlatformType.LinkedIn]);
+            typeof(Content).GetProperty("Status")!.SetValue(c, ContentStatus.Approved);
+            return c;
+        }).ToArray();
+
+        SetupDbSets(slots: slots, contents: contents);
+
+        var sut = CreateSut();
+        var result = await sut.AutoFillSlotsAsync(
+            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);
+
+        Assert.Equal(2, result.Value);
+    }
+
+    [Fact]
+    public async Task AutoFillSlotsAsync_ConsidersThemeTagAffinity()
+    {
+        var series = new ContentSeries
+        {
+            Name = "DotNet Series",
+            RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO",
+            TargetPlatforms = [PlatformType.LinkedIn],
+            ContentType = ContentType.SocialPost,
+            ThemeTags = ["dotnet", "csharp"],
+            TimeZoneId = "UTC",
+            IsActive = true,
+            StartsAt = DateTimeOffset.UtcNow.AddMonths(-1),
+        };
+
+        var slot = new CalendarSlot
+        {
+            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
+            Platform = PlatformType.LinkedIn,
+            ContentSeriesId = series.Id,
+            Status = CalendarSlotStatus.Open,
+        };
+
+        var dotnetContent = Content.Create(ContentType.SocialPost, "dotnet post", targetPlatforms: [PlatformType.LinkedIn]);
+        typeof(Content).GetProperty("Status")!.SetValue(dotnetContent, ContentStatus.Approved);
+        dotnetContent.Metadata.Tags.Add("dotnet");
+
+        var cookingContent = Content.Create(ContentType.SocialPost, "cooking post", targetPlatforms: [PlatformType.LinkedIn]);
+        typeof(Content).GetProperty("Status")!.SetValue(cookingContent, ContentStatus.Approved);
+        cookingContent.Metadata.Tags.Add("cooking");
+
+        SetupDbSets(series: [series], slots: [slot], contents: [cookingContent, dotnetContent]);
+
+        var sut = CreateSut();
+        var result = await sut.AutoFillSlotsAsync(
+            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);
+
+        Assert.Equal(1, result.Value);
+        Assert.Equal(dotnetContent.Id, slot.ContentId);
+    }
+}
