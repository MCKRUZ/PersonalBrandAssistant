using Microsoft.EntityFrameworkCore;
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

public class TokenRefreshProcessorTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<ILogger<TokenRefreshProcessor>> _logger = new();
    private readonly DateTimeOffset _now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public TokenRefreshProcessorTests()
    {
        _dateTime.Setup(d => d.UtcNow).Returns(_now);
        SetupOAuthStates([]);
    }

    [Fact]
    public async Task RefreshesTwitterTokens_WhenExpiryWithin30Min()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(20));
        SetupPlatforms([platform]);
        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddHours(2), null)));

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshesLinkedInTokens_WhenExpiryWithin10Days()
    {
        var platform = CreatePlatform(PlatformType.LinkedIn, _now.AddDays(8));
        SetupPlatforms([platform]);
        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.LinkedIn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddDays(60), null)));

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.LinkedIn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsYouTube_NoScheduledRefresh()
    {
        var platform = CreatePlatform(PlatformType.YouTube, _now.AddMinutes(20));
        SetupPlatforms([platform]);

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.YouTube, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarksDisconnected_OnRefreshFailure()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(10));
        SetupPlatforms([platform]);
        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<OAuthTokens>(ErrorCode.Unauthorized, "Token revoked"));

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        Assert.False(platform.IsConnected);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task NotifiesUser_OnRefreshFailure()
    {
        var platform = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(10));
        SetupPlatforms([platform]);
        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<OAuthTokens>(ErrorCode.Unauthorized, "Token revoked"));

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        _notifications.Verify(n => n.SendAsync(
            NotificationType.PlatformDisconnected,
            It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnlyRefreshesWithinThreshold()
    {
        var twitter = CreatePlatform(PlatformType.TwitterX, _now.AddMinutes(10)); // within 30min threshold
        var linkedin = CreatePlatform(PlatformType.LinkedIn, _now.AddDays(30)); // outside 10day threshold
        SetupPlatforms([twitter, linkedin]);
        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new OAuthTokens("new", "refresh", _now.AddHours(2), null)));

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.LinkedIn, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CleansUpExpiredOAuthStates()
    {
        SetupPlatforms([]);
        var expiredStates = new List<OAuthState>
        {
            new() { State = "old", Platform = PlatformType.TwitterX, CreatedAt = _now.AddHours(-3), ExpiresAt = _now.AddHours(-2) }
        };
        SetupOAuthStates(expiredStates);

        var processor = CreateProcessor();
        await processor.ProcessTokenRefreshAsync(CancellationToken.None);

        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private TokenRefreshProcessor CreateProcessor()
    {
        var scopeFactory = CreateScopeFactory();
        return new TokenRefreshProcessor(scopeFactory, _dateTime.Object, _logger.Object);
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IApplicationDbContext))).Returns(_db.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(IOAuthManager))).Returns(_oauthManager.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(INotificationService))).Returns(_notifications.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static Platform CreatePlatform(PlatformType type, DateTimeOffset? tokenExpiresAt)
    {
        return new Platform
        {
            Type = type,
            IsConnected = true,
            DisplayName = type.ToString(),
            TokenExpiresAt = tokenExpiresAt,
        };
    }

    private void SetupPlatforms(List<Platform> platforms)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
        _db.Setup(d => d.Platforms).Returns(mockSet.Object);
    }

    private void SetupOAuthStates(List<OAuthState> states)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(states);
        _db.Setup(d => d.OAuthStates).Returns(mockSet.Object);
    }
}
