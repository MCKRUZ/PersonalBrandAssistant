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
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class HighScoreAlertServiceTests
{
    private static (HighScoreAlertService svc, ApplicationDbContext db, Mock<IDeliveryDispatcher> dispatcher) Build(
        AlertDeliveryOptions alerts)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);
        var dispatcher = new Mock<IDeliveryDispatcher>();

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        provider.Setup(p => p.GetService(typeof(IDeliveryDispatcher))).Returns(dispatcher.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var options = Options.Create(new DigestDeliveryOptions { Alerts = alerts });
        var svc = new HighScoreAlertService(scopeFactory.Object, options, NullLogger<HighScoreAlertService>.Instance);
        return (svc, db, dispatcher);
    }

    private static Idea Idea(int score, DateTimeOffset? alertedAt = null, Guid? dupOf = null) => new()
    {
        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Score = score,
        ScoredAt = DateTimeOffset.UtcNow, Summary = "summary", ScoreReason = "reason",
        AlertedAt = alertedAt, DuplicateOfId = dupOf
    };

    private static readonly AlertDeliveryOptions Default = new() { Enabled = true, ScoreThreshold = 9, MaxPerDay = 5 };

    [Fact]
    public async Task SweepAsync_IdeaAtThreshold_DispatchesAndStampsAlertedAt()
    {
        var (svc, db, dispatcher) = Build(Default);
        var idea = Idea(9);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await svc.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(
            It.Is<DeliveryNotification>(n => n.Kind == DeliveryKind.Alert), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(db.Ideas.Single().AlertedAt);
    }

    [Fact]
    public async Task SweepAsync_Alert_WritesHighPriorityTrendAlertFeedItem()
    {
        var (svc, db, _) = Build(Default);
        var idea = Idea(9);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await svc.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var feedItem = Assert.Single(db.FeedItems);
        Assert.Equal(FeedItemType.TrendAlert, feedItem.Type);
        Assert.Equal(FeedItemPriority.High, feedItem.Priority);
        Assert.Equal(idea.Id, feedItem.ActionTargetId);
        Assert.Contains(idea.Title, feedItem.Title);
    }

    [Fact]
    public async Task SweepAsync_BelowThreshold_DoesNotDispatch()
    {
        var (svc, db, dispatcher) = Build(Default);
        db.Ideas.Add(Idea(8));
        await db.SaveChangesAsync();

        await svc.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(db.Ideas.Single().AlertedAt);
    }

    [Fact]
    public async Task SweepAsync_AlreadyAlerted_NotReAlerted()
    {
        var (svc, db, dispatcher) = Build(Default);
        db.Ideas.Add(Idea(10, alertedAt: DateTimeOffset.UtcNow.AddHours(-1)));
        await db.SaveChangesAsync();

        await svc.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepAsync_DuplicateIdea_Excluded()
    {
        var (svc, db, dispatcher) = Build(Default);
        var primary = Idea(9);
        db.Ideas.Add(primary);
        await db.SaveChangesAsync();
        db.Ideas.Add(Idea(10, dupOf: primary.Id));
        await db.SaveChangesAsync();

        await svc.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // only the primary alerts; the higher-scored duplicate is excluded
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SweepAsync_DailyCapReached_DoesNotDispatch()
    {
        var (svc, db, dispatcher) = Build(new AlertDeliveryOptions { Enabled = true, ScoreThreshold = 9, MaxPerDay = 5 });
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++) db.Ideas.Add(Idea(9, alertedAt: now)); // 5 already alerted today
        db.Ideas.Add(Idea(9)); // one fresh candidate
        await db.SaveChangesAsync();

        await svc.SweepAsync(now, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepAsync_PartialBudget_SendsOnlyUpToRemaining()
    {
        var (svc, db, dispatcher) = Build(new AlertDeliveryOptions { Enabled = true, ScoreThreshold = 9, MaxPerDay = 5 });
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++) db.Ideas.Add(Idea(9, alertedAt: now)); // 4 used today
        for (var i = 0; i < 3; i++) db.Ideas.Add(Idea(9)); // 3 fresh candidates, only 1 budget left
        await db.SaveChangesAsync();

        await svc.SweepAsync(now, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(5, db.Ideas.Count(i => i.AlertedAt != null));
    }

    [Fact]
    public async Task SweepAsync_AlertsDisabled_DoesNothing()
    {
        var (svc, db, dispatcher) = Build(new AlertDeliveryOptions { Enabled = false, ScoreThreshold = 9, MaxPerDay = 5 });
        db.Ideas.Add(Idea(10));
        await db.SaveChangesAsync();

        await svc.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(db.Ideas.Single().AlertedAt);
    }
}
