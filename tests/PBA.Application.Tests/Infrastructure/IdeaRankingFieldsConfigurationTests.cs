using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.FeedRanking;

// InMemory checks for the new ranking fields on Idea (round-trip + ScoreAttempts default). The
// vector(1536) Embedding column and the literal jsonb column type are Postgres-only and covered by
// section-12 Testcontainers — InMemory stores PillarSubScores/float[] natively.
public class IdeaRankingFieldsConfigurationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Idea NewIdea() => new()
    {
        Title = "t",
        SourceName = "s",
        DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New,
        DetectedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task NewIdea_ScoreAttempts_DefaultsToZero_AndRankingFieldsNullByDefault()
    {
        var id = Guid.NewGuid();
        await using var db = NewContext();
        var idea = NewIdea();
        idea.GetType().GetProperty("Id")!.SetValue(idea, id);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.Ideas.SingleAsync(i => i.Id == id);
        Assert.Equal(0, loaded.ScoreAttempts);
        Assert.Null(loaded.Embedding);
        Assert.Null(loaded.EmbeddedAt);
        Assert.Null(loaded.IsAntiTopic);
        Assert.Null(loaded.IsAuthorityTopic);
        Assert.Null(loaded.ScoredProfileVersion);
        Assert.Empty(loaded.PillarSubScores);
    }

    [Fact]
    public async Task RankingFields_RoundTrip_IncludingPillarSubScoresByPillarId()
    {
        var id = Guid.NewGuid();
        var pillarId = Guid.NewGuid();
        await using var db = NewContext();
        var idea = NewIdea();
        idea.GetType().GetProperty("Id")!.SetValue(idea, id);
        idea.EmbeddedAt = DateTimeOffset.UtcNow;
        idea.IsAntiTopic = false;
        idea.IsAuthorityTopic = true;
        idea.ScoredProfileVersion = 1;
        idea.ScoreAttempts = 2;
        idea.PillarSubScores =
        [
            new PillarSubScore { PillarId = pillarId, PillarName = "P1", Score = 0.75, Reason = "fit" },
            new PillarSubScore { PillarId = Guid.NewGuid(), PillarName = "P2", Score = 0.25 },
        ];
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.Ideas.SingleAsync(i => i.Id == id);
        Assert.True(loaded.IsAuthorityTopic);
        Assert.False(loaded.IsAntiTopic);
        Assert.Equal(1, loaded.ScoredProfileVersion);
        Assert.Equal(2, loaded.ScoreAttempts);
        Assert.Equal(2, loaded.PillarSubScores.Count);
        var p1 = loaded.PillarSubScores.Single(s => s.PillarId == pillarId);
        Assert.Equal(0.75, p1.Score);
        Assert.Equal("fit", p1.Reason);
        Assert.Equal("P1", p1.PillarName);
    }
}
