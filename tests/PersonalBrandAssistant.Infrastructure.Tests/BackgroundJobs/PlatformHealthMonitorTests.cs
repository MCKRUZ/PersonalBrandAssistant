using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class PlatformHealthMonitorTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<ILogger<PlatformHealthMonitor>> _logger = new();
    private readonly Mock<ISocialPlatform> _twitterAdapter = new();
    private readonly Mock<ISocialPlatform> _linkedInAdapter = new();
    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public PlatformHealthMonitorTests()
    {
        _dateTime.Setup(d => d.UtcNow).Returns(_now);
        _twitterAdapter.Setup(a => a.Type).Returns(PlatformType.TwitterX);
        _linkedInAdapter.Setup(a => a.Type).Returns(PlatformType.LinkedIn);
    }

    [Fact]
    public async Task CallsGetProfileAsync_ForEachConnectedPlatform()
    {
        var platforms = new List<Platform>
        {
            CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]),
            CreatePlatform(PlatformType.LinkedIn, ["w_member_social", "r_liteprofile"]),
        };
        SetupPlatforms(platforms);
        SetupProfileSuccess(_twitterAdapter);
        SetupProfileSuccess(_linkedInAdapter);

        var processor = CreateProcessor();
        await processor.CheckPlatformHealthAsync(CancellationToken.None);

        _twitterAdapter.Verify(a => a.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
        _linkedInAdapter.Verify(a => a.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatesLastSyncAt_OnSuccess()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]);
        SetupPlatforms([platform]);
        SetupProfileSuccess(_twitterAdapter);

        var processor = CreateProcessor();
        await processor.CheckPlatformHealthAsync(CancellationToken.None);

        Assert.Equal(_now, platform.LastSyncAt);
    }

    [Fact]
    public async Task WarnsOnMissingScopes()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read"]); // missing tweet.write, users.read, offline.access
        SetupPlatforms([platform]);
        SetupProfileSuccess(_twitterAdapter);

        var processor = CreateProcessor();
        await processor.CheckPlatformHealthAsync(CancellationToken.None);

        _notifications.Verify(n => n.SendAsync(
            NotificationType.PlatformScopeMismatch,
            It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttemptsTokenRefresh_OnAuthFailure()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]);
        SetupPlatforms([platform]);
        _twitterAdapter.Setup(a => a.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PlatformProfile>(ErrorCode.Unauthorized, "401 unauthorized"));
        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddHours(2), null)));

        var processor = CreateProcessor();
        await processor.CheckPlatformHealthAsync(CancellationToken.None);

        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogsWarning_OnNonAuthError_WithoutDisconnecting()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, ["tweet.read", "tweet.write", "users.read", "offline.access"]);
        SetupPlatforms([platform]);
        _twitterAdapter.Setup(a => a.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PlatformProfile>(ErrorCode.InternalError, "500 server error"));

        var processor = CreateProcessor();
        await processor.CheckPlatformHealthAsync(CancellationToken.None);

        Assert.True(platform.IsConnected);
        _oauthManager.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private PlatformHealthMonitor CreateProcessor()
    {
        var scopeFactory = CreateScopeFactory();
        return new PlatformHealthMonitor(scopeFactory, _dateTime.Object, _logger.Object);
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var adapters = new[] { _twitterAdapter.Object, _linkedInAdapter.Object };
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext))).Returns(_db.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(IOAuthManager))).Returns(_oauthManager.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(INotificationService))).Returns(_notifications.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(IEnumerable<ISocialPlatform>))).Returns(adapters);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static Platform CreatePlatform(PlatformType type, string[] scopes)
    {
        return new Platform
        {
            Type = type,
            IsConnected = true,
            DisplayName = type.ToString(),
            GrantedScopes = scopes,
        };
    }

    private void SetupPlatforms(List<Platform> platforms)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
        _db.Setup(d => d.Platforms).Returns(mockSet.Object);
    }

    private static void SetupProfileSuccess(Mock<ISocialPlatform> adapter)
    {
        adapter.Setup(a => a.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlatformProfile("user-1", "Test User", null, 100)));
    }
}
