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

public class DigestServiceTests
{
    // Use mock scope so scope.Dispose() does not dispose the shared db instance.
    // Writer is resolved from the provider (not constructor) per Correction 1.
    // Mirrors IdeaScoringServiceTests.Build harness pattern.
    private static (DigestService svc, ApplicationDbContext db, Mock<IDigestWriter> writer, Mock<IDeliveryDispatcher> dispatcher) Build(DigestOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);
        var writer = new Mock<IDigestWriter>();
        var dispatcher = new Mock<IDeliveryDispatcher>();

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        serviceProvider.Setup(p => p.GetService(typeof(IDigestWriter))).Returns(writer.Object);
        serviceProvider.Setup(p => p.GetService(typeof(IDeliveryDispatcher))).Returns(dispatcher.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var svc = new DigestService(scopeFactory.Object, Options.Create(options), NullLogger<DigestService>.Instance);
        return (svc, db, writer, dispatcher);
    }

    private static Idea Scored(int score, Guid? dupOf = null) => new()
    {
        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Score = score,
        ScoredAt = DateTimeOffset.UtcNow, Summary = "summary", DuplicateOfId = dupOf
    };

    [Fact]
    public async Task GenerateDigestAsync_TopScoredPrimaries_CreatesDigestItemsAndFeedAlert()
    {
        var (svc, db, writer, _) = Build(new DigestOptions { TopN = 8, LookbackHours = 24 });
        var a = Scored(9); var b = Scored(7);
        db.Ideas.AddRange(a, b);
        await db.SaveChangesAsync();

        writer.Setup(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DigestCopy("Brief", "Intro", new List<DigestItemCopy>
            {
                new(0, "Why A"), new(1, "Why B")
            }));

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var digest = db.Digests.Include(d => d.Items).Single();
        Assert.Equal("Brief", digest.Title);
        Assert.Equal(2, digest.Items.Count);
        Assert.Equal(1, digest.Items.Single(i => i.IdeaId == a.Id).Rank); // highest score ranked 1
        Assert.Equal(2, digest.Items.Single(i => i.IdeaId == b.Id).Rank);
        Assert.Single(db.FeedItems.Where(f => f.Type == FeedItemType.SystemNotification));
    }

    [Fact]
    public async Task GenerateDigestAsync_AfterPersist_DispatchesDigestNotificationWithItems()
    {
        var (svc, db, writer, dispatcher) = Build(new DigestOptions { TopN = 8, LookbackHours = 24 });
        db.Ideas.AddRange(Scored(9), Scored(7));
        await db.SaveChangesAsync();
        writer.Setup(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DigestCopy("Brief", "Intro", new List<DigestItemCopy> { new(0, "Why A"), new(1, "Why B") }));

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(
            It.Is<DeliveryNotification>(n => n.Kind == DeliveryKind.Digest && n.Title == "Brief" && n.Items.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateDigestAsync_NoScoredIdeas_DoesNotDispatch()
    {
        var (svc, db, _, dispatcher) = Build(new DigestOptions());

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateDigestAsync_ExcludesDuplicates()
    {
        var (svc, db, writer, _) = Build(new DigestOptions { TopN = 8 });
        var primary = Scored(9);
        db.Ideas.Add(primary);
        await db.SaveChangesAsync();
        db.Ideas.Add(Scored(10, dupOf: primary.Id)); // duplicate, must be excluded despite higher score
        await db.SaveChangesAsync();

        DigestInput[]? captured = null;
        writer.Setup(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<DigestInput>, CancellationToken>((inp, _) => captured = inp.ToArray())
            .ReturnsAsync(new DigestCopy("t", "i", new List<DigestItemCopy> { new(0, "w") }));

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Single(captured!);
    }

    [Fact]
    public async Task GenerateDigestAsync_DigestForDateExists_SkipsAndDoesNotCallWriter()
    {
        var (svc, db, writer, _) = Build(new DigestOptions());
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        db.Digests.Add(new Digest { Date = today, Title = "x", Intro = "y", CreatedAt = DateTimeOffset.UtcNow });
        db.Ideas.Add(Scored(9));
        await db.SaveChangesAsync();

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        writer.Verify(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(db.Digests);
    }

    [Fact]
    public async Task GenerateDigestAsync_NoScoredIdeas_DoesNothing()
    {
        var (svc, db, writer, _) = Build(new DigestOptions());
        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(db.Digests);
        writer.Verify(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateDigestAsync_WriterReturnsFewerItems_MissingWhyItMattersIsEmpty()
    {
        var (svc, db, writer, _) = Build(new DigestOptions { TopN = 8, LookbackHours = 24 });
        var a = Scored(9); var b = Scored(7);
        db.Ideas.AddRange(a, b);
        await db.SaveChangesAsync();

        // Writer only returns copy for index 0 — index 1 is absent
        writer.Setup(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DigestCopy("Brief", "Intro", new List<DigestItemCopy>
            {
                new(0, "Why A")
            }));

        var exception = await Record.ExceptionAsync(() =>
            svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Null(exception);

        var digest = db.Digests.Include(d => d.Items).Single();
        Assert.Equal(2, digest.Items.Count);

        var rank2Item = digest.Items.Single(i => i.Rank == 2);
        Assert.Equal(string.Empty, rank2Item.WhyItMatters);
    }
}
