using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Entities;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class RefreshIdeaSourcesHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CallsFeedReader_ForEachEnabledSourceWithFeedUrl()
    {
        using var db = CreateContext();
        var source1 = new IdeaSource { Name = "Source 1", Category = "Tech", IsEnabled = true, FeedUrl = "https://blog1.com/rss" };
        var source2 = new IdeaSource { Name = "Source 2", Category = "AI", IsEnabled = true, FeedUrl = "https://blog2.com/feed" };
        var disabled = new IdeaSource { Name = "Disabled", Category = "Off", IsEnabled = false, FeedUrl = "https://disabled.com/rss" };
        var noFeed = new IdeaSource { Name = "No Feed", Category = "Manual", IsEnabled = true, FeedUrl = "" };
        db.IdeaSources.AddRange(source1, source2, disabled, noFeed);
        await db.SaveChangesAsync();

        var mockReader = new Mock<IRssFeedReader>();
        mockReader.Setup(r => r.ReadFeedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>());

        var handler = new RefreshIdeaSources.Handler(
            db, mockReader.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        mockReader.Verify(r => r.ReadFeedAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_ReturnsCountOfNewIdeas()
    {
        using var db = CreateContext();
        var source = new IdeaSource { Name = "Source", Category = "Tech", IsEnabled = true, FeedUrl = "https://blog.com/rss" };
        db.IdeaSources.Add(source);
        await db.SaveChangesAsync();

        var items = new List<RssFeedItem>
        {
            new("Article 1", "Desc 1", "https://example.com/1", null, "Tech", DateTimeOffset.UtcNow),
            new("Article 2", "Desc 2", "https://example.com/2", null, "Tech", DateTimeOffset.UtcNow),
            new("Article 3", "Desc 3", "https://example.com/3", null, "Tech", DateTimeOffset.UtcNow),
        };

        var mockReader = new Mock<IRssFeedReader>();
        mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var handler = new RefreshIdeaSources.Handler(
            db, mockReader.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        var result = await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(3, await db.Ideas.CountAsync());
    }

    [Fact]
    public async Task Handle_SetsDetectedAt_FromFeedPublishDate()
    {
        // Regression: refresh used to omit DetectedAt, so items defaulted to
        // 0001-01-01 and sank to the bottom of the DetectedAt-sorted feed —
        // ingested but invisible. Must carry the feed's publish date like the poller.
        using var db = CreateContext();
        var source = new IdeaSource { Name = "Source", Category = "Tech", IsEnabled = true, FeedUrl = "https://blog.com/rss" };
        db.IdeaSources.Add(source);
        await db.SaveChangesAsync();

        var published = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);
        var mockReader = new Mock<IRssFeedReader>();
        mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>
            {
                new("Today's Story", "Desc", "https://blog.com/today", null, "Tech", published),
            });

        var handler = new RefreshIdeaSources.Handler(
            db, mockReader.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        var idea = await db.Ideas.SingleAsync();
        Assert.Equal(published, idea.DetectedAt);
    }

    [Fact]
    public async Task Handle_HandlesFeedErrors_GracefullyPerSource()
    {
        using var db = CreateContext();
        var failSource = new IdeaSource { Name = "Failing", Category = "Fail", IsEnabled = true, FeedUrl = "https://failing.com/rss" };
        var okSource = new IdeaSource { Name = "Working", Category = "OK", IsEnabled = true, FeedUrl = "https://working.com/rss" };
        db.IdeaSources.AddRange(failSource, okSource);
        await db.SaveChangesAsync();

        var mockReader = new Mock<IRssFeedReader>();
        mockReader.Setup(r => r.ReadFeedAsync(failSource.FeedUrl!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        mockReader.Setup(r => r.ReadFeedAsync(okSource.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>
            {
                new("Good Article", "Desc", "https://working.com/good", null, "OK", DateTimeOffset.UtcNow),
            });

        var handler = new RefreshIdeaSources.Handler(
            db, mockReader.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        var result = await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);

        var failedSource = await db.IdeaSources.FindAsync(failSource.Id);
        Assert.Equal(1, failedSource!.ConsecutiveFailures);
        Assert.Equal("Connection refused", failedSource.LastError);
    }
}
