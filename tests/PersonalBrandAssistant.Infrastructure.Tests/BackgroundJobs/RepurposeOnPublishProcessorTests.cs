using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class RepurposeOnPublishProcessorTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly Mock<IRepurposingService> _repurposingService = new();
    private readonly Mock<ILogger<RepurposeOnPublishProcessor>> _logger = new();
    private readonly Mock<IApplicationDbContext> _dbContext = new();

    private readonly DateTimeOffset _now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);

    public RepurposeOnPublishProcessorTests()
    {
        _dateTimeProvider.Setup(d => d.UtcNow).Returns(_now);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IRepurposingService)))
            .Returns(_repurposingService.Object);
        // The processor resolves ApplicationDbContext, but we return our mock as the
        // IApplicationDbContext through the concrete type registration
        _serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext)))
            .Returns(_dbContext.Object);
    }

    private RepurposeOnPublishProcessor CreateSut() => new(
        _scopeFactory.Object,
        _dateTimeProvider.Object,
        _logger.Object);

    private void SetupDbSets(
        Content[]? contents = null,
        AutonomyConfiguration? autonomy = null)
    {
        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);

        var autonomyConfigs = autonomy is not null ? new[] { autonomy } : Array.Empty<AutonomyConfiguration>();
        var autonomyMock = autonomyConfigs.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.AutonomyConfigurations).Returns(autonomyMock.Object);

        var slotMock = Array.Empty<CalendarSlot>().AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.CalendarSlots).Returns(slotMock.Object);

        var seriesMock = Array.Empty<ContentSeries>().AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.ContentSeries).Returns(seriesMock.Object);
    }

    private static Content CreatePublishedContent(
        ContentType type = ContentType.BlogPost,
        PlatformType[]? platforms = null)
    {
        var content = Content.Create(type, "body", "Test", platforms ?? [PlatformType.TwitterX]);
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.TransitionTo(ContentStatus.Scheduled);
        content.TransitionTo(ContentStatus.Publishing);
        content.TransitionTo(ContentStatus.Published);
        // Must be within the 2-hour lookback window (now = 2026-03-16 12:00:00 UTC)
        content.PublishedAt = new DateTimeOffset(2026, 3, 16, 11, 0, 0, TimeSpan.Zero);
        return content;
    }

    [Fact]
    public async Task ProcessAsync_ContentPublished_TriggersRepurpose()
    {
        // Arrange
        var content = CreatePublishedContent();
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.Autonomous;

        SetupDbSets([content], autonomy);

        _repurposingService.Setup(r => r.RepurposeAsync(content.Id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _repurposingService.Verify(
            r => r.RepurposeAsync(content.Id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ManualAutonomy_SkipsRepurpose()
    {
        // Arrange
        var content = CreatePublishedContent();
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.Manual;

        SetupDbSets([content], autonomy);

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _repurposingService.Verify(
            r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SemiAutoAutonomy_OnlyPublishedContent()
    {
        // Arrange
        var content = CreatePublishedContent();
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.SemiAuto;

        SetupDbSets([content], autonomy);

        _repurposingService.Setup(r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([]));

        var sut = CreateSut();

        // Act
        await sut.ProcessAsync(CancellationToken.None);

        // Assert
        _repurposingService.Verify(
            r => r.RepurposeAsync(content.Id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_RepurposeFails_ContinuesProcessing()
    {
        // Arrange
        var content1 = CreatePublishedContent();
        var content2 = CreatePublishedContent();
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.Autonomous;

        SetupDbSets([content1, content2], autonomy);

        _repurposingService.SetupSequence(r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Boom"))
            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));

        var sut = CreateSut();

        // Act
        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));

        // Assert
        Assert.Null(ex);
        _repurposingService.Verify(
            r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEvent_IdempotentBehavior()
    {
        // Arrange
        var content = CreatePublishedContent();
        var autonomy = AutonomyConfiguration.CreateDefault();
        autonomy.GlobalLevel = AutonomyLevel.Autonomous;

        SetupDbSets([content], autonomy);

        _repurposingService.Setup(r => r.RepurposeAsync(It.IsAny<Guid>(), It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Conflict<IReadOnlyList<Guid>>("Already repurposed"));

        var sut = CreateSut();

        // Act
        var ex = await Record.ExceptionAsync(() => sut.ProcessAsync(CancellationToken.None));

        // Assert
        Assert.Null(ex);
    }
}
