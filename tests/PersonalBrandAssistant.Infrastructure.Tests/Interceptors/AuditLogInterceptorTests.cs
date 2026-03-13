using Microsoft.EntityFrameworkCore;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Interceptors;

[Collection("Postgres")]
public class AuditLogInterceptorTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _connectionString;

    public AuditLogInterceptorTests(PostgresFixture fixture)
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
    public async Task ModifyContent_CreatesAuditLogEntry()
    {
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(d => d.UtcNow).Returns(DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);

        var content = Content.Create(ContentType.BlogPost, "Original body");
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        content.Body = "Modified body";
        await context.SaveChangesAsync();

        var auditEntries = await context.AuditLogEntries
            .Where(a => a.EntityId == content.Id && a.Action == "Modified")
            .ToListAsync();

        Assert.NotEmpty(auditEntries);
        Assert.Equal("Content", auditEntries.First().EntityType);
    }

    [Fact]
    public async Task AuditEntry_ExcludesEncryptedFields()
    {
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(d => d.UtcNow).Returns(DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);

        var platform = new Platform
        {
            Type = PlatformType.TwitterX,
            DisplayName = "Twitter/X",
            IsConnected = true,
            EncryptedAccessToken = new byte[] { 1, 2, 3 },
        };
        context.Platforms.Add(platform);
        await context.SaveChangesAsync();

        var auditEntry = await context.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EntityId == platform.Id && a.Action == "Created");

        Assert.NotNull(auditEntry);
        Assert.DoesNotContain("EncryptedAccessToken", auditEntry!.NewValue ?? "");
        Assert.DoesNotContain("EncryptedRefreshToken", auditEntry.NewValue ?? "");
    }

    [Fact]
    public async Task AuditEntry_OldValueNewValue_AreValidJson()
    {
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(d => d.UtcNow).Returns(DateTimeOffset.UtcNow);

        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);

        var content = Content.Create(ContentType.BlogPost, "Original");
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        content.Body = "Updated";
        await context.SaveChangesAsync();

        var auditEntry = await context.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EntityId == content.Id && a.Action == "Modified");

        Assert.NotNull(auditEntry);
        Assert.NotNull(auditEntry!.NewValue);

        var newDoc = System.Text.Json.JsonDocument.Parse(auditEntry.NewValue!);
        Assert.NotNull(newDoc);

        if (auditEntry.OldValue is not null)
        {
            var oldDoc = System.Text.Json.JsonDocument.Parse(auditEntry.OldValue);
            Assert.NotNull(oldDoc);
        }
    }
}
