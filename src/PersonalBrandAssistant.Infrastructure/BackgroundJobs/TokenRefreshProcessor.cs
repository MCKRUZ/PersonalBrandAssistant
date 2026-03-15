using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class TokenRefreshProcessor : BackgroundService
{
    private static readonly TimeSpan TwitterRefreshThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LongLivedRefreshThreshold = TimeSpan.FromDays(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<TokenRefreshProcessor> _logger;

    public TokenRefreshProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<TokenRefreshProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessTokenRefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during {Processor} processing", nameof(TokenRefreshProcessor));
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessTokenRefreshAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var oauthManager = scope.ServiceProvider.GetRequiredService<IOAuthManager>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = _dateTimeProvider.UtcNow;

        var twitterThreshold = now.Add(TwitterRefreshThreshold);
        var longLivedThreshold = now.Add(LongLivedRefreshThreshold);

        var platformsNeedingRefresh = await db.Platforms
            .Where(p => p.IsConnected && p.TokenExpiresAt != null && p.Type != PlatformType.YouTube)
            .Where(p =>
                (p.Type == PlatformType.TwitterX && p.TokenExpiresAt < twitterThreshold) ||
                (p.Type != PlatformType.TwitterX && p.TokenExpiresAt < longLivedThreshold))
            .ToListAsync(ct);

        foreach (var platform in platformsNeedingRefresh)
        {
            if (platform.Type == PlatformType.Instagram)
            {
                var daysUntilExpiry = (platform.TokenExpiresAt!.Value - now).TotalDays;
                if (daysUntilExpiry < 3)
                {
                    _logger.LogError("Instagram token for {Platform} expires in {Days:F1} days — re-authentication required if refresh fails",
                        platform.DisplayName, daysUntilExpiry);
                    await notifications.SendAsync(
                        NotificationType.PlatformTokenExpiring,
                        $"{platform.DisplayName} token expiring",
                        $"Instagram token expires in {daysUntilExpiry:F0} days. Re-authenticate immediately.",
                        ct: ct);
                }
                else if (daysUntilExpiry < 14)
                {
                    _logger.LogWarning("Instagram token for {Platform} expires in {Days:F1} days",
                        platform.DisplayName, daysUntilExpiry);
                    await notifications.SendAsync(
                        NotificationType.PlatformTokenExpiring,
                        $"{platform.DisplayName} token expiring soon",
                        $"Instagram token expires in {daysUntilExpiry:F0} days.",
                        ct: ct);
                }
            }

            var result = await oauthManager.RefreshTokenAsync(platform.Type, ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Failed to refresh token for {Platform}: {Errors}",
                    platform.Type, string.Join(", ", result.Errors));

                platform.IsConnected = false;
                await db.SaveChangesAsync(ct);

                await notifications.SendAsync(
                    NotificationType.PlatformDisconnected,
                    $"{platform.DisplayName} disconnected",
                    $"Token refresh failed for {platform.DisplayName}. Please reconnect.",
                    ct: ct);
            }
        }

        // Clean up expired OAuthState entries
        var expiredStates = await db.OAuthStates
            .Where(o => o.ExpiresAt < now.AddHours(-1))
            .ToListAsync(ct);

        if (expiredStates.Count > 0)
        {
            foreach (var state in expiredStates)
            {
                db.OAuthStates.Remove(state);
            }
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} expired OAuth state entries", expiredStates.Count);
        }
    }
}
