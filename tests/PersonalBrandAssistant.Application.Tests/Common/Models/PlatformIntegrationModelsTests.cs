using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Tests.Common.Models;

public class PlatformIntegrationModelsTests
{
    [Fact]
    public void PlatformContent_RecordEquality_WithSameValues()
    {
        var media = new List<MediaFile>();
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        var a = new PlatformContent("Hello", null, ContentType.SocialPost, media, metadata);
        var b = new PlatformContent("Hello", null, ContentType.SocialPost, media, metadata);

        Assert.Equal(a, b);
    }

    [Fact]
    public void MediaFile_UsesFileId_NotFilePath()
    {
        var file = new MediaFile("2026-03-abc.jpg", "image/jpeg", "Alt text");

        Assert.Equal("2026-03-abc.jpg", file.FileId);
        Assert.Equal("image/jpeg", file.MimeType);
        Assert.Equal("Alt text", file.AltText);

        var properties = typeof(MediaFile).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name == "FilePath");
    }

    [Fact]
    public void RateLimitDecision_WhenNotAllowed_HasRetryAtAndReason()
    {
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var decision = new RateLimitDecision(false, retryAt, "Rate limit exceeded");

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.RetryAt);
        Assert.Equal(retryAt, decision.RetryAt);
        Assert.Equal("Rate limit exceeded", decision.Reason);
    }

    [Fact]
    public void OAuthTokens_StoresGrantedScopesArray()
    {
        var scopes = new[] { "tweet.read", "tweet.write", "offline.access" };
        var tokens = new OAuthTokens("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), scopes);

        Assert.NotNull(tokens.GrantedScopes);
        Assert.Equal(3, tokens.GrantedScopes.Count);
        Assert.Contains("tweet.read", tokens.GrantedScopes);
    }

    [Fact]
    public void PlatformIntegrationOptions_HasPerPlatformSubOptions()
    {
        var options = new PlatformIntegrationOptions();

        Assert.NotNull(options.Twitter);
        Assert.NotNull(options.LinkedIn);
        Assert.NotNull(options.Instagram);
        Assert.NotNull(options.YouTube);

        options.Twitter.CallbackUrl = "http://localhost/callback";
        options.Twitter.BaseUrl = "https://api.x.com/2";

        Assert.Equal("http://localhost/callback", options.Twitter.CallbackUrl);
        Assert.Equal("https://api.x.com/2", options.Twitter.BaseUrl);
    }

    [Fact]
    public void MediaStorageOptions_BindsBasePathAndSigningKey()
    {
        var options = new MediaStorageOptions
        {
            BasePath = "/tmp/media",
            SigningKey = "test-key",
        };

        Assert.Equal("/tmp/media", options.BasePath);
        Assert.Equal("test-key", options.SigningKey);
    }
}
