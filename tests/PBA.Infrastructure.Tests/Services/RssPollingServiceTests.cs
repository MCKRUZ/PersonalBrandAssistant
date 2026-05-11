using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
    private readonly Mock<FreshRssClient> _mockClient;
    private readonly FreshRssOptions _options = new()
    {
        BaseUrl = "http://freshrss.local",
        Username = "admin",
        ApiPassword = "secret",
        BatchSize = 200,
        PollIntervalMinutes = 15,
        MaxConsecutiveFailures = 5,
    };

    public RssPollingServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);

        var httpClient = new HttpClient(new MockHttpMessageHandler());
        var optsMon = Mock.Of<IOptionsMonitor<FreshRssOptions>>(o => o.CurrentValue == _options);
        _mockClient = new Mock<FreshRssClient>(httpClient, optsMon, NullLogger<FreshRssClient>.Instance);
    }

    private RssPollingService CreateService()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(_dbContext);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var optsMon = Mock.Of<IOptionsMonitor<FreshRssOptions>>(o => o.CurrentValue == _options);

        return new RssPollingService(
            scopeFactory.Object,
            _mockClient.Object,
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
        _mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>
            {
                new("1", "Article 1", "Content", "https://testblog.com/article-1", "Test Blog", null, ["tech"], DateTimeOffset.UtcNow),
                new("2", "Article 2", "Content", "https://testblog.com/article-2", "Test Blog", null, [], DateTimeOffset.UtcNow),
            });
        _mockClient.Setup(c => c.MarkAsReadAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        var existingKey = RssPollingService.GenerateDeduplicationKey("https://testblog.com/existing", "Existing");
        _dbContext.Ideas.Add(new Idea
        {
            Title = "Existing",
            Url = "https://testblog.com/existing",
            DeduplicationKey = existingKey,
            SourceName = "Test Blog",
            IdeaSourceId = source.Id,
        });
        _dbContext.SaveChanges();

        _mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>
            {
                new("1", "Existing", "Content", "https://testblog.com/existing", "Test Blog", null, [], DateTimeOffset.UtcNow),
                new("2", "New Article", "Content", "https://testblog.com/new", "Test Blog", null, [], DateTimeOffset.UtcNow),
            });
        _mockClient.Setup(c => c.MarkAsReadAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var ideas = await _dbContext.Ideas.ToListAsync();
        Assert.Equal(2, ideas.Count);
    }

    [Fact]
    public async Task PollAsync_UpdatesSourceTimestampsOnSuccess()
    {
        var source = AddRssSource();
        _mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>());
        _mockClient.Setup(c => c.MarkAsReadAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        _mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>());
        _mockClient.Setup(c => c.MarkAsReadAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var updated = await _dbContext.IdeaSources.FindAsync(source.Id);
        Assert.Equal(0, updated!.ConsecutiveFailures);
        Assert.Null(updated.LastError);
    }

    [Fact]
    public async Task PollAsync_DisablesSourceAfterMaxFailures()
    {
        var source = AddRssSource(failures: 4);

        _mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>
            {
                new("1", "Bad", "Content", "https://other-domain.com/1", "Other", null, [], DateTimeOffset.UtcNow),
            });
        _mockClient.Setup(c => c.MarkAsReadAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        var updated = await _dbContext.IdeaSources.FindAsync(source.Id);
        Assert.Equal(0, updated!.ConsecutiveFailures);
    }

    [Fact]
    public async Task PollAsync_SkipsDisabledSources()
    {
        AddRssSource(enabled: false);
        _mockClient.Setup(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssEntry>());

        var service = CreateService();
        await service.PollAsync(CancellationToken.None);

        _mockClient.Verify(c => c.GetEntriesAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GenerateDeduplicationKey_NormalizesUrls()
    {
        var key1 = RssPollingService.GenerateDeduplicationKey("https://example.com/article?utm_source=twitter", "Title");
        var key2 = RssPollingService.GenerateDeduplicationKey("https://example.com/article", "Title");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GenerateDeduplicationKey_FallsBackToTitle()
    {
        var key1 = RssPollingService.GenerateDeduplicationKey(null, "Same Title");
        var key2 = RssPollingService.GenerateDeduplicationKey("", "Same Title");
        Assert.Equal(key1, key2);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
