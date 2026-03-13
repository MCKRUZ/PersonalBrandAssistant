using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
using PersonalBrandAssistant.Infrastructure.Tests.Utilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class QueryFilterTests
{
    private readonly PostgresFixture _fixture;

    public QueryFilterTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueryContent_ExcludesArchivedByDefault()
    {
        var connStr = _fixture.GetUniqueConnectionString();

        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
        await setupContext.Database.EnsureCreatedAsync();

        var active = TestEntityFactory.CreateContent(body: "Active content");
        var archived = TestEntityFactory.CreateArchivedContent(body: "Archived content");

        setupContext.Contents.AddRange(active, archived);
        await setupContext.SaveChangesAsync();

        await using var queryContext = _fixture.CreateDbContext(connectionString: connStr);
        var results = await queryContext.Contents.ToListAsync();

        Assert.Single(results);
        Assert.Equal("Active content", results[0].Body);
    }

    [Fact]
    public async Task IgnoreQueryFilters_IncludesArchivedContent()
    {
        var connStr = _fixture.GetUniqueConnectionString();

        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
        await setupContext.Database.EnsureCreatedAsync();

        var active = TestEntityFactory.CreateContent(body: "Active");
        var archived = TestEntityFactory.CreateArchivedContent(body: "Archived");

        setupContext.Contents.AddRange(active, archived);
        await setupContext.SaveChangesAsync();

        await using var queryContext = _fixture.CreateDbContext(connectionString: connStr);
        var results = await queryContext.Contents
            .IgnoreQueryFilters()
            .ToListAsync();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task FindAsync_ArchivedContent_ReturnsNullWithFilter()
    {
        var connStr = _fixture.GetUniqueConnectionString();

        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
        await setupContext.Database.EnsureCreatedAsync();

        var archived = TestEntityFactory.CreateArchivedContent();
        setupContext.Contents.Add(archived);
        await setupContext.SaveChangesAsync();
        var archivedId = archived.Id;

        await using var queryContext = _fixture.CreateDbContext(connectionString: connStr);
        var result = await queryContext.Contents.FindAsync(archivedId);

        Assert.Null(result);
    }
}
