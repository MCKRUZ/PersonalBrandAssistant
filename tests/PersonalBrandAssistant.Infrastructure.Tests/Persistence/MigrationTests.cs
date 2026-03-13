using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class MigrationTests
{
    private readonly PostgresFixture _fixture;

    public MigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureCreated_AppliesSchemaCleanly()
    {
        var connStr = _fixture.GetUniqueConnectionString();
        await using var context = _fixture.CreateDbContext(connectionString: connStr);

        await context.Database.EnsureCreatedAsync();

        var tables = await context.Database
            .SqlQueryRaw<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        Assert.Contains("Contents", tables);
        Assert.Contains("Platforms", tables);
        Assert.Contains("BrandProfiles", tables);
        Assert.Contains("ContentCalendarSlots", tables);
        Assert.Contains("AuditLogEntries", tables);
        Assert.Contains("Users", tables);
    }
}
