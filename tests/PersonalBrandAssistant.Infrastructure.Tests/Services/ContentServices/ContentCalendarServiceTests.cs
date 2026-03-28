using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

public class ContentCalendarServiceTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ILogger<ContentCalendarService>> _logger = new();

    private ContentCalendarService CreateSut() => new(_dbContext.Object, _logger.Object);

    private void SetupDbSets(
        ContentSeries[]? series = null,
        CalendarSlot[]? slots = null,
        Content[]? contents = null)
    {
        var seriesMock = (series ?? []).AsQueryable().BuildMockDbSet();
        seriesMock.Setup(d => d.Add(It.IsAny<ContentSeries>()));
        _dbContext.Setup(d => d.ContentSeries).Returns(seriesMock.Object);

        var slotMock = (slots ?? []).AsQueryable().BuildMockDbSet();
        slotMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                ValueTask.FromResult((slots ?? []).FirstOrDefault(s => s.Id == (Guid)keys[0])));
        slotMock.Setup(d => d.Add(It.IsAny<CalendarSlot>()));
        _dbContext.Setup(d => d.CalendarSlots).Returns(slotMock.Object);

        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
        contentMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                ValueTask.FromResult((contents ?? []).FirstOrDefault(c => c.Id == (Guid)keys[0])));
        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);
    }

    // --- CreateSeriesAsync ---

    [Fact]
    public async Task CreateSeriesAsync_WithValidRRule_CreatesSeriesEntity()
    {
        SetupDbSets();

        var request = new ContentSeriesRequest(
            "Weekly LinkedIn Posts", null,
            "FREQ=WEEKLY;BYDAY=TU",
            [PlatformType.LinkedIn], ContentType.SocialPost,
            ["dotnet"], "America/New_York",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);

        var sut = CreateSut();
        var result = await sut.CreateSeriesAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSeriesAsync_WithInvalidRRule_ReturnsValidationFailure()
    {
        SetupDbSets();

        var request = new ContentSeriesRequest(
            "Bad Series", null,
            "NOT_A_VALID_RRULE",
            [PlatformType.TwitterX], ContentType.Thread,
            [], "UTC",
            DateTimeOffset.UtcNow, null);

        var sut = CreateSut();
        var result = await sut.CreateSeriesAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    // --- CreateManualSlotAsync ---

    [Fact]
    public async Task CreateManualSlotAsync_CreatesSlotWithNoSeriesReference()
    {
        SetupDbSets();

        var request = new CalendarSlotRequest(
            new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
            PlatformType.Instagram);

        var sut = CreateSut();
        var result = await sut.CreateManualSlotAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _dbContext.Verify(d => d.CalendarSlots.Add(
            It.Is<CalendarSlot>(s => s.ContentSeriesId == null && s.Status == CalendarSlotStatus.Open)),
            Times.Once);
    }

    // --- AssignContentAsync ---

    [Fact]
    public async Task AssignContentAsync_WithOpenSlot_FillsAndChangesStatus()
    {
        var slot = new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            Platform = PlatformType.TwitterX,
            Status = CalendarSlotStatus.Open,
        };
        var content = Content.Create(ContentType.Thread, "tweet thread", targetPlatforms: [PlatformType.TwitterX]);
        SetupDbSets(slots: [slot], contents: [content]);

        var sut = CreateSut();
        var result = await sut.AssignContentAsync(slot.Id, content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(content.Id, slot.ContentId);
        Assert.Equal(CalendarSlotStatus.Filled, slot.Status);
    }

    [Fact]
    public async Task AssignContentAsync_WithAlreadyFilledSlot_ReturnsConflict()
    {
        var slot = new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            Platform = PlatformType.TwitterX,
            Status = CalendarSlotStatus.Filled,
            ContentId = Guid.NewGuid(),
        };
        SetupDbSets(slots: [slot]);

        var sut = CreateSut();
        var result = await sut.AssignContentAsync(slot.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
    }

    [Fact]
    public async Task AssignContentAsync_SlotNotFound_ReturnsNotFound()
    {
        SetupDbSets();

        var sut = CreateSut();
        var result = await sut.AssignContentAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- GetSlotsAsync ---

    [Fact]
    public async Task GetSlotsAsync_IncludesManualSlotsWithNoSeriesReference()
    {
        var manualSlot = new CalendarSlot
        {
            ScheduledAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            Platform = PlatformType.Instagram,
            ContentSeriesId = null,
            Status = CalendarSlotStatus.Open,
        };
        SetupDbSets(slots: [manualSlot]);

        var sut = CreateSut();
        var result = await sut.GetSlotsAsync(
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 23, 59, 59, TimeSpan.Zero),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Null(result.Value![0].ContentSeriesId);
    }

    [Fact]
    public async Task GetSlotsAsync_MergesMaterializedSlotsWithGeneratedOccurrences()
    {
        var series = new ContentSeries
        {
            Name = "Weekly",
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=TU",
            TargetPlatforms = [PlatformType.LinkedIn],
            ContentType = ContentType.SocialPost,
            ThemeTags = [],
            TimeZoneId = "UTC",
            IsActive = true,
            StartsAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
        };

        var materializedSlot = new CalendarSlot
        {
            ScheduledAt = new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero), // Tuesday
            Platform = PlatformType.LinkedIn,
            ContentSeriesId = series.Id,
            ContentId = Guid.NewGuid(),
            Status = CalendarSlotStatus.Filled,
        };

        SetupDbSets(series: [series], slots: [materializedSlot]);

        var sut = CreateSut();
        var result = await sut.GetSlotsAsync(
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 7, 23, 59, 59, TimeSpan.Zero),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Should contain the materialized slot (Filled), not a duplicate generated one
        var filledSlots = result.Value!.Where(s => s.Status == CalendarSlotStatus.Filled).ToList();
        Assert.Single(filledSlots);
        Assert.Equal(materializedSlot.ContentId, filledSlots[0].ContentId);
    }

    // --- AutoFillSlotsAsync ---

    [Fact]
    public async Task AutoFillSlotsAsync_MatchesContentToSlotsByPlatform()
    {
        var twitterSlot = new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            Platform = PlatformType.TwitterX,
            Status = CalendarSlotStatus.Open,
        };

        var twitterContent = Content.Create(ContentType.Thread, "twitter thread", targetPlatforms: [PlatformType.TwitterX]);
        // Simulate Approved status via reflection or factory
        typeof(Content).GetProperty("Status")!.SetValue(twitterContent, ContentStatus.Approved);

        var linkedInContent = Content.Create(ContentType.SocialPost, "linkedin post", targetPlatforms: [PlatformType.LinkedIn]);
        typeof(Content).GetProperty("Status")!.SetValue(linkedInContent, ContentStatus.Approved);

        SetupDbSets(slots: [twitterSlot], contents: [twitterContent, linkedInContent]);

        var sut = CreateSut();
        var result = await sut.AutoFillSlotsAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(twitterContent.Id, twitterSlot.ContentId);
    }

    [Fact]
    public async Task AutoFillSlotsAsync_SkipsAlreadyFilledSlots()
    {
        var openSlot = new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            Platform = PlatformType.TwitterX,
            Status = CalendarSlotStatus.Open,
        };
        var filledSlot = new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(2),
            Platform = PlatformType.TwitterX,
            Status = CalendarSlotStatus.Filled,
            ContentId = Guid.NewGuid(),
        };

        var content = Content.Create(ContentType.Thread, "thread", targetPlatforms: [PlatformType.TwitterX]);
        typeof(Content).GetProperty("Status")!.SetValue(content, ContentStatus.Approved);

        SetupDbSets(slots: [openSlot, filledSlot], contents: [content]);

        var sut = CreateSut();
        var result = await sut.AutoFillSlotsAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Equal(1, result.Value);
    }

    [Fact]
    public async Task AutoFillSlotsAsync_ReturnsCountOfSlotsFilled()
    {
        var slots = Enumerable.Range(1, 3).Select(i => new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(i),
            Platform = PlatformType.LinkedIn,
            Status = CalendarSlotStatus.Open,
        }).ToArray();

        var contents = Enumerable.Range(1, 2).Select(i =>
        {
            var c = Content.Create(ContentType.SocialPost, $"post {i}", targetPlatforms: [PlatformType.LinkedIn]);
            typeof(Content).GetProperty("Status")!.SetValue(c, ContentStatus.Approved);
            return c;
        }).ToArray();

        SetupDbSets(slots: slots, contents: contents);

        var sut = CreateSut();
        var result = await sut.AutoFillSlotsAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Equal(2, result.Value);
    }

    [Fact]
    public async Task AutoFillSlotsAsync_ConsidersThemeTagAffinity()
    {
        var series = new ContentSeries
        {
            Name = "DotNet Series",
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO",
            TargetPlatforms = [PlatformType.LinkedIn],
            ContentType = ContentType.SocialPost,
            ThemeTags = ["dotnet", "csharp"],
            TimeZoneId = "UTC",
            IsActive = true,
            StartsAt = DateTimeOffset.UtcNow.AddMonths(-1),
        };

        var slot = new CalendarSlot
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            Platform = PlatformType.LinkedIn,
            ContentSeriesId = series.Id,
            Status = CalendarSlotStatus.Open,
        };

        var dotnetContent = Content.Create(ContentType.SocialPost, "dotnet post", targetPlatforms: [PlatformType.LinkedIn]);
        typeof(Content).GetProperty("Status")!.SetValue(dotnetContent, ContentStatus.Approved);
        dotnetContent.Metadata.Tags.Add("dotnet");

        var cookingContent = Content.Create(ContentType.SocialPost, "cooking post", targetPlatforms: [PlatformType.LinkedIn]);
        typeof(Content).GetProperty("Status")!.SetValue(cookingContent, ContentStatus.Approved);
        cookingContent.Metadata.Tags.Add("cooking");

        SetupDbSets(series: [series], slots: [slot], contents: [cookingContent, dotnetContent]);

        var sut = CreateSut();
        var result = await sut.AutoFillSlotsAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Equal(1, result.Value);
        Assert.Equal(dotnetContent.Id, slot.ContentId);
    }
}
