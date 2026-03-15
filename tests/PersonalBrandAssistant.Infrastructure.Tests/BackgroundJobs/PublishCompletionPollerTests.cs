using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class PublishCompletionPollerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<ILogger<PublishCompletionPoller>> _logger = new();
    private readonly Mock<ISocialPlatform> _instagramAdapter = new();
    private readonly Mock<ISocialPlatform> _youtubeAdapter = new();
    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public PublishCompletionPollerTests()
    {
        _dateTime.Setup(d => d.UtcNow).Returns(_now);
        _instagramAdapter.Setup(a => a.Type).Returns(PlatformType.Instagram);
        _youtubeAdapter.Setup(a => a.Type).Returns(PlatformType.YouTube);
    }

    [Fact]
    public async Task LeavesProcessing_WhenStillInProgress()
    {
        var entry = CreateProcessingEntry(PlatformType.Instagram, _now.AddMinutes(-5));
        SetupEntries([entry]);
        _instagramAdapter.Setup(a => a.CheckPublishStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlatformPublishStatusCheck(PlatformPublishStatus.Processing, null, null)));

        var poller = CreatePoller();
        await poller.PollProcessingEntriesAsync(CancellationToken.None);

        Assert.Equal(PlatformPublishStatus.Processing, entry.Status);
    }

    [Fact]
    public async Task UpdatesToPublished_WhenFinished()
    {
        var entry = CreateProcessingEntry(PlatformType.Instagram, _now.AddMinutes(-5));
        SetupEntries([entry]);
        _instagramAdapter.Setup(a => a.CheckPublishStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlatformPublishStatusCheck(
                PlatformPublishStatus.Published, "https://instagram.com/p/abc123", null)));

        var poller = CreatePoller();
        await poller.PollProcessingEntriesAsync(CancellationToken.None);

        Assert.Equal(PlatformPublishStatus.Published, entry.Status);
        Assert.Equal("https://instagram.com/p/abc123", entry.PostUrl);
    }

    [Fact]
    public async Task UpdatesYouTubeToPublished_WhenComplete()
    {
        var entry = CreateProcessingEntry(PlatformType.YouTube, _now.AddMinutes(-10));
        SetupEntries([entry]);
        _youtubeAdapter.Setup(a => a.CheckPublishStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlatformPublishStatusCheck(
                PlatformPublishStatus.Published, "https://youtube.com/watch?v=abc", null)));

        var poller = CreatePoller();
        await poller.PollProcessingEntriesAsync(CancellationToken.None);

        Assert.Equal(PlatformPublishStatus.Published, entry.Status);
    }

    [Fact]
    public async Task MarksFailed_After30MinuteTimeout()
    {
        var entry = CreateProcessingEntry(PlatformType.Instagram, _now.AddMinutes(-31));
        SetupEntries([entry]);

        var poller = CreatePoller();
        await poller.PollProcessingEntriesAsync(CancellationToken.None);

        Assert.Equal(PlatformPublishStatus.Failed, entry.Status);
        Assert.Contains("timed out", entry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private PublishCompletionPoller CreatePoller()
    {
        var scopeFactory = CreateScopeFactory();
        return new PublishCompletionPoller(scopeFactory, _dateTime.Object, _logger.Object);
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var adapters = new[] { _instagramAdapter.Object, _youtubeAdapter.Object };
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext))).Returns(_db.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(IEnumerable<ISocialPlatform>))).Returns(adapters);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private ContentPlatformStatus CreateProcessingEntry(PlatformType platform, DateTimeOffset createdAt)
    {
        return new ContentPlatformStatus
        {
            ContentId = Guid.NewGuid(),
            Platform = platform,
            Status = PlatformPublishStatus.Processing,
            PlatformPostId = "container-123",
            CreatedAt = createdAt,
        };
    }

    private void SetupEntries(List<ContentPlatformStatus> entries)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(entries);
        _db.Setup(d => d.ContentPlatformStatuses).Returns(mockSet.Object);
    }
}
