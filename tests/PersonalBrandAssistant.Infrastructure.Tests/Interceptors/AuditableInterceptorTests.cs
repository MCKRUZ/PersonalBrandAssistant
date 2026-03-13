using Microsoft.EntityFrameworkCore;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Interceptors;

[Collection("Postgres")]
public class AuditableInterceptorTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _connectionString;

    public AuditableInterceptorTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.GetUniqueConnectionString();
    }

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateDbContext(connectionString: _connectionString);
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var ctx = _fixture.CreateDbContext(connectionString: _connectionString);
        await ctx.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Insert_SetsCreatedAtAndUpdatedAt()
    {
        var mockTime = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(d => d.UtcNow).Returns(mockTime);

        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);

        var content = Content.Create(ContentType.BlogPost, "Test body");
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        Assert.Equal(mockTime, content.CreatedAt);
        Assert.Equal(mockTime, content.UpdatedAt);
    }

    [Fact]
    public async Task Update_UpdatesUpdatedAtPreservesCreatedAt()
    {
        var initialTime = new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);
        var updateTime = new DateTimeOffset(2026, 3, 13, 14, 0, 0, TimeSpan.Zero);

        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(d => d.UtcNow).Returns(initialTime);

        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);

        var content = Content.Create(ContentType.BlogPost, "Test body");
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        dateTimeProvider.Setup(d => d.UtcNow).Returns(updateTime);
        content.Body = "Updated body";
        await context.SaveChangesAsync();

        Assert.Equal(initialTime, content.CreatedAt);
        Assert.Equal(updateTime, content.UpdatedAt);
    }

    [Fact]
    public async Task AuditLogEntry_NotAffectedByAuditableInterceptor()
    {
        var mockTime = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(d => d.UtcNow).Returns(mockTime);

        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);

        var entry = new AuditLogEntry
        {
            EntityType = "Test",
            EntityId = Guid.NewGuid(),
            Action = "TestAction",
            Timestamp = DateTimeOffset.UtcNow,
        };

        context.AuditLogEntries.Add(entry);
        await context.SaveChangesAsync();

        var saved = await context.AuditLogEntries.FirstAsync(a => a.Id == entry.Id);
        Assert.NotNull(saved);
    }
}
