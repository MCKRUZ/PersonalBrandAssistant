using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class SourcePollingServiceTests
{
    private static (SourcePollingService svc, ApplicationDbContext db) Build(
        Dictionary<IdeaSourceType, ISourceScraper> scrapers, RssPollingOptions? opts = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        var keyed = provider.As<IKeyedServiceProvider>();
        keyed.Setup(p => p.GetKeyedService(typeof(ISourceScraper), It.IsAny<object>()))
            .Returns((Type _, object key) => scrapers.TryGetValue((IdeaSourceType)key, out var s) ? s : null);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var monitor = new Mock<IOptionsMonitor<RssPollingOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(opts ?? new RssPollingOptions());

        return (new SourcePollingService(factory.Object, monitor.Object, NullLogger<SourcePollingService>.Instance), db);
    }

    private static ISourceScraper StubScraper(params ScrapedItem[] items)
    {
        var m = new Mock<ISourceScraper>();
        m.Setup(s => s.FetchAsync(It.IsAny<IdeaSource>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        return m.Object;
    }

    private static IdeaSource Src(IdeaSourceType type, string name = "s") =>
        new() { Name = name, Type = type, Category = "C", IsEnabled = true, FeedUrl = "https://x/f" };

    [Fact]
    public async Task PollAsync_DispatchesByType_AndCreatesIdeas()
    {
        var item = new ScrapedItem("HN Story", "desc", "https://ex/1", null, DateTimeOffset.UtcNow);
        var (svc, db) = Build(new() { [IdeaSourceType.HackerNews] = StubScraper(item) });
        db.IdeaSources.Add(Src(IdeaSourceType.HackerNews, "HN"));
        await db.SaveChangesAsync();

        await svc.PollAsync(CancellationToken.None);

        var idea = Assert.Single(db.Ideas);
        Assert.Equal("HN Story", idea.Title);
        Assert.Equal("HN", idea.SourceName);
        Assert.Equal("C", idea.Category);
    }

    [Fact]
    public async Task PollAsync_DedupsAcrossPolls()
    {
        var item = new ScrapedItem("Dup", "d", "https://ex/same", null, DateTimeOffset.UtcNow);
        var (svc, db) = Build(new() { [IdeaSourceType.HackerNews] = StubScraper(item) });
        db.IdeaSources.Add(Src(IdeaSourceType.HackerNews));
        await db.SaveChangesAsync();

        await svc.PollAsync(CancellationToken.None);
        await svc.PollAsync(CancellationToken.None);

        Assert.Single(db.Ideas);
    }

    [Fact]
    public async Task PollAsync_NoScraperForType_SkipsWithoutThrowing()
    {
        var (svc, db) = Build(new());
        db.IdeaSources.Add(Src(IdeaSourceType.GitHub));
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => svc.PollAsync(CancellationToken.None));
        Assert.Null(ex);
        Assert.Empty(db.Ideas);
    }

    [Fact]
    public async Task PollAsync_ScraperThrows_IncrementsFailureAndDisablesAtThreshold()
    {
        var throwing = new Mock<ISourceScraper>();
        throwing.Setup(s => s.FetchAsync(It.IsAny<IdeaSource>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var (svc, db) = Build(new() { [IdeaSourceType.HackerNews] = throwing.Object },
            new RssPollingOptions { MaxConsecutiveFailures = 1 });
        db.IdeaSources.Add(Src(IdeaSourceType.HackerNews));
        await db.SaveChangesAsync();

        await svc.PollAsync(CancellationToken.None);

        var src = Assert.Single(db.IdeaSources);
        Assert.False(src.IsEnabled);
        Assert.Equal(1, src.ConsecutiveFailures);
    }
}
