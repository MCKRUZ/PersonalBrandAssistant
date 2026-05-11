using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
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
    public async Task Handle_CallsFreshRssClient_ForAllEnabledSources()
    {
        using var db = CreateContext();
        var enabled1 = new IdeaSource { Name = "Source 1", Category = "Tech", IsEnabled = true };
        var enabled2 = new IdeaSource { Name = "Source 2", Category = "AI", IsEnabled = true };
        var disabled = new IdeaSource { Name = "Disabled", Category = "Off", IsEnabled = false };
        db.IdeaSources.AddRange(enabled1, enabled2, disabled);
        await db.SaveChangesAsync();

        var mockClient = new Mock<IFreshRssClient>();
        mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>());

        var handler = new RefreshIdeaSources.Handler(
            db, mockClient.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        mockClient.Verify(c => c.GetEntriesAsync(
            It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_ReturnsCountOfNewIdeas()
    {
        using var db = CreateContext();
        var source = new IdeaSource { Name = "Source", Category = "Tech", IsEnabled = true };
        db.IdeaSources.Add(source);
        await db.SaveChangesAsync();

        var entries = new List<RssEntry>
        {
            new("Article 1", "Desc 1", "https://example.com/1", "Feed", null, "Tech", DateTimeOffset.UtcNow, "e1"),
            new("Article 2", "Desc 2", "https://example.com/2", "Feed", null, "Tech", DateTimeOffset.UtcNow, "e2"),
            new("Article 3", "Desc 3", "https://example.com/3", "Feed", null, "Tech", DateTimeOffset.UtcNow, "e3")
        };

        var mockClient = new Mock<IFreshRssClient>();
        mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        var handler = new RefreshIdeaSources.Handler(
            db, mockClient.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        var result = await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(3, await db.Ideas.CountAsync());
    }

    [Fact]
    public async Task Handle_HandlesClientErrors_GracefullyPerSource()
    {
        using var db = CreateContext();
        var failSource = new IdeaSource { Name = "Failing", Category = "Fail", IsEnabled = true };
        var okSource = new IdeaSource { Name = "Working", Category = "OK", IsEnabled = true };
        db.IdeaSources.AddRange(failSource, okSource);
        await db.SaveChangesAsync();

        var entries = new List<RssEntry>
        {
            new("Good Article", "Desc", "https://example.com/good", "Feed", null, "OK", DateTimeOffset.UtcNow, "e1")
        };

        var callCount = 0;
        var mockClient = new Mock<IFreshRssClient>();
        mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) throw new HttpRequestException("Connection refused");
                return entries;
            });

        var handler = new RefreshIdeaSources.Handler(
            db, mockClient.Object, NullLogger<RefreshIdeaSources.Handler>.Instance);

        var result = await handler.Handle(new RefreshIdeaSources.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);

        var failedSource = await db.IdeaSources.FindAsync(failSource.Id);
        Assert.Equal(1, failedSource!.ConsecutiveFailures);
        Assert.Equal("Connection refused", failedSource.LastError);
    }
}
