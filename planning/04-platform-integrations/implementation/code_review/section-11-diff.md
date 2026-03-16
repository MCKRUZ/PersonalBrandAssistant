diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs
index bd02b94..11db6e6 100644
--- a/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs
@@ -12,4 +12,5 @@ public interface ISocialPlatform
     Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct);
     Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct);
     Task<Result<ContentValidation>> ValidateContentAsync(PlatformContent content, CancellationToken ct);
+    Task<Result<PlatformPublishStatusCheck>> CheckPublishStatusAsync(string platformPostId, CancellationToken ct);
 }
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PlatformPublishStatusCheck.cs b/src/PersonalBrandAssistant.Application/Common/Models/PlatformPublishStatusCheck.cs
new file mode 100644
index 0000000..6f08db9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PlatformPublishStatusCheck.cs
@@ -0,0 +1,5 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record PlatformPublishStatusCheck(PlatformPublishStatus Status, string? PostUrl, string? ErrorMessage);
diff --git a/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs b/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs
index d39c07d..8467eac 100644
--- a/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs
+++ b/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs
@@ -6,5 +6,8 @@ public enum NotificationType
     ContentApproved,
     ContentRejected,
     ContentPublished,
-    ContentFailed
+    ContentFailed,
+    PlatformDisconnected,
+    PlatformTokenExpiring,
+    PlatformScopeMismatch
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs
new file mode 100644
index 0000000..f608a56
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs
@@ -0,0 +1,124 @@
+using System.Collections.Frozen;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class PlatformHealthMonitor : BackgroundService
+{
+    private static readonly FrozenDictionary<PlatformType, string[]> RequiredScopes =
+        new Dictionary<PlatformType, string[]>
+        {
+            [PlatformType.TwitterX] = ["tweet.read", "tweet.write", "users.read", "offline.access"],
+            [PlatformType.LinkedIn] = ["w_member_social", "r_liteprofile"],
+            [PlatformType.Instagram] = ["instagram_basic", "instagram_content_publish", "pages_show_list"],
+            [PlatformType.YouTube] = ["youtube", "youtube.upload"],
+        }.ToFrozenDictionary();
+
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IDateTimeProvider _dateTimeProvider;
+    private readonly ILogger<PlatformHealthMonitor> _logger;
+
+    public PlatformHealthMonitor(
+        IServiceScopeFactory scopeFactory,
+        IDateTimeProvider dateTimeProvider,
+        ILogger<PlatformHealthMonitor> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _dateTimeProvider = dateTimeProvider;
+        _logger = logger;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await CheckPlatformHealthAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during {Processor} processing", nameof(PlatformHealthMonitor));
+                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
+            }
+        }
+    }
+
+    internal async Task CheckPlatformHealthAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
+        var adapters = scope.ServiceProvider.GetRequiredService<IEnumerable<ISocialPlatform>>();
+        var oauthManager = scope.ServiceProvider.GetRequiredService<IOAuthManager>();
+        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
+        var now = _dateTimeProvider.UtcNow;
+
+        var connectedPlatforms = await db.Platforms
+            .Where(p => p.IsConnected)
+            .ToListAsync(ct);
+
+        foreach (var platform in connectedPlatforms)
+        {
+            var adapter = adapters.FirstOrDefault(a => a.Type == platform.Type);
+            if (adapter is null)
+            {
+                _logger.LogWarning("No adapter found for connected platform {Platform}", platform.Type);
+                continue;
+            }
+
+            var profileResult = await adapter.GetProfileAsync(ct);
+
+            if (profileResult.IsSuccess)
+            {
+                platform.LastSyncAt = now;
+                await db.SaveChangesAsync(ct);
+            }
+            else
+            {
+                var isAuthError = profileResult.ErrorCode == ErrorCode.Unauthorized ||
+                                  profileResult.Errors.Any(e => e.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
+                                                                 e.Contains("401", StringComparison.OrdinalIgnoreCase));
+
+                if (isAuthError)
+                {
+                    _logger.LogWarning("Auth failure for {Platform}, attempting token refresh", platform.Type);
+                    await oauthManager.RefreshTokenAsync(platform.Type, ct);
+                }
+                else
+                {
+                    _logger.LogWarning("Health check failed for {Platform}: {Errors}",
+                        platform.Type, string.Join(", ", profileResult.Errors));
+                }
+            }
+
+            // Check scope integrity
+            if (RequiredScopes.TryGetValue(platform.Type, out var required) && platform.GrantedScopes is not null)
+            {
+                var missing = required.Except(platform.GrantedScopes).ToArray();
+                if (missing.Length > 0)
+                {
+                    _logger.LogWarning("{Platform} is missing required scopes: {Scopes}",
+                        platform.Type, string.Join(", ", missing));
+
+                    await notifications.SendAsync(
+                        NotificationType.PlatformScopeMismatch,
+                        $"{platform.DisplayName} scope mismatch",
+                        $"Missing required scopes: {string.Join(", ", missing)}. Please reconnect to grant permissions.",
+                        ct: ct);
+                }
+            }
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs
new file mode 100644
index 0000000..d5b5b3a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs
@@ -0,0 +1,112 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class PublishCompletionPoller : BackgroundService
+{
+    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(30);
+
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IDateTimeProvider _dateTimeProvider;
+    private readonly ILogger<PublishCompletionPoller> _logger;
+
+    public PublishCompletionPoller(
+        IServiceScopeFactory scopeFactory,
+        IDateTimeProvider dateTimeProvider,
+        ILogger<PublishCompletionPoller> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _dateTimeProvider = dateTimeProvider;
+        _logger = logger;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await PollProcessingEntriesAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during {Processor} processing", nameof(PublishCompletionPoller));
+                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
+            }
+        }
+    }
+
+    internal async Task PollProcessingEntriesAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
+        var adapters = scope.ServiceProvider.GetRequiredService<IEnumerable<ISocialPlatform>>();
+        var now = _dateTimeProvider.UtcNow;
+
+        var processingEntries = await db.ContentPlatformStatuses
+            .Where(e => e.Status == PlatformPublishStatus.Processing)
+            .ToListAsync(ct);
+
+        if (processingEntries.Count == 0)
+            return;
+
+        foreach (var entry in processingEntries)
+        {
+            if (now - entry.CreatedAt > ProcessingTimeout)
+            {
+                entry.Status = PlatformPublishStatus.Failed;
+                entry.ErrorMessage = "Processing timed out after 30 minutes";
+                _logger.LogWarning("Processing timed out for {ContentId} on {Platform}",
+                    entry.ContentId, entry.Platform);
+                continue;
+            }
+
+            var adapter = adapters.FirstOrDefault(a => a.Type == entry.Platform);
+            if (adapter is null)
+            {
+                _logger.LogWarning("No adapter found for platform {Platform}", entry.Platform);
+                continue;
+            }
+
+            var statusResult = await adapter.CheckPublishStatusAsync(entry.PlatformPostId!, ct);
+            if (!statusResult.IsSuccess)
+            {
+                _logger.LogWarning("Failed to check publish status for {ContentId} on {Platform}: {Errors}",
+                    entry.ContentId, entry.Platform, string.Join(", ", statusResult.Errors));
+                continue;
+            }
+
+            var check = statusResult.Value!;
+            if (check.Status == PlatformPublishStatus.Published)
+            {
+                entry.Status = PlatformPublishStatus.Published;
+                entry.PublishedAt = now;
+                if (check.PostUrl is not null)
+                    entry.PostUrl = check.PostUrl;
+
+                _logger.LogInformation("Processing completed for {ContentId} on {Platform}",
+                    entry.ContentId, entry.Platform);
+            }
+            else if (check.Status == PlatformPublishStatus.Failed)
+            {
+                entry.Status = PlatformPublishStatus.Failed;
+                entry.ErrorMessage = check.ErrorMessage ?? "Processing failed on platform";
+                _logger.LogWarning("Processing failed for {ContentId} on {Platform}: {Error}",
+                    entry.ContentId, entry.Platform, check.ErrorMessage);
+            }
+        }
+
+        await db.SaveChangesAsync(ct);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs
new file mode 100644
index 0000000..d825f2e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs
@@ -0,0 +1,115 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+
+public class TokenRefreshProcessor : BackgroundService
+{
+    private static readonly TimeSpan TwitterRefreshThreshold = TimeSpan.FromMinutes(30);
+    private static readonly TimeSpan LongLivedRefreshThreshold = TimeSpan.FromDays(10);
+
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IDateTimeProvider _dateTimeProvider;
+    private readonly ILogger<TokenRefreshProcessor> _logger;
+
+    public TokenRefreshProcessor(
+        IServiceScopeFactory scopeFactory,
+        IDateTimeProvider dateTimeProvider,
+        ILogger<TokenRefreshProcessor> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _dateTimeProvider = dateTimeProvider;
+        _logger = logger;
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
+
+        while (await timer.WaitForNextTickAsync(stoppingToken))
+        {
+            try
+            {
+                await ProcessTokenRefreshAsync(stoppingToken);
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during {Processor} processing", nameof(TokenRefreshProcessor));
+                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
+            }
+        }
+    }
+
+    internal async Task ProcessTokenRefreshAsync(CancellationToken ct)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
+        var oauthManager = scope.ServiceProvider.GetRequiredService<IOAuthManager>();
+        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
+        var now = _dateTimeProvider.UtcNow;
+
+        var twitterThreshold = now.Add(TwitterRefreshThreshold);
+        var longLivedThreshold = now.Add(LongLivedRefreshThreshold);
+
+        var platformsNeedingRefresh = await db.Platforms
+            .Where(p => p.IsConnected && p.TokenExpiresAt != null && p.Type != PlatformType.YouTube)
+            .Where(p =>
+                (p.Type == PlatformType.TwitterX && p.TokenExpiresAt < twitterThreshold) ||
+                (p.Type != PlatformType.TwitterX && p.TokenExpiresAt < longLivedThreshold))
+            .ToListAsync(ct);
+
+        foreach (var platform in platformsNeedingRefresh)
+        {
+            if (platform.Type == PlatformType.Instagram)
+            {
+                var daysUntilExpiry = (platform.TokenExpiresAt!.Value - now).TotalDays;
+                if (daysUntilExpiry < 3)
+                    _logger.LogError("Instagram token for {Platform} expires in {Days:F1} days — re-authentication required if refresh fails",
+                        platform.DisplayName, daysUntilExpiry);
+                else if (daysUntilExpiry < 14)
+                    _logger.LogWarning("Instagram token for {Platform} expires in {Days:F1} days",
+                        platform.DisplayName, daysUntilExpiry);
+            }
+
+            var result = await oauthManager.RefreshTokenAsync(platform.Type, ct);
+
+            if (!result.IsSuccess)
+            {
+                _logger.LogWarning("Failed to refresh token for {Platform}: {Errors}",
+                    platform.Type, string.Join(", ", result.Errors));
+
+                platform.IsConnected = false;
+                await db.SaveChangesAsync(ct);
+
+                await notifications.SendAsync(
+                    NotificationType.PlatformDisconnected,
+                    $"{platform.DisplayName} disconnected",
+                    $"Token refresh failed for {platform.DisplayName}. Please reconnect.",
+                    ct: ct);
+            }
+        }
+
+        // Clean up expired OAuthState entries
+        var expiredStates = await db.OAuthStates
+            .Where(o => o.ExpiresAt < now.AddHours(-1))
+            .ToListAsync(ct);
+
+        if (expiredStates.Count > 0)
+        {
+            foreach (var state in expiredStates)
+            {
+                db.OAuthStates.Remove(state);
+            }
+            await db.SaveChangesAsync(ct);
+            _logger.LogInformation("Cleaned up {Count} expired OAuth state entries", expiredStates.Count);
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs
index 2970f8c..e4eb25e 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs
@@ -64,6 +64,13 @@ public abstract class PlatformAdapterBase : ISocialPlatform
     public abstract Task<Result<ContentValidation>> ValidateContentAsync(
         PlatformContent content, CancellationToken ct);
 
+    public virtual Task<Result<PlatformPublishStatusCheck>> CheckPublishStatusAsync(
+        string platformPostId, CancellationToken ct)
+    {
+        return Task.FromResult(Result.Success(
+            new PlatformPublishStatusCheck(PlatformPublishStatus.Published, null, null)));
+    }
+
     protected abstract Task<Result<PublishResult>> ExecutePublishAsync(
         string accessToken, PlatformContent content, CancellationToken ct);
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PlatformHealthMonitorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PlatformHealthMonitorTests.cs
new file mode 100644
index 0000000..6440a86
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PlatformHealthMonitorTests.cs
@@ -0,0 +1,155 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class PlatformHealthMonitorTests
+{
+    private readonly Mock<IApplicationDbContext> _db = new();
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<INotificationService> _notifications = new();
+    private readonly Mock<IDateTimeProvider> _dateTime = new();
+    private readonly Mock<ILogger<PlatformHealthMonitor>> _logger = new();
+    private readonly Mock<ISocialPlatform> _twitterAdapter = new();
+    private readonly Mock<ISocialPlatform> _linkedInAdapter = new();
+    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
+
+    public PlatformHealthMonitorTests()
+    {
+        _dateTime.Setup(d => d.UtcNow).Returns(_now);
+        _twitterAdapter.Setup(a => a.Type).Returns(PlatformType.TwitterX);
+        _linkedInAdapter.Setup(a => a.Type).Returns(PlatformType.LinkedIn);
+    }
+
+    [Fact]
+    public async Task CallsGetProfileAsync_ForEachConnectedPlatform()
+    {
+        var platforms = new List<Platform>
+        {
+            CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]),
+            CreatePlatform(PlatformType.LinkedIn, ["w_member_social", "r_liteprofile"]),
+        };
+        SetupPlatforms(platforms);
+        SetupProfileSuccess(_twitterAdapter);
+        SetupProfileSuccess(_linkedInAdapter);
+
+        var processor = CreateProcessor();
+        await processor.CheckPlatformHealthAsync(CancellationToken.None);
+
+        _twitterAdapter.Verify(a => a.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
+        _linkedInAdapter.Verify(a => a.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task UpdatesLastSyncAt_OnSuccess()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]);
+        SetupPlatforms([platform]);
+        SetupProfileSuccess(_twitterAdapter);
+
+        var processor = CreateProcessor();
+        await processor.CheckPlatformHealthAsync(CancellationToken.None);
+
+        Assert.Equal(_now, platform.LastSyncAt);
+    }
+
+    [Fact]
+    public async Task WarnsOnMissingScopes()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read"]); // missing tweet.write, users.read, offline.access
+        SetupPlatforms([platform]);
+        SetupProfileSuccess(_twitterAdapter);
+
+        var processor = CreateProcessor();
+        await processor.CheckPlatformHealthAsync(CancellationToken.None);
+
+        _notifications.Verify(n => n.SendAsync(
+            NotificationType.PlatformScopeMismatch,
+            It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task AttemptsTokenRefresh_OnAuthFailure()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]);
+        SetupPlatforms([platform]);
+        _twitterAdapter.Setup(a => a.GetProfileAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<PlatformProfile>(ErrorCode.Unauthorized, "401 unauthorized"));
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddHours(2), null)));
+
+        var processor = CreateProcessor();
+        await processor.CheckPlatformHealthAsync(CancellationToken.None);
+
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task LogsWarning_OnNonAuthError_WithoutDisconnecting()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]);
+        SetupPlatforms([platform]);
+        _twitterAdapter.Setup(a => a.GetProfileAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<PlatformProfile>(ErrorCode.InternalError, "500 server error"));
+
+        var processor = CreateProcessor();
+        await processor.CheckPlatformHealthAsync(CancellationToken.None);
+
+        Assert.True(platform.IsConnected);
+        _oauthManager.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformType>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    private PlatformHealthMonitor CreateProcessor()
+    {
+        var scopeFactory = CreateScopeFactory();
+        return new PlatformHealthMonitor(scopeFactory, _dateTime.Object, _logger.Object);
+    }
+
+    private IServiceScopeFactory CreateScopeFactory()
+    {
+        var adapters = new[] { _twitterAdapter.Object, _linkedInAdapter.Object };
+        var serviceProvider = new Mock<IServiceProvider>();
+        serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext))).Returns(_db.Object);
+        serviceProvider.Setup(sp => sp.GetService(typeof(IOAuthManager))).Returns(_oauthManager.Object);
+        serviceProvider.Setup(sp => sp.GetService(typeof(INotificationService))).Returns(_notifications.Object);
+        serviceProvider.Setup(sp => sp.GetService(typeof(IEnumerable<ISocialPlatform>))).Returns(adapters);
+
+        var scope = new Mock<IServiceScope>();
+        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
+
+        var factory = new Mock<IServiceScopeFactory>();
+        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
+        return factory.Object;
+    }
+
+    private static Platform CreatePlatform(PlatformType type, string[] scopes)
+    {
+        return new Platform
+        {
+            Type = type,
+            IsConnected = true,
+            DisplayName = type.ToString(),
+            GrantedScopes = scopes,
+        };
+    }
+
+    private void SetupPlatforms(List<Platform> platforms)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
+        _db.Setup(d => d.Platforms).Returns(mockSet.Object);
+    }
+
+    private static void SetupProfileSuccess(Mock<ISocialPlatform> adapter)
+    {
+        adapter.Setup(a => a.GetProfileAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PlatformProfile("user-1", "Test User", null, 100)));
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PublishCompletionPollerTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PublishCompletionPollerTests.cs
new file mode 100644
index 0000000..0bd17ae
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PublishCompletionPollerTests.cs
@@ -0,0 +1,125 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class PublishCompletionPollerTests
+{
+    private readonly Mock<IApplicationDbContext> _db = new();
+    private readonly Mock<IDateTimeProvider> _dateTime = new();
+    private readonly Mock<ILogger<PublishCompletionPoller>> _logger = new();
+    private readonly Mock<ISocialPlatform> _instagramAdapter = new();
+    private readonly Mock<ISocialPlatform> _youtubeAdapter = new();
+    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
+
+    public PublishCompletionPollerTests()
+    {
+        _dateTime.Setup(d => d.UtcNow).Returns(_now);
+        _instagramAdapter.Setup(a => a.Type).Returns(PlatformType.Instagram);
+        _youtubeAdapter.Setup(a => a.Type).Returns(PlatformType.YouTube);
+    }
+
+    [Fact]
+    public async Task LeavesProcessing_WhenStillInProgress()
+    {
+        var entry = CreateProcessingEntry(PlatformType.Instagram, _now.AddMinutes(-5));
+        SetupEntries([entry]);
+        _instagramAdapter.Setup(a => a.CheckPublishStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PlatformPublishStatusCheck(PlatformPublishStatus.Processing, null, null)));
+
+        var poller = CreatePoller();
+        await poller.PollProcessingEntriesAsync(CancellationToken.None);
+
+        Assert.Equal(PlatformPublishStatus.Processing, entry.Status);
+    }
+
+    [Fact]
+    public async Task UpdatesToPublished_WhenFinished()
+    {
+        var entry = CreateProcessingEntry(PlatformType.Instagram, _now.AddMinutes(-5));
+        SetupEntries([entry]);
+        _instagramAdapter.Setup(a => a.CheckPublishStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PlatformPublishStatusCheck(
+                PlatformPublishStatus.Published, "https://instagram.com/p/abc123", null)));
+
+        var poller = CreatePoller();
+        await poller.PollProcessingEntriesAsync(CancellationToken.None);
+
+        Assert.Equal(PlatformPublishStatus.Published, entry.Status);
+        Assert.Equal("https://instagram.com/p/abc123", entry.PostUrl);
+    }
+
+    [Fact]
+    public async Task UpdatesYouTubeToPublished_WhenComplete()
+    {
+        var entry = CreateProcessingEntry(PlatformType.YouTube, _now.AddMinutes(-10));
+        SetupEntries([entry]);
+        _youtubeAdapter.Setup(a => a.CheckPublishStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PlatformPublishStatusCheck(
+                PlatformPublishStatus.Published, "https://youtube.com/watch?v=abc", null)));
+
+        var poller = CreatePoller();
+        await poller.PollProcessingEntriesAsync(CancellationToken.None);
+
+        Assert.Equal(PlatformPublishStatus.Published, entry.Status);
+    }
+
+    [Fact]
+    public async Task MarksFailed_After30MinuteTimeout()
+    {
+        var entry = CreateProcessingEntry(PlatformType.Instagram, _now.AddMinutes(-31));
+        SetupEntries([entry]);
+
+        var poller = CreatePoller();
+        await poller.PollProcessingEntriesAsync(CancellationToken.None);
+
+        Assert.Equal(PlatformPublishStatus.Failed, entry.Status);
+        Assert.Contains("timed out", entry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
+    }
+
+    private PublishCompletionPoller CreatePoller()
+    {
+        var scopeFactory = CreateScopeFactory();
+        return new PublishCompletionPoller(scopeFactory, _dateTime.Object, _logger.Object);
+    }
+
+    private IServiceScopeFactory CreateScopeFactory()
+    {
+        var adapters = new[] { _instagramAdapter.Object, _youtubeAdapter.Object };
+        var serviceProvider = new Mock<IServiceProvider>();
+        serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext))).Returns(_db.Object);
+        serviceProvider.Setup(sp => sp.GetService(typeof(IEnumerable<ISocialPlatform>))).Returns(adapters);
+
+        var scope = new Mock<IServiceScope>();
+        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
+
+        var factory = new Mock<IServiceScopeFactory>();
+        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
+        return factory.Object;
+    }
+
+    private ContentPlatformStatus CreateProcessingEntry(PlatformType platform, DateTimeOffset createdAt)
+    {
+        return new ContentPlatformStatus
+        {
+            ContentId = Guid.NewGuid(),
+            Platform = platform,
+            Status = PlatformPublishStatus.Processing,
+            PlatformPostId = "container-123",
+            CreatedAt = createdAt,
+        };
+    }
+
+    private void SetupEntries(List<ContentPlatformStatus> entries)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(entries);
+        _db.Setup(d => d.ContentPlatformStatuses).Returns(mockSet.Object);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TokenRefreshProcessorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TokenRefreshProcessorTests.cs
new file mode 100644
index 0000000..1bc7a7e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TokenRefreshProcessorTests.cs
@@ -0,0 +1,177 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+using MediatR;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;
+
+public class TokenRefreshProcessorTests
+{
+    private readonly Mock<IApplicationDbContext> _db = new();
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<INotificationService> _notifications = new();
+    private readonly Mock<IDateTimeProvider> _dateTime = new();
+    private readonly Mock<ILogger<TokenRefreshProcessor>> _logger = new();
+    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
+
+    public TokenRefreshProcessorTests()
+    {
+        _dateTime.Setup(d => d.UtcNow).Returns(_now);
+        SetupOAuthStates([]);
+    }
+
+    [Fact]
+    public async Task RefreshesTwitterTokens_WhenExpiryWithin30Min()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(20));
+        SetupPlatforms([platform]);
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddHours(2), null)));
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task RefreshesLinkedInTokens_WhenExpiryWithin10Days()
+    {
+        var platform = CreatePlatform(PlatformType.LinkedIn, _now.AddDays(8));
+        SetupPlatforms([platform]);
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.LinkedIn, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddDays(60), null)));
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.LinkedIn, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task SkipsYouTube_NoScheduledRefresh()
+    {
+        var platform = CreatePlatform(PlatformType.YouTube, _now.AddMinutes(20));
+        SetupPlatforms([platform]);
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.YouTube, It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task MarksDisconnected_OnRefreshFailure()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(10));
+        SetupPlatforms([platform]);
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<OAuthTokens>(ErrorCode.Unauthorized, "Token revoked"));
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        Assert.False(platform.IsConnected);
+        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
+    }
+
+    [Fact]
+    public async Task NotifiesUser_OnRefreshFailure()
+    {
+        var platform = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(10));
+        SetupPlatforms([platform]);
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<OAuthTokens>(ErrorCode.Unauthorized, "Token revoked"));
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        _notifications.Verify(n => n.SendAsync(
+            NotificationType.PlatformDisconnected,
+            It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task OnlyRefreshesWithinThreshold()
+    {
+        var twitter = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(10)); // within 30min threshold
+        var linkedin = CreatePlatform(PlatformType.LinkedIn, _now.AddDays(30)); // outside 10day threshold
+        SetupPlatforms([twitter, linkedin]);
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddHours(2), null)));
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.LinkedIn, It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task CleansUpExpiredOAuthStates()
+    {
+        SetupPlatforms([]);
+        var expiredStates = new List<OAuthState>
+        {
+            new() { State = "old", Platform = PlatformType.TwitterX, CreatedAt = _now.AddHours(-3), ExpiresAt = _now.AddHours(-2) }
+        };
+        SetupOAuthStates(expiredStates);
+
+        var processor = CreateProcessor();
+        await processor.ProcessTokenRefreshAsync(CancellationToken.None);
+
+        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
+    }
+
+    private TokenRefreshProcessor CreateProcessor()
+    {
+        var scopeFactory = CreateScopeFactory();
+        return new TokenRefreshProcessor(scopeFactory, _dateTime.Object, _logger.Object);
+    }
+
+    private IServiceScopeFactory CreateScopeFactory()
+    {
+        var serviceProvider = new Mock<IServiceProvider>();
+        serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext))).Returns(_db.Object);
+        serviceProvider.Setup(sp => sp.GetService(typeof(IOAuthManager))).Returns(_oauthManager.Object);
+        serviceProvider.Setup(sp => sp.GetService(typeof(INotificationService))).Returns(_notifications.Object);
+
+        var scope = new Mock<IServiceScope>();
+        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
+
+        var factory = new Mock<IServiceScopeFactory>();
+        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
+        return factory.Object;
+    }
+
+    private static Platform CreatePlatform(PlatformType type, DateTimeOffset? tokenExpiresAt)
+    {
+        return new Platform
+        {
+            Type = type,
+            IsConnected = true,
+            DisplayName = type.ToString(),
+            TokenExpiresAt = tokenExpiresAt,
+        };
+    }
+
+    private void SetupPlatforms(List<Platform> platforms)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
+        _db.Setup(d => d.Platforms).Returns(mockSet.Object);
+    }
+
+    private void SetupOAuthStates(List<OAuthState> states)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(states);
+        _db.Setup(d => d.OAuthStates).Returns(mockSet.Object);
+    }
+}
