using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaScoringServiceTests
{
    private static (IdeaScoringService svc, ApplicationDbContext db, Mock<IIdeaAnalyzer> analyzer)
        Build(IdeaScoringOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var analyzer = new Mock<IIdeaAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdeaAnalysis(7, "reason", "summary", "AI", new[] { "tag1" }));

        // Use mock scope so scope.Dispose() does not dispose the shared db instance.
        // Follows the same pattern as RssPollingServiceTests.
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var svc = new IdeaScoringService(scopeFactory.Object, analyzer.Object, Options.Create(options),
            NullLogger<IdeaScoringService>.Instance);
        return (svc, db, analyzer);
    }

    private static Idea NewIdea(DateTimeOffset detectedAt) => new()
    {
        Title = "T", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = detectedAt, ScoredAt = null
    };

    [Fact]
    public async Task ScoreBatchAsync_UnscoredIdea_PopulatesScoreSummaryTags()
    {
        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
        db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);

        var idea = db.Ideas.Single();
        Assert.Equal(7, idea.Score);
        Assert.Equal("summary", idea.Summary);
        Assert.Equal("AI", idea.Category);
        Assert.Equal(new[] { "tag1" }, idea.Tags);
        Assert.NotNull(idea.ScoredAt);
    }

    [Fact]
    public async Task ScoreBatchAsync_BackfillCutoffSet_SkipsOldIdeas()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
        var cutoff = DateTimeOffset.UtcNow;
        db.Ideas.Add(NewIdea(cutoff.AddDays(-5)));   // old, should be skipped
        db.Ideas.Add(NewIdea(cutoff.AddMinutes(5))); // new, should be scored
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: cutoff, CancellationToken.None);

        Assert.Equal(1, db.Ideas.Count(i => i.ScoredAt != null));
        analyzer.Verify(a => a.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScoreBatchAsync_RespectsBatchSize()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0 });
        for (var i = 0; i < 5; i++) db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);

        Assert.Equal(2, db.Ideas.Count(i => i.ScoredAt != null));
    }

    [Fact]
    public async Task ScoreBatchAsync_AnalyzerReturnsNull_LeavesIdeaUnscored()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
        analyzer.Setup(a => a.AnalyzeAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdeaAnalysis?)null);
        db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);

        Assert.Null(db.Ideas.Single().ScoredAt);
    }
}
