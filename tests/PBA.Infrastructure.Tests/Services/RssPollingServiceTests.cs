using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class RssPollingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IRssFeedReader> _mockReader;
    private readonly RssPollingOptions _options = new()
    {
        PollIntervalMinutes = 15,
        MaxConsecutiveFailures = 5,
    };

    public RssPollingServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);
        _mockReader = new Mock<IRssFeedReader>();
    }

    private RssPollingService CreateService()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(_dbContext);
        serviceProvider.Setup(p => p.GetService(typeof(IRssFeedReader))).Returns(_mockReader.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var optsMon = Mock.Of<IOptionsMonitor<RssPollingOptions>>(o => o.CurrentValue == _options);

        return new RssPollingService(
            scopeFactory.Object,
            optsMon,
            NullLogger<RssPollingService>.Instance);
    }

    private IdeaSource AddRssSource(string name = "Test Blog", string feedUrl = "https://testblog.com/rss",
        int failures = 0, bool enabled = true)
    {
        var source = new IdeaSource
        {
            Name = name,
            Type = IdeaSourceType.RSS,
            FeedUrl = feedUrl,
            Category = "Tech",
            PollIntervalMinutes = 30,
            IsEnabled = enabled,
            ConsecutiveFailures = failures,
        };
        _dbContext.IdeaSources.Add(source);
        _dbContext.SaveChanges();
        return source;
    }

    [Fact]
    public async Task PollAsync_CreatesIdeasForNewEntries()
    {
        var source = AddRssSource();
        _mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>
            {
                new("Article 1", "Content", "https://testblog.com/article-1", null, "tech", DateTimeOffset.UtcNow),
                new("Article 2", "Content", "https://testblog.com/article-2", null, null, DateTimeOffset.UtcNow),
            });

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var ideas = await _dbContext.Ideas.ToListAsync();
        Assert.Equal(2, ideas.Count);
        Assert.All(ideas, i => Assert.Equal(IdeaStatus.New, i.Status));
    }

    [Fact]
    public async Task PollAsync_SkipsDuplicateEntries()
    {
        var source = AddRssSource();
        var existingKey = DeduplicationKeyGenerator.Generate("https://testblog.com/existing", "Existing");
        _dbContext.Ideas.Add(new Idea
        {
            Title = "Existing",
            Url = "https://testblog.com/existing",
            DeduplicationKey = existingKey,
            SourceName = "Test Blog",
            IdeaSourceId = source.Id,
        });
        _dbContext.SaveChanges();

        _mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>
            {
                new("Existing", "Content", "https://testblog.com/existing", null, null, DateTimeOffset.UtcNow),
                new("New Article", "Content", "https://testblog.com/new", null, null, DateTimeOffset.UtcNow),
            });

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var ideas = await _dbContext.Ideas.ToListAsync();
        Assert.Equal(2, ideas.Count);
    }

    [Fact]
    public async Task PollAsync_UpdatesSourceTimestampsOnSuccess()
    {
        var source = AddRssSource();
        _mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>());

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var updated = await _dbContext.IdeaSources.FindAsync(source.Id);
        Assert.NotNull(updated!.LastPolledAt);
        Assert.NotNull(updated.LastSuccessAt);
    }

    [Fact]
    public async Task PollAsync_ResetsConsecutiveFailuresOnSuccess()
    {
        var source = AddRssSource(failures: 3);
        _mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>());

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var updated = await _dbContext.IdeaSources.FindAsync(source.Id);
        Assert.Equal(0, updated!.ConsecutiveFailures);
        Assert.Null(updated.LastError);
    }

    [Fact]
    public async Task PollAsync_IncrementsFailuresOnError()
    {
        var source = AddRssSource();
        _mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var updated = await _dbContext.IdeaSources.FindAsync(source.Id);
        Assert.Equal(1, updated!.ConsecutiveFailures);
        Assert.Equal("Connection refused", updated.LastError);
    }

    [Fact]
    public async Task PollAsync_DisablesSourceAfterMaxFailures()
    {
        var source = AddRssSource(failures: 4);
        _mockReader.Setup(r => r.ReadFeedAsync(source.FeedUrl!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Timeout"));

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var updated = await _dbContext.IdeaSources.FindAsync(source.Id);
        Assert.False(updated!.IsEnabled);
        Assert.Equal(5, updated.ConsecutiveFailures);
    }

    [Fact]
    public async Task PollAsync_SkipsDisabledSources()
    {
        AddRssSource(enabled: false);

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        _mockReader.Verify(r => r.ReadFeedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GenerateDeduplicationKey_NormalizesUrls()
    {
        var key1 = DeduplicationKeyGenerator.Generate("https://example.com/article?utm_source=twitter", "Title");
        var key2 = DeduplicationKeyGenerator.Generate("https://example.com/article", "Title");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GenerateDeduplicationKey_FallsBackToTitle()
    {
        var key1 = DeduplicationKeyGenerator.Generate(null, "Same Title");
        var key2 = DeduplicationKeyGenerator.Generate("", "Same Title");
        Assert.Equal(key1, key2);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
