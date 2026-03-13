using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Infrastructure.Services;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

[Collection("Postgres")]
public class AuditLogCleanupServiceTests
{
    private readonly PostgresFixture _fixture;

    public AuditLogCleanupServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Cleanup_DeletesEntriesOlderThanRetention()
    {
        var connStr = _fixture.GetUniqueConnectionString();
        await using var context = _fixture.CreateDbContext(connectionString: connStr);
        await context.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;

        var oldEntry = new AuditLogEntry
        {
            EntityType = "Content",
            EntityId = Guid.NewGuid(),
            Action = "Updated",
            Timestamp = now.AddDays(-100),
        };

        var recentEntry = new AuditLogEntry
        {
            EntityType = "Content",
            EntityId = Guid.NewGuid(),
            Action = "Created",
            Timestamp = now.AddDays(-10),
        };

        context.AuditLogEntries.AddRange(oldEntry, recentEntry);
        await context.SaveChangesAsync();

        var cutoff = now.AddDays(-90);
        var deleted = await context.AuditLogEntries
            .Where(e => e.Timestamp < cutoff)
            .ExecuteDeleteAsync();

        Assert.Equal(1, deleted);

        var remaining = await context.AuditLogEntries.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("Created", remaining[0].Action);
    }

    [Fact]
    public async Task Cleanup_EmptyTable_NoErrors()
    {
        var connStr = _fixture.GetUniqueConnectionString();
        await using var context = _fixture.CreateDbContext(connectionString: connStr);
        await context.Database.EnsureCreatedAsync();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var deleted = await context.AuditLogEntries
            .Where(e => e.Timestamp < cutoff)
            .ExecuteDeleteAsync();

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task Cleanup_EntryAtExactCutoff_IsPreserved()
    {
        var connStr = _fixture.GetUniqueConnectionString();
        await using var context = _fixture.CreateDbContext(connectionString: connStr);
        await context.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;

        var exactCutoffEntry = new AuditLogEntry
        {
            EntityType = "Content",
            EntityId = Guid.NewGuid(),
            Action = "Updated",
            Timestamp = now.AddDays(-90),
        };

        context.AuditLogEntries.Add(exactCutoffEntry);
        await context.SaveChangesAsync();

        var cutoff = now.AddDays(-90);
        var deleted = await context.AuditLogEntries
            .Where(e => e.Timestamp < cutoff)
            .ExecuteDeleteAsync();

        Assert.Equal(0, deleted);

        var remaining = await context.AuditLogEntries.ToListAsync();
        Assert.Single(remaining);
    }
}
