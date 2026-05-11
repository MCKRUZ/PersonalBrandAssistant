using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class ContentPublisherTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IBlogConnector> _blogConnector = new();
    private readonly Mock<ILogger<ContentPublisher>> _logger = new();

    public ContentPublisherTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    private ContentPublisher CreatePublisher() =>
        new(_dbContext, _blogConnector.Object, _logger.Object);

    private Content CreateScheduledContent(Platform platform = Platform.Blog) =>
        new()
        {
            Title = "Test Post",
            Body = "Some content",
            Status = ContentStatus.Scheduled,
            PrimaryPlatform = platform,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

    [Fact]
    public async Task PublishAsync_PublishesContent_WhenStatusIsScheduled()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://matthewkruczek.ai/posts/test-post");

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
        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_InvokesBlogConnector_ForBlogPlatform()
    {
        var content = CreateScheduledContent(Platform.Blog);
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://matthewkruczek.ai/posts/test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_DoesNotInvokeBlogConnector_ForNonBlogPlatform()
    {
        var content = CreateScheduledContent(Platform.Twitter);
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_CreatesContentPlatformPublishRecord()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://matthewkruczek.ai/posts/test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
        Assert.NotNull(record);
        Assert.Equal(PublishStatus.Published, record.Status);
        Assert.Equal("https://matthewkruczek.ai/posts/test-post", record.PublishedUrl);
    }

    public void Dispose() => _dbContext.Dispose();
}
