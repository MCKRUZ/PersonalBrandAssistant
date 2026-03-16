diff --git a/src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs b/src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs
new file mode 100644
index 0000000..a1bc1f2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs
@@ -0,0 +1,19 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class ContentPlatformStatus : AuditableEntityBase
+{
+    public Guid ContentId { get; set; }
+    public PlatformType Platform { get; set; }
+    public PlatformPublishStatus Status { get; set; } = PlatformPublishStatus.Pending;
+    public string? PlatformPostId { get; set; }
+    public string? PostUrl { get; set; }
+    public string? ErrorMessage { get; set; }
+    public string? IdempotencyKey { get; init; }
+    public int RetryCount { get; set; } = 0;
+    public DateTimeOffset? NextRetryAt { get; set; }
+    public DateTimeOffset? PublishedAt { get; set; }
+    public uint Version { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/OAuthState.cs b/src/PersonalBrandAssistant.Domain/Entities/OAuthState.cs
new file mode 100644
index 0000000..0b98573
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/OAuthState.cs
@@ -0,0 +1,13 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class OAuthState : EntityBase
+{
+    public string State { get; set; } = string.Empty;
+    public PlatformType Platform { get; set; }
+    public string? CodeVerifier { get; set; }
+    public DateTimeOffset CreatedAt { get; set; }
+    public DateTimeOffset ExpiresAt { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/Platform.cs b/src/PersonalBrandAssistant.Domain/Entities/Platform.cs
index cdc2c1b..0403c8f 100644
--- a/src/PersonalBrandAssistant.Domain/Entities/Platform.cs
+++ b/src/PersonalBrandAssistant.Domain/Entities/Platform.cs
@@ -12,6 +12,7 @@ public class Platform : AuditableEntityBase
     public byte[]? EncryptedAccessToken { get; set; }
     public byte[]? EncryptedRefreshToken { get; set; }
     public DateTimeOffset? TokenExpiresAt { get; set; }
+    public string[]? GrantedScopes { get; set; }
     public PlatformRateLimitState RateLimitState { get; set; } = new();
     public DateTimeOffset? LastSyncAt { get; set; }
     public PlatformSettings Settings { get; set; } = new();
diff --git a/src/PersonalBrandAssistant.Domain/Enums/PlatformPublishStatus.cs b/src/PersonalBrandAssistant.Domain/Enums/PlatformPublishStatus.cs
new file mode 100644
index 0000000..4093372
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/PlatformPublishStatus.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum PlatformPublishStatus { Pending, Published, Failed, RateLimited, Skipped, Processing }
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs b/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs
index 193146a..63ff3a3 100644
--- a/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs
+++ b/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs
@@ -5,4 +5,14 @@ public class PlatformRateLimitState
     public int? RemainingCalls { get; set; }
     public DateTimeOffset? ResetAt { get; set; }
     public TimeSpan? WindowDuration { get; set; }
+    public Dictionary<string, EndpointRateLimit> Endpoints { get; set; } = new();
+    public int? DailyQuotaUsed { get; set; }
+    public int? DailyQuotaLimit { get; set; }
+    public DateTimeOffset? QuotaResetAt { get; set; }
+}
+
+public class EndpointRateLimit
+{
+    public int? RemainingCalls { get; set; }
+    public DateTimeOffset? ResetAt { get; set; }
 }
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentPlatformStatusTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentPlatformStatusTests.cs
new file mode 100644
index 0000000..151f99f
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentPlatformStatusTests.cs
@@ -0,0 +1,66 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class ContentPlatformStatusTests
+{
+    [Fact]
+    public void ContentPlatformStatus_DefaultsTo_PendingStatus_And_ZeroRetries()
+    {
+        var status = new ContentPlatformStatus
+        {
+            ContentId = Guid.NewGuid(),
+            Platform = PlatformType.TwitterX,
+        };
+
+        Assert.Equal(PlatformPublishStatus.Pending, status.Status);
+        Assert.Equal(0, status.RetryCount);
+    }
+
+    [Fact]
+    public void IdempotencyKey_CanBeSetViaInitProperty()
+    {
+        var key = "sha256-hash-value";
+        var status = new ContentPlatformStatus
+        {
+            ContentId = Guid.NewGuid(),
+            Platform = PlatformType.LinkedIn,
+            IdempotencyKey = key,
+        };
+
+        Assert.Equal(key, status.IdempotencyKey);
+    }
+
+    [Fact]
+    public void ContentPlatformStatus_GetsValidGuidId_FromEntityBase()
+    {
+        var status = new ContentPlatformStatus
+        {
+            ContentId = Guid.NewGuid(),
+            Platform = PlatformType.Instagram,
+        };
+
+        Assert.NotEqual(Guid.Empty, status.Id);
+    }
+
+    [Fact]
+    public void ContentPlatformStatus_StoresAllPublishOutcomeFields()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var status = new ContentPlatformStatus
+        {
+            ContentId = Guid.NewGuid(),
+            Platform = PlatformType.YouTube,
+            PlatformPostId = "yt-video-123",
+            PostUrl = "https://youtube.com/watch?v=123",
+            PublishedAt = now,
+            ErrorMessage = "some error",
+        };
+
+        Assert.Equal("yt-video-123", status.PlatformPostId);
+        Assert.Equal("https://youtube.com/watch?v=123", status.PostUrl);
+        Assert.Equal(now, status.PublishedAt);
+        Assert.Equal("some error", status.ErrorMessage);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/OAuthStateTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/OAuthStateTests.cs
new file mode 100644
index 0000000..31ee4cf
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/OAuthStateTests.cs
@@ -0,0 +1,54 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class OAuthStateTests
+{
+    [Fact]
+    public void ExpiresAt_IsSetRelativeTo_CreatedAt()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var state = new OAuthState
+        {
+            State = "random-state",
+            Platform = PlatformType.TwitterX,
+            CreatedAt = now,
+            ExpiresAt = now.AddMinutes(10),
+        };
+
+        Assert.True(state.ExpiresAt > state.CreatedAt);
+        Assert.Equal(TimeSpan.FromMinutes(10), state.ExpiresAt - state.CreatedAt);
+    }
+
+    [Fact]
+    public void OAuthState_StoresAllFields()
+    {
+        var state = new OAuthState
+        {
+            State = "csrf-state-token",
+            Platform = PlatformType.TwitterX,
+            CodeVerifier = "pkce-code-verifier",
+            CreatedAt = DateTimeOffset.UtcNow,
+            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
+        };
+
+        Assert.Equal("csrf-state-token", state.State);
+        Assert.Equal(PlatformType.TwitterX, state.Platform);
+        Assert.Equal("pkce-code-verifier", state.CodeVerifier);
+    }
+
+    [Fact]
+    public void OAuthState_GetsValidGuidId()
+    {
+        var state = new OAuthState
+        {
+            State = "test",
+            Platform = PlatformType.LinkedIn,
+            CreatedAt = DateTimeOffset.UtcNow,
+            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
+        };
+
+        Assert.NotEqual(Guid.Empty, state.Id);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs
index 5293094..55ed9cb 100644
--- a/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs
@@ -35,4 +35,32 @@ public class PlatformTests
         Assert.IsType<byte[]>(platform.EncryptedAccessToken);
         Assert.IsType<byte[]>(platform.EncryptedRefreshToken);
     }
+
+    [Fact]
+    public void GrantedScopes_StoresAndRetrievesStringArray()
+    {
+        var platform = new Platform
+        {
+            Type = PlatformType.TwitterX,
+            DisplayName = "Twitter/X",
+            GrantedScopes = new[] { "tweet.read", "tweet.write" },
+        };
+
+        Assert.NotNull(platform.GrantedScopes);
+        Assert.Equal(2, platform.GrantedScopes.Length);
+        Assert.Contains("tweet.read", platform.GrantedScopes);
+        Assert.Contains("tweet.write", platform.GrantedScopes);
+    }
+
+    [Fact]
+    public void GrantedScopes_DefaultsToNull()
+    {
+        var platform = new Platform
+        {
+            Type = PlatformType.Instagram,
+            DisplayName = "Instagram",
+        };
+
+        Assert.Null(platform.GrantedScopes);
+    }
 }
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
index 1e2e3d1..335f9c4 100644
--- a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
@@ -99,4 +99,17 @@ public class EnumTests
         Assert.Contains(ModelTier.Standard, values);
         Assert.Contains(ModelTier.Advanced, values);
     }
+
+    [Fact]
+    public void PlatformPublishStatus_HasExactly6Values()
+    {
+        var values = Enum.GetValues<PlatformPublishStatus>();
+        Assert.Equal(6, values.Length);
+        Assert.Contains(PlatformPublishStatus.Pending, values);
+        Assert.Contains(PlatformPublishStatus.Published, values);
+        Assert.Contains(PlatformPublishStatus.Failed, values);
+        Assert.Contains(PlatformPublishStatus.RateLimited, values);
+        Assert.Contains(PlatformPublishStatus.Skipped, values);
+        Assert.Contains(PlatformPublishStatus.Processing, values);
+    }
 }
