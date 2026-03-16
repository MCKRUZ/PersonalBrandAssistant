diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IMediaStorage.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IMediaStorage.cs
new file mode 100644
index 0000000..b727eb7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IMediaStorage.cs
@@ -0,0 +1,10 @@
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IMediaStorage
+{
+    Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct);
+    Task<Stream> GetStreamAsync(string fileId, CancellationToken ct);
+    Task<string> GetPathAsync(string fileId, CancellationToken ct);
+    Task<bool> DeleteAsync(string fileId, CancellationToken ct);
+    Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IOAuthManager.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IOAuthManager.cs
new file mode 100644
index 0000000..9f69902
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IOAuthManager.cs
@@ -0,0 +1,13 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IOAuthManager
+{
+    Task<Result<OAuthAuthorizationUrl>> GenerateAuthUrlAsync(PlatformType platform, CancellationToken ct);
+    Task<Result<OAuthTokens>> ExchangeCodeAsync(PlatformType platform, string code, string state, string? codeVerifier, CancellationToken ct);
+    Task<Result<OAuthTokens>> RefreshTokenAsync(PlatformType platform, CancellationToken ct);
+    Task<Result<Unit>> RevokeTokenAsync(PlatformType platform, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IPlatformContentFormatter.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IPlatformContentFormatter.cs
new file mode 100644
index 0000000..b46799a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IPlatformContentFormatter.cs
@@ -0,0 +1,11 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IPlatformContentFormatter
+{
+    PlatformType Platform { get; }
+    Result<PlatformContent> FormatAndValidate(Content content);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IRateLimiter.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IRateLimiter.cs
new file mode 100644
index 0000000..67dcd21
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IRateLimiter.cs
@@ -0,0 +1,11 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IRateLimiter
+{
+    Task<RateLimitDecision> CanMakeRequestAsync(PlatformType platform, string endpoint, CancellationToken ct);
+    Task RecordRequestAsync(PlatformType platform, string endpoint, int remaining, DateTimeOffset? resetAt, CancellationToken ct);
+    Task<RateLimitStatus> GetStatusAsync(PlatformType platform, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs
new file mode 100644
index 0000000..bd02b94
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs
@@ -0,0 +1,15 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface ISocialPlatform
+{
+    PlatformType Type { get; }
+    Task<Result<PublishResult>> PublishAsync(PlatformContent content, CancellationToken ct);
+    Task<Result<Unit>> DeletePostAsync(string platformPostId, CancellationToken ct);
+    Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct);
+    Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct);
+    Task<Result<ContentValidation>> ValidateContentAsync(PlatformContent content, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentValidation.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentValidation.cs
new file mode 100644
index 0000000..f9f2278
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentValidation.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record ContentValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/EngagementStats.cs b/src/PersonalBrandAssistant.Application/Common/Models/EngagementStats.cs
new file mode 100644
index 0000000..f32bd35
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/EngagementStats.cs
@@ -0,0 +1,9 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record EngagementStats(
+    int Likes,
+    int Comments,
+    int Shares,
+    int Impressions,
+    int Clicks,
+    Dictionary<string, int> PlatformSpecific);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs
new file mode 100644
index 0000000..c1c1e34
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs
@@ -0,0 +1,7 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class MediaStorageOptions
+{
+    public string BasePath { get; set; } = "./media";
+    public string? SigningKey { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/OAuthTokens.cs b/src/PersonalBrandAssistant.Application/Common/Models/OAuthTokens.cs
new file mode 100644
index 0000000..611044c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/OAuthTokens.cs
@@ -0,0 +1,5 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record OAuthAuthorizationUrl(string Url, string State);
+
+public record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string[]? GrantedScopes);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PlatformContent.cs b/src/PersonalBrandAssistant.Application/Common/Models/PlatformContent.cs
new file mode 100644
index 0000000..53a93c8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PlatformContent.cs
@@ -0,0 +1,12 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record PlatformContent(
+    string Text,
+    string? Title,
+    ContentType ContentType,
+    IReadOnlyList<MediaFile> Media,
+    Dictionary<string, string> Metadata);
+
+public record MediaFile(string FileId, string MimeType, string? AltText);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PlatformIntegrationOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/PlatformIntegrationOptions.cs
new file mode 100644
index 0000000..ed69c5d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PlatformIntegrationOptions.cs
@@ -0,0 +1,17 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class PlatformIntegrationOptions
+{
+    public PlatformOptions Twitter { get; set; } = new();
+    public PlatformOptions LinkedIn { get; set; } = new();
+    public PlatformOptions Instagram { get; set; } = new();
+    public PlatformOptions YouTube { get; set; } = new();
+}
+
+public class PlatformOptions
+{
+    public string CallbackUrl { get; set; } = string.Empty;
+    public string? BaseUrl { get; set; }
+    public string? ApiVersion { get; set; }
+    public int? DailyQuotaLimit { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PlatformProfile.cs b/src/PersonalBrandAssistant.Application/Common/Models/PlatformProfile.cs
new file mode 100644
index 0000000..996f304
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PlatformProfile.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record PlatformProfile(string PlatformUserId, string DisplayName, string? AvatarUrl, int? FollowerCount);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PublishResult.cs b/src/PersonalBrandAssistant.Application/Common/Models/PublishResult.cs
new file mode 100644
index 0000000..ea98582
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PublishResult.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record PublishResult(string PlatformPostId, string PostUrl, DateTimeOffset PublishedAt);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/RateLimitDecision.cs b/src/PersonalBrandAssistant.Application/Common/Models/RateLimitDecision.cs
new file mode 100644
index 0000000..2bb3f08
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/RateLimitDecision.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record RateLimitDecision(bool Allowed, DateTimeOffset? RetryAt, string? Reason);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/RateLimitStatus.cs b/src/PersonalBrandAssistant.Application/Common/Models/RateLimitStatus.cs
new file mode 100644
index 0000000..dfeaf16
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/RateLimitStatus.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record RateLimitStatus(int? RemainingCalls, DateTimeOffset? ResetAt, bool IsLimited);
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Common/Interfaces/PlatformInterfacesTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Common/Interfaces/PlatformInterfacesTests.cs
new file mode 100644
index 0000000..13fdf08
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Common/Interfaces/PlatformInterfacesTests.cs
@@ -0,0 +1,61 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Application.Tests.Common.Interfaces;
+
+public class PlatformInterfacesTests
+{
+    [Fact]
+    public void ISocialPlatform_DefinesRequiredMembers()
+    {
+        var type = typeof(ISocialPlatform);
+
+        Assert.NotNull(type.GetProperty("Type"));
+        Assert.NotNull(type.GetMethod("PublishAsync"));
+        Assert.NotNull(type.GetMethod("DeletePostAsync"));
+        Assert.NotNull(type.GetMethod("GetEngagementAsync"));
+        Assert.NotNull(type.GetMethod("GetProfileAsync"));
+        Assert.NotNull(type.GetMethod("ValidateContentAsync"));
+    }
+
+    [Fact]
+    public void IOAuthManager_DefinesOAuthLifecycleMethods()
+    {
+        var type = typeof(IOAuthManager);
+
+        Assert.NotNull(type.GetMethod("GenerateAuthUrlAsync"));
+        Assert.NotNull(type.GetMethod("ExchangeCodeAsync"));
+        Assert.NotNull(type.GetMethod("RefreshTokenAsync"));
+        Assert.NotNull(type.GetMethod("RevokeTokenAsync"));
+    }
+
+    [Fact]
+    public void IRateLimiter_DefinesRateLimitMethods()
+    {
+        var type = typeof(IRateLimiter);
+
+        Assert.NotNull(type.GetMethod("CanMakeRequestAsync"));
+        Assert.NotNull(type.GetMethod("RecordRequestAsync"));
+        Assert.NotNull(type.GetMethod("GetStatusAsync"));
+    }
+
+    [Fact]
+    public void IMediaStorage_DefinesStorageMethods()
+    {
+        var type = typeof(IMediaStorage);
+
+        Assert.NotNull(type.GetMethod("SaveAsync"));
+        Assert.NotNull(type.GetMethod("GetStreamAsync"));
+        Assert.NotNull(type.GetMethod("GetPathAsync"));
+        Assert.NotNull(type.GetMethod("DeleteAsync"));
+        Assert.NotNull(type.GetMethod("GetSignedUrlAsync"));
+    }
+
+    [Fact]
+    public void IPlatformContentFormatter_DefinesPlatformAndFormatAndValidate()
+    {
+        var type = typeof(IPlatformContentFormatter);
+
+        Assert.NotNull(type.GetProperty("Platform"));
+        Assert.NotNull(type.GetMethod("FormatAndValidate"));
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Common/Models/PlatformIntegrationModelsTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Common/Models/PlatformIntegrationModelsTests.cs
new file mode 100644
index 0000000..e7738a2
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Common/Models/PlatformIntegrationModelsTests.cs
@@ -0,0 +1,85 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Tests.Common.Models;
+
+public class PlatformIntegrationModelsTests
+{
+    [Fact]
+    public void PlatformContent_RecordEquality_WithSameValues()
+    {
+        var media = new List<MediaFile>();
+        var metadata = new Dictionary<string, string> { ["key"] = "value" };
+
+        var a = new PlatformContent("Hello", null, ContentType.SocialPost, media, metadata);
+        var b = new PlatformContent("Hello", null, ContentType.SocialPost, media, metadata);
+
+        Assert.Equal(a, b);
+    }
+
+    [Fact]
+    public void MediaFile_UsesFileId_NotFilePath()
+    {
+        var file = new MediaFile("2026-03-abc.jpg", "image/jpeg", "Alt text");
+
+        Assert.Equal("2026-03-abc.jpg", file.FileId);
+        Assert.Equal("image/jpeg", file.MimeType);
+        Assert.Equal("Alt text", file.AltText);
+
+        var properties = typeof(MediaFile).GetProperties();
+        Assert.DoesNotContain(properties, p => p.Name == "FilePath");
+    }
+
+    [Fact]
+    public void RateLimitDecision_WhenNotAllowed_HasRetryAtAndReason()
+    {
+        var retryAt = DateTimeOffset.UtcNow.AddMinutes(15);
+        var decision = new RateLimitDecision(false, retryAt, "Rate limit exceeded");
+
+        Assert.False(decision.Allowed);
+        Assert.NotNull(decision.RetryAt);
+        Assert.Equal(retryAt, decision.RetryAt);
+        Assert.Equal("Rate limit exceeded", decision.Reason);
+    }
+
+    [Fact]
+    public void OAuthTokens_StoresGrantedScopesArray()
+    {
+        var scopes = new[] { "tweet.read", "tweet.write", "offline.access" };
+        var tokens = new OAuthTokens("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), scopes);
+
+        Assert.NotNull(tokens.GrantedScopes);
+        Assert.Equal(3, tokens.GrantedScopes.Length);
+        Assert.Contains("tweet.read", tokens.GrantedScopes);
+    }
+
+    [Fact]
+    public void PlatformIntegrationOptions_HasPerPlatformSubOptions()
+    {
+        var options = new PlatformIntegrationOptions();
+
+        Assert.NotNull(options.Twitter);
+        Assert.NotNull(options.LinkedIn);
+        Assert.NotNull(options.Instagram);
+        Assert.NotNull(options.YouTube);
+
+        options.Twitter.CallbackUrl = "http://localhost/callback";
+        options.Twitter.BaseUrl = "https://api.x.com/2";
+
+        Assert.Equal("http://localhost/callback", options.Twitter.CallbackUrl);
+        Assert.Equal("https://api.x.com/2", options.Twitter.BaseUrl);
+    }
+
+    [Fact]
+    public void MediaStorageOptions_BindsBasePathAndSigningKey()
+    {
+        var options = new MediaStorageOptions
+        {
+            BasePath = "/tmp/media",
+            SigningKey = "test-key",
+        };
+
+        Assert.Equal("/tmp/media", options.BasePath);
+        Assert.Equal("test-key", options.SigningKey);
+    }
+}
