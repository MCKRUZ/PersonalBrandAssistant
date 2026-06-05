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

public class IdeaClusteringServiceTests
{
    private static (IdeaClusteringService svc, ApplicationDbContext db, Mock<IIdeaClusterer> clusterer)
        Build(ClusteringOptions options)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var clusterer = new Mock<IIdeaClusterer>();

        // Use mock scope so scope.Dispose() does not dispose the shared db instance.
        // Follows the same pattern as IdeaScoringServiceTests.
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        serviceProvider.Setup(p => p.GetService(typeof(IIdeaClusterer))).Returns(clusterer.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var svc = new IdeaClusteringService(scopeFactory.Object, Options.Create(options),
            NullLogger<IdeaClusteringService>.Instance);
        return (svc, db, clusterer);
    }

    private static Idea Scored(int score) => new()
    {
        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Score = score,
        ScoredAt = DateTimeOffset.UtcNow, ClusteredAt = null, DuplicateOfId = null
    };

    [Fact]
    public async Task ClusterBatchAsync_GroupsReturned_SetsDuplicateOfIdOnSecondary()
    {
        var (svc, db, clusterer) = Build(new ClusteringOptions { MinScore = 6, LookbackHours = 48 });
        var a = Scored(8); var b = Scored(7);
        db.Ideas.AddRange(a, b);
        await db.SaveChangesAsync();

        clusterer.Setup(c => c.ClusterAsync(It.IsAny<IReadOnlyList<ClusterInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IReadOnlyList<int>> { new[] { 0, 1 } });

        await svc.ClusterBatchAsync(CancellationToken.None);

        var primary = db.Ideas.Single(i => i.DuplicateOfId == null);
        var dup = db.Ideas.Single(i => i.DuplicateOfId != null);
        Assert.Equal(primary.Id, dup.DuplicateOfId);
        Assert.All(db.Ideas, i => Assert.NotNull(i.ClusteredAt));
    }

    [Fact]
    public async Task ClusterBatchAsync_LowScoreIdeas_AreExcluded()
    {
        var (svc, db, clusterer) = Build(new ClusteringOptions { MinScore = 6 });
        db.Ideas.AddRange(Scored(3), Scored(2));
        await db.SaveChangesAsync();

        await svc.ClusterBatchAsync(CancellationToken.None);

        clusterer.Verify(c => c.ClusterAsync(It.IsAny<IReadOnlyList<ClusterInput>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
