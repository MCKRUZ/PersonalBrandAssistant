using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Infrastructure.Tests.Data;

// Provider-agnostic model + config tests (run on the InMemory provider; the jsonb converter makes the
// metric-bag round-trip work there). Index-enforcement lives in ChannelAnalyticsPersistenceTests (real DB).
public class ChannelAnalyticsModelTests
{
    [Fact]
    public void Platform_Enum_YouTubeInstagramTikTok_HaveStableNumericValues()
    {
        // Persisted PlatformCredential.Platform values depend on these numbers — append only, never renumber.
        Assert.Equal(0, (int)Platform.Blog);
        Assert.Equal(1, (int)Platform.Substack);
        Assert.Equal(2, (int)Platform.LinkedIn);
        Assert.Equal(3, (int)Platform.Twitter);
        Assert.Equal(4, (int)Platform.Reddit);
        Assert.Equal(5, (int)Platform.YouTube);
        Assert.Equal(6, (int)Platform.Medium);
        Assert.Equal(7, (int)Platform.Instagram);
        Assert.Equal(8, (int)Platform.TikTok);
    }

    [Fact]
    public void CredentialPurpose_HasStableNumericValues()
    {
        // Persisted as int (HasConversion<int>) and embedded in the (Platform, Purpose) unique index —
        // renumbering silently corrupts data. Same guard class as Platform.
        Assert.Equal(0, (int)CredentialPurpose.Publishing);
        Assert.Equal(1, (int)CredentialPurpose.Analytics);
    }

    [Fact]
    public void SnapshotScope_HasStableNumericValues()
    {
        // Persisted as int and embedded in the snapshot unique index — renumbering corrupts data.
        Assert.Equal(0, (int)SnapshotScope.Account);
        Assert.Equal(1, (int)SnapshotScope.Video);
    }

    [Fact]
    public void ChannelMetricSnapshot_AccountScope_UsesEmptyStringVideoIdSentinel()
    {
        var snapshot = new ChannelMetricSnapshot
        {
            Platform = Platform.YouTube,
            Scope = SnapshotScope.Account
        };

        // Sentinel "" (never null) so the unique index + ON CONFLICT upsert function in PostgreSQL.
        Assert.NotNull(snapshot.VideoId);
        Assert.Equal(string.Empty, snapshot.VideoId);
    }

    [Fact]
    public void PlatformCredential_DefaultsPurposeToPublishing()
    {
        var credential = new PlatformCredential { Platform = Platform.LinkedIn };
        Assert.Equal(CredentialPurpose.Publishing, credential.Purpose);
    }

    [Fact]
    public async Task ChannelMetricSnapshotConfiguration_MetricsBag_RoundTripsThroughJsonb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var id = Guid.NewGuid();

        await using (var db = new ApplicationDbContext(options))
        {
            db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
            {
                Id = id,
                Platform = Platform.YouTube,
                SnapshotDate = new DateOnly(2026, 7, 17),
                Scope = SnapshotScope.Account,
                Metrics = new Dictionary<string, long> { ["subscribers"] = 1234, ["views"] = 99 }
            });
            await db.SaveChangesAsync();
        }

        // Fresh context forces a real deserialize through the ValueConverter (not a change-tracker cache hit).
        await using (var db = new ApplicationDbContext(options))
        {
            var reloaded = await db.ChannelMetricSnapshots.SingleAsync(s => s.Id == id);
            Assert.Equal(2, reloaded.Metrics.Count);
            Assert.Equal(1234L, reloaded.Metrics["subscribers"]);
            Assert.Equal(99L, reloaded.Metrics["views"]);
        }
    }
}
