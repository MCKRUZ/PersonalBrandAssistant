using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class CalendarSlotProcessorTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly Mock<IContentCalendarService> _calendarService = new();
    private readonly Mock<ILogger<CalendarSlotProcessor>> _logger = new();
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly ContentEngineOptions _options = new();

    private readonly DateTimeOffset _now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);

    public CalendarSlotProcessorTests()
    {
        _dateTimeProvider.Setup(d => d.UtcNow).Returns(_now);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IContentCalendarService)))
            .Returns(_calendarService.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext)))
            .Returns(_dbContext.Object);
    }

    private CalendarSlotProcessor CreateSut() => new(
        _scopeFactory.Object,
        _dateTimeProvider.Object,
        Options.Create(_options),
        _logger.Object);

    private void SetupDbSets(
        ContentSeries[]? series = null,
        CalendarSlot[]? slots = null,
        AutonomyConfiguration? autonomy = null)
    {
        var seriesMock = (series ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.ContentSeries).Returns(seriesMock.Object);

        var slotMock = (slots ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.CalendarSlots).Returns(slotMock.Object);

        var autonomyConfigs = autonomy is not null ? new[] { autonomy } : Array.Empty<AutonomyConfiguration>();
        var autonomyMock = autonomyConfigs.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.AutonomyConfigurations).Returns(autonomyMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_ActiveSeries_MaterializesUpcomingSlots()
    {
        // Arrange
        var series = new ContentSeries
        {
            Name = "Weekly Tech",
            RecurrenceRule = "FREQ=DAILY",
            TargetPlatforms = [PlatformType.TwitterX],
            ContentType = ContentType.BlogPost,
            TimeZoneId = "UTC",
            IsActive = true,
            StartsAt = _now.AddDays(-1),
        };
        typeof(ContentSeries).BaseType!.BaseType!.GetProperty("Id")!.SetValue(series, Guid.NewGuid());

        SetupDbSets(series: [series]);

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_ExistingSlots_NoDuplicates()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = new ContentSeries
        {
            Name = "Daily Post",
            RecurrenceRule = "FREQ=DAILY",
            TargetPlatforms = [PlatformType.TwitterX],
            ContentType = ContentType.BlogPost,
            TimeZoneId = "UTC",
            IsActive = true,
            StartsAt = _now.AddDays(-1),
        };
        typeof(ContentSeries).BaseType!.BaseType!.GetProperty("Id")!.SetValue(series, seriesId);

        // Pre-populate slots covering the full materialization window (0..7 days) so dedup kicks in
        var existingSlots = Enumerable.Range(0, 8)
            .Select(i => new CalendarSlot
            {
                ScheduledAt = _now.AddDays(i),
                Platform = PlatformType.TwitterX,
                ContentSeriesId = seriesId,
                Status = CalendarSlotStatus.Open,
            })
            .ToArray();

        SetupDbSets(series: [series], slots: existingSlots);

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert — no new slots should be added since all occurrences already have slots
        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AutonomousLevel_TriggersAutoFill()
    {
        // Arrange
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.Autonomous;

        SetupDbSets(series: [], autonomy: autonomy);

        _calendarService.Setup(c => c.AutoFillSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(3));

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _calendarService.Verify(
            c => c.AutoFillSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ManualLevel_NoAutoFill()
    {
        // Arrange
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.Manual;

        SetupDbSets(series: [], autonomy: autonomy);

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _calendarService.Verify(
            c => c.AutoFillSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
