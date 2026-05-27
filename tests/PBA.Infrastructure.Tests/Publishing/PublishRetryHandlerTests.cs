using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class PublishRetryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IContentTransformer> _transformer = new();
    private readonly Mock<IBackgroundJobClient> _jobClient = new();
    private readonly Mock<IPlatformConnector> _connector = new();
    private readonly PublishRetryHandler _handler;

    public PublishRetryHandlerTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Medium, _connector.Object);
        var serviceProvider = services.BuildServiceProvider();

        _transformer.Setup(t => t.TransformAsync(It.IsAny<Content>(), It.IsAny<Platform>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("transformed-content");

        _handler = new PublishRetryHandler(
            _dbContext, serviceProvider, _transformer.Object, _jobClient.Object,
            NullLogger<PublishRetryHandler>.Instance);
    }

    private (Content Content, ContentPlatformPublish Record) SeedFailedRecord(
        int retryCount = 0, Platform platform = Platform.Medium)
    {
        var content = new Content
        {
            Title = "Test Post",
            Body = "Test body",
            PrimaryPlatform = Platform.Blog,
            Tags = ["AI"]
        };
        _dbContext.Contents.Add(content);

        var record = new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = platform,
            Status = PublishStatus.Failed,
            RetryCount = retryCount,
            ErrorMessage = "Previous failure"
        };
        _dbContext.ContentPlatformPublishes.Add(record);
        _dbContext.SaveChanges();

        return (content, record);
    }

    [Fact]
    public async Task RetryAsync_PublishedRecord_SkipsWithoutPublishing()
    {
        var content = new Content { Title = "Test", PrimaryPlatform = Platform.Blog };
        _dbContext.Contents.Add(content);
        var record = new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = Platform.Medium,
            Status = PublishStatus.Published,
            PublishedUrl = "https://example.com/post"
        };
        _dbContext.ContentPlatformPublishes.Add(record);
        await _dbContext.SaveChangesAsync();

        await _handler.RetryAsync(record.Id);

        _connector.Verify(
            c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RetryAsync_Success_UpdatesRecordToPublished()
    {
        var (_, record) = SeedFailedRecord();
        _connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(true, "https://medium.com/p/123", "post-123", null));

        await _handler.RetryAsync(record.Id);

        var updated = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.Equal(PublishStatus.Published, updated!.Status);
        Assert.Equal("https://medium.com/p/123", updated.PublishedUrl);
        Assert.Equal("post-123", updated.PlatformPostId);
        Assert.NotNull(updated.PublishedAt);
        Assert.Null(updated.NextRetryAt);
    }

    [Fact]
    public async Task RetryAsync_Failure_IncrementsRetryCount()
    {
        var (_, record) = SeedFailedRecord(retryCount: 0);
        _connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(false, null, null, "API error"));

        await _handler.RetryAsync(record.Id);

        var updated = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.Equal(1, updated!.RetryCount);
        Assert.Equal(PublishStatus.Failed, updated.Status);
        Assert.Equal("API error", updated.ErrorMessage);
    }

    [Fact]
    public async Task RetryAsync_UnderMaxRetries_SchedulesNextRetry()
    {
        var (_, record) = SeedFailedRecord(retryCount: 0);
        _connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(false, null, null, "API error"));

        await _handler.RetryAsync(record.Id);

        var updated = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.NotNull(updated!.NextRetryAt);

        _jobClient.Verify(c => c.Create(
            It.IsAny<Job>(),
            It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task RetryAsync_AtMaxRetries_MarksAsPermanentlyFailed()
    {
        var (_, record) = SeedFailedRecord(retryCount: 2);
        _connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(false, null, null, "Still failing"));

        await _handler.RetryAsync(record.Id);

        var updated = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.Equal(3, updated!.RetryCount);
        Assert.Equal(PublishStatus.Failed, updated.Status);
        Assert.Null(updated.NextRetryAt);

        _jobClient.Verify(c => c.Create(
            It.IsAny<Job>(),
            It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task RetryAsync_BackoffIncreases_30min_2hours()
    {
        var (content, record) = SeedFailedRecord(retryCount: 0);
        _connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(false, null, null, "API error"));

        await _handler.RetryAsync(record.Id);

        var afterFirst = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.Equal(1, afterFirst!.RetryCount);
        var firstRetryAt = afterFirst.NextRetryAt!.Value;
        var expectedFirst = TimeSpan.FromMinutes(30);
        var actualFirst = firstRetryAt - DateTimeOffset.UtcNow;
        Assert.InRange(actualFirst.TotalMinutes, expectedFirst.TotalMinutes - 1, expectedFirst.TotalMinutes + 1);

        await _handler.RetryAsync(record.Id);

        var afterSecond = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.Equal(2, afterSecond!.RetryCount);
        var secondRetryAt = afterSecond.NextRetryAt!.Value;
        var expectedSecond = TimeSpan.FromHours(2);
        var actualSecond = secondRetryAt - DateTimeOffset.UtcNow;
        Assert.InRange(actualSecond.TotalMinutes, expectedSecond.TotalMinutes - 1, expectedSecond.TotalMinutes + 1);
    }

    [Fact]
    public async Task RetryAsync_ConnectorThrows_IncrementsRetryAndStoresMessage()
    {
        var (_, record) = SeedFailedRecord(retryCount: 0);
        _connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await _handler.RetryAsync(record.Id);

        var updated = await _dbContext.ContentPlatformPublishes.FindAsync(record.Id);
        Assert.Equal(1, updated!.RetryCount);
        Assert.Equal("Connection refused", updated.ErrorMessage);
        Assert.NotNull(updated.NextRetryAt);
    }

    [Fact]
    public async Task RetryAsync_NonexistentRecord_ReturnsWithoutError()
    {
        await _handler.RetryAsync(Guid.NewGuid());

        _connector.Verify(
            c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public void Dispose() => _dbContext.Dispose();
}
