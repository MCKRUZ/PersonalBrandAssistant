using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class PlatformHealthMonitor : BackgroundService
{
    private static readonly FrozenDictionary<PlatformType, string[]> RequiredScopes =
        new Dictionary<PlatformType, string[]>
        {
            [PlatformType.TwitterX] = ["tweet.read", "tweet.write", "users.read", "offline.access"],
            [PlatformType.LinkedIn] = ["w_member_social", "r_liteprofile"],
            [PlatformType.Instagram] = ["instagram_basic", "instagram_content_publish", "pages_show_list"],
            [PlatformType.YouTube] = ["youtube", "youtube.upload"],
        }.ToFrozenDictionary();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<PlatformHealthMonitor> _logger;

    public PlatformHealthMonitor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<PlatformHealthMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckPlatformHealthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during {Processor} processing", nameof(PlatformHealthMonitor));
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task CheckPlatformHealthAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var adapters = scope.ServiceProvider.GetRequiredService<IEnumerable<ISocialPlatform>>();
        var oauthManager = scope.ServiceProvider.GetRequiredService<IOAuthManager>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = _dateTimeProvider.UtcNow;

        var connectedPlatforms = await db.Platforms
            .Where(p => p.IsConnected)
            .ToListAsync(ct);

        foreach (var platform in connectedPlatforms)
        {
            var adapter = adapters.FirstOrDefault(a => a.Type == platform.Type);
            if (adapter is null)
            {
                _logger.LogWarning("No adapter found for connected platform {Platform}", platform.Type);
                continue;
            }

            var profileResult = await adapter.GetProfileAsync(ct);

            if (profileResult.IsSuccess)
            {
                platform.LastSyncAt = now;
            }
            else
            {
                var isAuthError = profileResult.ErrorCode == ErrorCode.Unauthorized ||
                                  profileResult.Errors.Any(e => e.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                                                                 e.Contains("401", StringComparison.OrdinalIgnoreCase));

                if (isAuthError)
                {
                    _logger.LogWarning("Auth failure for {Platform}, attempting token refresh", platform.Type);
                    var refreshResult = await oauthManager.RefreshTokenAsync(platform.Type, ct);
                    if (!refreshResult.IsSuccess)
                    {
                        _logger.LogWarning("Token refresh failed for {Platform}: {Errors}",
                            platform.Type, string.Join(", ", refreshResult.Errors));
                    }
                }
                else
                {
                    _logger.LogWarning("Health check failed for {Platform}: {Errors}",
                        platform.Type, string.Join(", ", profileResult.Errors));
                }
            }

            // Check scope integrity
            if (RequiredScopes.TryGetValue(platform.Type, out var required) && platform.GrantedScopes is not null)
            {
                var missing = required.Except(platform.GrantedScopes).ToArray();
                if (missing.Length > 0)
                {
                    _logger.LogWarning("{Platform} is missing required scopes: {Scopes}",
                        platform.Type, string.Join(", ", missing));

                    await notifications.SendAsync(
                        NotificationType.PlatformScopeMismatch,
                        $"{platform.DisplayName} scope mismatch",
                        $"Missing required scopes: {string.Join(", ", missing)}. Please reconnect to grant permissions.",
                        ct: ct);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
