using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
using PersonalBrandAssistant.Infrastructure.Tests.Utilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class ConcurrencyTests
{
    private readonly PostgresFixture _fixture;

    public ConcurrencyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentUpdate_SameContent_ThrowsDbUpdateConcurrencyException()
    {
        var connStr = _fixture.GetUniqueConnectionString();

        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
        await setupContext.Database.EnsureCreatedAsync();

        var content = TestEntityFactory.CreateContent();
        setupContext.Contents.Add(content);
        await setupContext.SaveChangesAsync();
        var contentId = content.Id;

        await using var context1 = _fixture.CreateDbContext(connectionString: connStr);
        await using var context2 = _fixture.CreateDbContext(connectionString: connStr);

        var content1 = await context1.Contents.FindAsync(contentId);
        var content2 = await context2.Contents.FindAsync(contentId);
        Assert.NotNull(content1);
        Assert.NotNull(content2);

        content1.Body = "Updated by context 1";
        await context1.SaveChangesAsync();

        content2.Body = "Updated by context 2";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => context2.SaveChangesAsync());
    }

    [Fact]
    public async Task SequentialUpdates_WithFreshReads_Succeed()
    {
        var connStr = _fixture.GetUniqueConnectionString();

        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
        await setupContext.Database.EnsureCreatedAsync();

        var content = TestEntityFactory.CreateContent();
        setupContext.Contents.Add(content);
        await setupContext.SaveChangesAsync();
        var contentId = content.Id;

        await using var context1 = _fixture.CreateDbContext(connectionString: connStr);
        var c1 = await context1.Contents.FindAsync(contentId);
        Assert.NotNull(c1);
        c1.Body = "First update";
        await context1.SaveChangesAsync();

        await using var context2 = _fixture.CreateDbContext(connectionString: connStr);
        var c2 = await context2.Contents.FindAsync(contentId);
        Assert.NotNull(c2);
        c2.Body = "Second update";
        await context2.SaveChangesAsync();

        await using var verifyContext = _fixture.CreateDbContext(connectionString: connStr);
        var result = await verifyContext.Contents.FindAsync(contentId);
        Assert.NotNull(result);
        Assert.Equal("Second update", result.Body);
    }

    [Fact]
    public async Task ConcurrentUpdate_SamePlatform_ThrowsDbUpdateConcurrencyException()
    {
        var connStr = _fixture.GetUniqueConnectionString();

        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
        await setupContext.Database.EnsureCreatedAsync();

        var platform = TestEntityFactory.CreatePlatform();
        setupContext.Platforms.Add(platform);
        await setupContext.SaveChangesAsync();
        var platformId = platform.Id;

        await using var context1 = _fixture.CreateDbContext(connectionString: connStr);
        await using var context2 = _fixture.CreateDbContext(connectionString: connStr);

        var p1 = await context1.Platforms.FindAsync(platformId);
        var p2 = await context2.Platforms.FindAsync(platformId);
        Assert.NotNull(p1);
        Assert.NotNull(p2);

        p1.DisplayName = "Updated by context 1";
        await context1.SaveChangesAsync();

        p2.DisplayName = "Updated by context 2";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => context2.SaveChangesAsync());
    }
}
