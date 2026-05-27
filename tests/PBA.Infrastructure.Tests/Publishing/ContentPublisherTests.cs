using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class ContentPublisherTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPlatformConnector> _blogConnector = new();
    private readonly Mock<IPlatformConnector> _mediumConnector = new();
    private readonly Mock<IPlatformConnector> _linkedInConnector = new();
    private readonly Mock<IPlatformConnector> _twitterConnector = new();
    private readonly Mock<IContentTransformer> _transformer = new();
    private readonly Mock<ILogger<ContentPublisher>> _logger = new();

    public ContentPublisherTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _transformer.Setup(t => t.TransformAsync(It.IsAny<Content>(), It.IsAny<Platform>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Content c, Platform _, CancellationToken _) => c.Body);
    }

    private ContentPublisher CreatePublisher()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Blog, _blogConnector.Object);
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Medium, _mediumConnector.Object);
        services.AddKeyedSingleton<IPlatformConnector>(Platform.LinkedIn, _linkedInConnector.Object);
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Twitter, _twitterConnector.Object);
        var sp = services.BuildServiceProvider();

        return new ContentPublisher(_dbContext, sp, _transformer.Object, _logger.Object);
    }

    private Content CreateScheduledContent(Platform platform = Platform.Blog) =>
        new()
        {
            Title = "Test Post",
            Body = "Some content",
            Status = ContentStatus.Scheduled,
            PrimaryPlatform = platform,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

    private void SetupConnectorSuccess(Mock<IPlatformConnector> connector, string url = "https://example.com/post", string postId = "post-1") =>
        connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(true, url, postId, null));

    private void SetupConnectorFailure(Mock<IPlatformConnector> connector, string error = "Publish failed") =>
        connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(false, null, null, error));

    // --- Migrated existing tests ---

    [Fact]
    public async Task PublishAsync_PublishesContent_WhenStatusIsScheduled()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
        Assert.NotNull(updated.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_SkipsPublishing_WhenStatusIsNoLongerScheduled()
    {
        var content = CreateScheduledContent();
        content.Status = ContentStatus.Approved;
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Approved, updated!.Status);
        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_InvokesPlatformConnector_ForBlogPlatform()
    {
        var content = CreateScheduledContent(Platform.Blog);
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_CreatesContentPlatformPublishRecord()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
        Assert.NotNull(record);
        Assert.Equal(PublishStatus.Published, record.Status);
        Assert.Equal("https://matthewkruczek.ai/posts/test-post", record.PublishedUrl);
    }

    // --- New tests ---

    [Fact]
    public async Task PublishAsync_ResolvesConnectorByPlatform_ViaKeyedDI()
    {
        var content = CreateScheduledContent(Platform.Medium);
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_mediumConnector, "https://medium.com/@matt/post", "medium-1");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_PrimaryFails_AbortsWithoutPublishingSecondaries()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorFailure(_blogConnector, "git push failed");

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        Assert.False(result.PrimarySuccess);
        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.NotEqual(ContentStatus.Published, updated!.Status);
    }

    [Fact]
    public async Task PublishAsync_PrimarySucceeds_FiresStateMachineTrigger()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
        Assert.NotNull(updated.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_SecondaryFails_CreatesFailedContentPlatformPublishRecord()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
        SetupConnectorFailure(_mediumConnector, "Medium API error");

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        Assert.True(result.PrimarySuccess);

        var records = await _dbContext.ContentPlatformPublishes
            .Where(p => p.ContentId == content.Id)
            .ToListAsync();

        var blogRecord = records.Single(r => r.Platform == Platform.Blog);
        Assert.Equal(PublishStatus.Published, blogRecord.Status);

        var mediumRecord = records.Single(r => r.Platform == Platform.Medium);
        Assert.Equal(PublishStatus.Failed, mediumRecord.Status);
        Assert.Equal("Medium API error", mediumRecord.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_SecondaryFails_CreatesRecordWithRetryCountZero()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);
        SetupConnectorFailure(_mediumConnector);

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        var mediumRecord = await _dbContext.ContentPlatformPublishes
            .SingleAsync(p => p.ContentId == content.Id && p.Platform == Platform.Medium);
        Assert.Equal(0, mediumRecord.RetryCount);
    }

    [Fact]
    public async Task PublishAsync_SkipsPlatformWithExistingPublishedRecord()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog];
        _dbContext.Contents.Add(content);
        _dbContext.ContentPlatformPublishes.Add(new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = Platform.Blog,
            Status = PublishStatus.Published,
            PublishedUrl = "https://matthewkruczek.ai/posts/existing",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await _dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var records = await _dbContext.ContentPlatformPublishes.Where(p => p.ContentId == content.Id).ToListAsync();
        Assert.Single(records);
    }

    [Fact]
    public async Task PublishAsync_NoTargetPlatforms_UsesContentTargetPlatforms()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.LinkedIn];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
        SetupConnectorSuccess(_linkedInConnector, "https://linkedin.com/post/1", "li-1");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _linkedInConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_NoContentTargetPlatforms_UsesPrimaryPlatformOnly()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _linkedInConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_GuidOverload_CallsFullMethodWithNullTargets()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
    }

    [Fact]
    public async Task PublishAsync_ParallelSecondaries_AllPublishIndependently()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium, Platform.LinkedIn, Platform.Twitter];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test", "blog-1");
        SetupConnectorFailure(_mediumConnector, "Medium error");
        SetupConnectorSuccess(_linkedInConnector, "https://linkedin.com/post/1", "li-1");
        SetupConnectorFailure(_twitterConnector, "Twitter error");

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);

        Assert.True(result.PrimarySuccess);
        Assert.Equal("https://matthewkruczek.ai/posts/test", result.PrimaryUrl);

        var records = await _dbContext.ContentPlatformPublishes
            .Where(p => p.ContentId == content.Id)
            .ToListAsync();
        Assert.Equal(4, records.Count);

        Assert.Equal(PublishStatus.Published, records.Single(r => r.Platform == Platform.Blog).Status);
        Assert.Equal(PublishStatus.Failed, records.Single(r => r.Platform == Platform.Medium).Status);
        Assert.Equal(PublishStatus.Published, records.Single(r => r.Platform == Platform.LinkedIn).Status);
        Assert.Equal(PublishStatus.Failed, records.Single(r => r.Platform == Platform.Twitter).Status);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
    }

    public void Dispose() => _dbContext.Dispose();
}
