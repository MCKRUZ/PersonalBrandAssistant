using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Infrastructure.Tests.Data;

public class PlatformCredentialPersistenceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task PlatformCredential_CanBePersisted_AndRetrieved()
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedAccessToken = "enc_access_token_123",
            EncryptedRefreshToken = "enc_refresh_token_456",
            AccessTokenExpiresAt = now.AddHours(1),
            RefreshTokenExpiresAt = now.AddDays(30),
            Scopes = "r_liteprofile w_member_social",
            IsActive = true,
            EncryptedCookies = "enc_cookies_789",
            EncryptedIntegrationToken = "enc_integration_abc"
        };

        context.PlatformCredentials.Add(credential);
        await context.SaveChangesAsync();

        var loaded = await context.PlatformCredentials.FindAsync(credential.Id);
        Assert.NotNull(loaded);
        Assert.Equal(Platform.LinkedIn, loaded.Platform);
        Assert.Equal("enc_access_token_123", loaded.EncryptedAccessToken);
        Assert.Equal("enc_refresh_token_456", loaded.EncryptedRefreshToken);
        Assert.Equal(now.AddHours(1), loaded.AccessTokenExpiresAt);
        Assert.Equal(now.AddDays(30), loaded.RefreshTokenExpiresAt);
        Assert.Equal("r_liteprofile w_member_social", loaded.Scopes);
        Assert.True(loaded.IsActive);
        Assert.Equal("enc_cookies_789", loaded.EncryptedCookies);
        Assert.Equal("enc_integration_abc", loaded.EncryptedIntegrationToken);
    }

    [Fact]
    public async Task PlatformCredential_OnlyOneActivePerPlatform_CanBeQueried()
    {
        using var context = CreateContext();
        var active = new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedAccessToken = "active_token",
            IsActive = true
        };
        var inactive = new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedAccessToken = "old_token",
            IsActive = false
        };

        context.PlatformCredentials.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var results = await context.PlatformCredentials
            .Where(c => c.Platform == Platform.LinkedIn && c.IsActive)
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal("active_token", results[0].EncryptedAccessToken);
    }

    [Fact]
    public async Task Content_TargetPlatforms_SerializesAndDeserializes()
    {
        using var context = CreateContext();
        var content = new Content
        {
            Title = "Multi-platform post",
            TargetPlatforms = [Platform.Blog, Platform.Medium, Platform.LinkedIn]
        };

        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var loaded = await context.Contents.FindAsync(content.Id);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.TargetPlatforms.Count);
        Assert.Equal(Platform.Blog, loaded.TargetPlatforms[0]);
        Assert.Equal(Platform.Medium, loaded.TargetPlatforms[1]);
        Assert.Equal(Platform.LinkedIn, loaded.TargetPlatforms[2]);
    }

    [Fact]
    public void Content_TargetPlatforms_DefaultsToEmptyList()
    {
        var content = new Content { Title = "Test" };
        Assert.NotNull(content.TargetPlatforms);
        Assert.Empty(content.TargetPlatforms);
    }

    [Fact]
    public void ContentPlatformPublish_RetryCount_DefaultsToZero()
    {
        var publish = new ContentPlatformPublish { ContentId = Guid.NewGuid() };
        Assert.Equal(0, publish.RetryCount);
    }

    [Fact]
    public void ContentPlatformPublish_NextRetryAt_DefaultsToNull()
    {
        var publish = new ContentPlatformPublish { ContentId = Guid.NewGuid() };
        Assert.Null(publish.NextRetryAt);
    }

    [Fact]
    public async Task ContentPlatformPublish_RetryFields_PersistCorrectly()
    {
        using var context = CreateContext();
        var content = new Content { Title = "Test" };
        context.Contents.Add(content);
        var futureRetry = DateTimeOffset.UtcNow.AddMinutes(30);
        var publish = new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = Platform.Medium,
            RetryCount = 2,
            NextRetryAt = futureRetry
        };

        context.ContentPlatformPublishes.Add(publish);
        await context.SaveChangesAsync();

        var loaded = await context.ContentPlatformPublishes.FindAsync(publish.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.RetryCount);
        Assert.Equal(futureRetry, loaded.NextRetryAt);
    }

    [Fact]
    public void IAppDbContext_Exposes_PlatformCredentials_DbSet()
    {
        using var context = CreateContext();
        Application.Common.Interfaces.IAppDbContext appContext = context;
        Assert.NotNull(appContext.PlatformCredentials);
    }

    [Fact]
    public void Platform_Enum_ContainsMedium()
    {
        Assert.True(Enum.IsDefined(typeof(Platform), Platform.Medium));
        Assert.True(Enum.TryParse<Platform>("Medium", out var parsed));
        Assert.Equal(Platform.Medium, parsed);
    }

    [Fact]
    public void PlatformCredential_Has_CompositeIndex_On_Platform_IsActive()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(PlatformCredential))!;
        var indexes = entity.GetIndexes().ToList();

        var compositeIndex = indexes.FirstOrDefault(i =>
        {
            var props = i.Properties.Select(p => p.Name).ToList();
            return props.Contains(nameof(PlatformCredential.Platform))
                && props.Contains(nameof(PlatformCredential.IsActive));
        });

        Assert.NotNull(compositeIndex);
    }
}
