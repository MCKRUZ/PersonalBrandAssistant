using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace PBA.Infrastructure.Tests.Data;

// Real-DB enforcement: the composite filtered unique index on PlatformCredentials and the snapshot unique
// index only mean anything on Postgres (the InMemory provider ignores unique indexes). Mirrors the
// Testcontainers pattern in CutoverTests. Requires Docker (pgvector/pgvector:pg16).
public sealed class ChannelAnalyticsDbFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public DbContextOptions<ApplicationDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await _container.StartAsync();

        var builder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        builder.EnableDynamicJson();
        builder.UseVector();
        DataSource = builder.Build();

        Options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(DataSource, o => o.UseVector())
            .Options;

        await using var db = new ApplicationDbContext(Options);
        await db.Database.MigrateAsync();
    }

    public ApplicationDbContext CreateContext() => new(Options);

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

public class ChannelAnalyticsPersistenceTests(ChannelAnalyticsDbFixture fixture)
    : IClassFixture<ChannelAnalyticsDbFixture>
{
    private static PlatformCredential Credential(Platform platform, CredentialPurpose purpose) => new()
    {
        Platform = platform,
        Purpose = purpose,
        EncryptedAccessToken = "token",
        IsActive = true
    };

    [Fact]
    public async Task PlatformCredentialConfiguration_AllowsActivePublishingAndAnalyticsForSamePlatform()
    {
        await using var db = fixture.CreateContext();

        db.PlatformCredentials.Add(Credential(Platform.Instagram, CredentialPurpose.Publishing));
        db.PlatformCredentials.Add(Credential(Platform.Instagram, CredentialPurpose.Analytics));

        // Both persist: the unique filter is on (Platform, Purpose), not Platform alone.
        await db.SaveChangesAsync();

        var count = await db.PlatformCredentials
            .CountAsync(c => c.Platform == Platform.Instagram && c.IsActive);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PlatformCredentialConfiguration_RejectsTwoActiveAnalyticsCredentials_SamePlatform()
    {
        await using var db = fixture.CreateContext();

        db.PlatformCredentials.Add(Credential(Platform.TikTok, CredentialPurpose.Analytics));
        await db.SaveChangesAsync();

        db.PlatformCredentials.Add(Credential(Platform.TikTok, CredentialPurpose.Analytics));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateAccountRow_SamePlatformDate()
    {
        await using var db = fixture.CreateContext();
        var date = new DateOnly(2026, 1, 5);

        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Account
        });
        await db.SaveChangesAsync();

        // Same (Platform, Date, Scope=Account, VideoId="") -> unique violation.
        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Account
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateVideoRow_SamePlatformDateVideo()
    {
        await using var db = fixture.CreateContext();
        var date = new DateOnly(2026, 1, 6);

        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Video, VideoId = "abc"
        });
        await db.SaveChangesAsync();

        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Video, VideoId = "abc"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ChannelMetricSnapshotConfiguration_UniqueIndex_AllowsAccountAndVideoRows_SamePlatformDate()
    {
        await using var db = fixture.CreateContext();
        var date = new DateOnly(2026, 1, 8);

        // Same (Platform, Date) but different Scope — the discriminator must permit both rows to coexist.
        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.Instagram, SnapshotDate = date, Scope = SnapshotScope.Account
        });
        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.Instagram, SnapshotDate = date, Scope = SnapshotScope.Video, VideoId = "reel-1"
        });

        await db.SaveChangesAsync();

        var count = await db.ChannelMetricSnapshots
            .CountAsync(s => s.Platform == Platform.Instagram && s.SnapshotDate == date);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ChannelMetricSnapshotConfiguration_MetricsBag_RoundTripsThroughRealJsonb()
    {
        var id = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
            {
                Id = id,
                Platform = Platform.TikTok,
                SnapshotDate = new DateOnly(2026, 1, 7),
                Scope = SnapshotScope.Account,
                Metrics = new Dictionary<string, long> { ["followers"] = 5000, ["likes"] = 120345 }
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var reloaded = await db.ChannelMetricSnapshots.SingleAsync(s => s.Id == id);
            Assert.Equal(5000L, reloaded.Metrics["followers"]);
            Assert.Equal(120345L, reloaded.Metrics["likes"]);
        }
    }
}

// Own container: this test mutates schema (migrate up then down), so it cannot share the class fixture.
public class ChannelAnalyticsMigrationTests
{
    private const string ThisMigration = "20260717191429_AddChannelAnalytics";
    private const string PreviousMigration = "20260617144341_AddIsMicrosoftSource";

    [Fact]
    public async Task Migration_AddChannelAnalytics_AppliesAndReverts_OnCleanDb()
    {
        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await container.StartAsync();

        var builder = new NpgsqlDataSourceBuilder(container.GetConnectionString());
        builder.EnableDynamicJson();
        builder.UseVector();
        await using var dataSource = builder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .Options;

        // Apply everything up to and including AddChannelAnalytics.
        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        await using (var conn = dataSource.CreateConnection())
        {
            await conn.OpenAsync();
            Assert.True(await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"ChannelMetricSnapshots\"') IS NOT NULL"), "snapshots table");
            Assert.True(await ScalarBoolAsync(conn,
                "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='PlatformCredentials' AND column_name='Purpose')"),
                "Purpose column");
            Assert.True(await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"IX_PlatformCredentials_Platform_Purpose\"') IS NOT NULL"),
                "new composite index");
            Assert.False(await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"IX_PlatformCredentials_Platform\"') IS NOT NULL"),
                "old single-column index dropped");
        }

        // Revert AddChannelAnalytics -> the previous migration.
        await using (var db = new ApplicationDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using (var conn = dataSource.CreateConnection())
        {
            await conn.OpenAsync();
            Assert.False(await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"ChannelMetricSnapshots\"') IS NOT NULL"), "snapshots table gone");
            Assert.False(await ScalarBoolAsync(conn,
                "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='PlatformCredentials' AND column_name='Purpose')"),
                "Purpose column gone");
            Assert.True(await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"IX_PlatformCredentials_Platform\"') IS NOT NULL"),
                "old single-column index restored");
        }
    }

    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
