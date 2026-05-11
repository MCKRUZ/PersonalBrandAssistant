using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class ScheduledPublishReconcilerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IContentPublisher> _publisher = new();
    private readonly Mock<ILogger<ScheduledPublishReconciler>> _logger = new();

    public ScheduledPublishReconcilerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    private ScheduledPublishReconciler CreateReconciler()
    {
        var services = new ServiceCollection();
        services.AddScoped<IContentPublisher>(_ => _publisher.Object);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new ScheduledPublishReconciler(scopeFactory, _logger.Object);
    }

    private Content CreateContent(ContentStatus status, DateTimeOffset? scheduledAt) =>
        new()
        {
            Title = $"Post-{Guid.NewGuid():N}",
            Body = "Content body",
            Status = status,
            PrimaryPlatform = Platform.Blog,
            ScheduledAt = scheduledAt
        };

    [Fact]
    public async Task QueryOverdueContentAsync_FindsOverdueScheduledContent()
    {
        var c1 = CreateContent(ContentStatus.Scheduled, DateTimeOffset.UtcNow.AddHours(-2));
        var c2 = CreateContent(ContentStatus.Scheduled, DateTimeOffset.UtcNow.AddHours(-1));
        _dbContext.Contents.AddRange(c1, c2);
        await _dbContext.SaveChangesAsync();

        var overdueIds = await ScheduledPublishReconciler.QueryOverdueContentAsync(_dbContext);

        Assert.Equal(2, overdueIds.Count);
        Assert.Contains(c1.Id, overdueIds);
        Assert.Contains(c2.Id, overdueIds);
    }

    [Fact]
    public async Task ReconcileAsync_PublishesEachOverdueItem()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var overdueIds = new List<Guid> { id1, id2 };

        var reconciler = CreateReconciler();
        await reconciler.ReconcileAsync(overdueIds);

        _publisher.Verify(p => p.PublishAsync(id1), Times.Once);
        _publisher.Verify(p => p.PublishAsync(id2), Times.Once);
    }

    [Fact]
    public async Task QueryOverdueContentAsync_IgnoresAlreadyPublishedContent()
    {
        var content = CreateContent(ContentStatus.Published, DateTimeOffset.UtcNow.AddHours(-1));
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        var overdueIds = await ScheduledPublishReconciler.QueryOverdueContentAsync(_dbContext);

        Assert.Empty(overdueIds);
    }

    [Fact]
    public async Task QueryOverdueContentAsync_IgnoresNonScheduledContent()
    {
        var content = CreateContent(ContentStatus.Approved, DateTimeOffset.UtcNow.AddHours(-1));
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        var overdueIds = await ScheduledPublishReconciler.QueryOverdueContentAsync(_dbContext);

        Assert.Empty(overdueIds);
    }

    [Fact]
    public async Task QueryOverdueContentAsync_IgnoresFutureScheduledContent()
    {
        var content = CreateContent(ContentStatus.Scheduled, DateTimeOffset.UtcNow.AddHours(1));
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        var overdueIds = await ScheduledPublishReconciler.QueryOverdueContentAsync(_dbContext);

        Assert.Empty(overdueIds);
    }

    [Fact]
    public async Task ReconcileAsync_DoesNotPublish_WhenListEmpty()
    {
        var reconciler = CreateReconciler();
        await reconciler.ReconcileAsync(Array.Empty<Guid>());

        _publisher.Verify(p => p.PublishAsync(It.IsAny<Guid>()), Times.Never);
    }

    public void Dispose() => _dbContext.Dispose();
}
