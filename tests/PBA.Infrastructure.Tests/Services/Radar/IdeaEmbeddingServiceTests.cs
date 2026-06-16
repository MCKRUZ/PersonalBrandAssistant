using Microsoft.EntityFrameworkCore;
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

public class IdeaEmbeddingServiceTests
{
    private static (IdeaEmbeddingService svc, ApplicationDbContext db, Mock<ISidecarClient> sidecar)
        Build(EmbeddingOptions? options = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sidecar = new Mock<ISidecarClient>();
        var opts = options ?? new EmbeddingOptions { Model = "embed-model", Dimensions = 1536, BatchSize = 128 };
        var monitor = Mock.Of<IOptionsMonitor<EmbeddingOptions>>(m => m.CurrentValue == opts);
        var svc = new IdeaEmbeddingService(db, sidecar.Object, monitor,
            NullLogger<IdeaEmbeddingService>.Instance);
        return (svc, db, sidecar);
    }

    private static Idea NewIdea(string title = "Title", string? description = "Desc", float[]? embedding = null) => new()
    {
        Title = title, Description = description, SourceName = "S",
        DeduplicationKey = Guid.NewGuid().ToString(), Status = IdeaStatus.New,
        DetectedAt = DateTimeOffset.UtcNow, Embedding = embedding
    };

    // EmbedAsync stub: one vector per input, so result count always matches input count.
    private static void SetupEmbed(Mock<ISidecarClient> sidecar, float[] vector) =>
        sidecar.Setup(s => s.EmbedAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> inputs, string? _, CancellationToken _) =>
                inputs.Select(_ => vector).ToList());

    private static BrandRankingProfile ActiveProfile(params BrandPillar[] pillars) => new()
    {
        Version = 1, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
        Positioning = "P", AudiencePrimary = "A", Pillars = pillars.ToList()
    };

    [Fact]
    public async Task EmbedPendingAsync_NullEmbeddingIdea_EmbedsAndStampsEmbeddedAt()
    {
        var (svc, db, sidecar) = Build();
        SetupEmbed(sidecar, new float[] { 1, 0 });
        db.Ideas.Add(NewIdea(title: "Title", description: "Desc"));
        await db.SaveChangesAsync();

        await svc.EmbedPendingAsync(CancellationToken.None);

        var idea = db.Ideas.Single();
        Assert.Equal(new float[] { 1, 0 }, idea.Embedding);
        Assert.NotNull(idea.EmbeddedAt);
        sidecar.Verify(s => s.EmbedAsync(
            It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "Title Desc"),
            "embed-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmbedPendingAsync_AlreadyEmbeddedIdea_IsNotReEmbedded()
    {
        var (svc, db, sidecar) = Build();
        db.Ideas.Add(NewIdea(embedding: new float[] { 0.5f, 0.5f }));
        await db.SaveChangesAsync();

        await svc.EmbedPendingAsync(CancellationToken.None);

        sidecar.Verify(s => s.EmbedAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmbedPendingAsync_ActiveProfilePillarsWithNullVector_AreEmbedded()
    {
        var (svc, db, sidecar) = Build();
        SetupEmbed(sidecar, new float[] { 1, 0 });
        db.BrandRankingProfiles.Add(ActiveProfile(
            new BrandPillar { Name = "Pillar", Description = "desc", Weight = 1, Order = 0 }));
        await db.SaveChangesAsync();

        await svc.EmbedPendingAsync(CancellationToken.None);

        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
        Assert.Equal(new float[] { 1, 0 }, pillar.DescriptionEmbedding);
    }

    [Fact]
    public async Task EmbedPendingAsync_EmbedBatchThrows_LeavesIdeaNullForRetry()
    {
        var (svc, db, sidecar) = Build();
        sidecar.Setup(s => s.EmbedAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("429"));
        db.Ideas.Add(NewIdea());
        await db.SaveChangesAsync();

        await svc.EmbedPendingAsync(CancellationToken.None); // must not throw

        Assert.Null(db.Ideas.Single().Embedding);
    }

    [Fact]
    public async Task EmbedPendingAsync_ZeroVector_IsRejectedAndLeavesIdeaNull()
    {
        var (svc, db, sidecar) = Build();
        SetupEmbed(sidecar, new float[] { 0, 0 });
        db.Ideas.Add(NewIdea());
        await db.SaveChangesAsync();

        await svc.EmbedPendingAsync(CancellationToken.None);

        Assert.Null(db.Ideas.Single().Embedding);
    }

    [Fact]
    public async Task EmbedPendingAsync_NaNVector_IsRejectedAndLeavesIdeaNull()
    {
        var (svc, db, sidecar) = Build();
        SetupEmbed(sidecar, new[] { float.NaN, 1f });
        db.Ideas.Add(NewIdea());
        await db.SaveChangesAsync();

        await svc.EmbedPendingAsync(CancellationToken.None);

        Assert.Null(db.Ideas.Single().Embedding);
    }

    [Fact]
    public void ComputeEmbeddingBrandFit_WeightsCosinePerPillar()
    {
        var (svc, _, _) = Build();
        var pillars = new List<BrandPillar>
        {
            new() { Name = "A", Description = "a", Weight = 0.5, DescriptionEmbedding = new float[] { 1, 0 } },
            new() { Name = "B", Description = "b", Weight = 0.3, DescriptionEmbedding = new float[] { 0, 1 } },
        };

        // 0.5 × cos([1,0],[1,0]) + 0.3 × cos([1,0],[0,1]) = 0.5×1 + 0.3×0 = 0.5
        var fit = svc.ComputeEmbeddingBrandFit(new float[] { 1, 0 }, pillars);

        Assert.Equal(0.5, fit, 6);
    }

    [Fact]
    public void ComputeEmbeddingBrandFit_NullPillarEmbedding_IsSkipped()
    {
        var (svc, _, _) = Build();
        var pillars = new List<BrandPillar>
        {
            new() { Name = "A", Description = "a", Weight = 0.5, DescriptionEmbedding = new float[] { 1, 0 } },
            new() { Name = "B", Description = "b", Weight = 0.4, DescriptionEmbedding = null },
        };

        var fit = svc.ComputeEmbeddingBrandFit(new float[] { 1, 0 }, pillars);

        Assert.Equal(0.5, fit, 6); // the null-embedding pillar contributes nothing, no NaN
    }
}
