using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Security;

namespace PBA.Infrastructure.Services.Analytics;

// Daily write-side heartbeat: walks every active Analytics-purpose credential for enabled platforms, ensures
// a fresh token, polls the platform's IChannelAnalyticsService, and persists cumulative ChannelMetricSnapshot
// rows idempotently. Trends are derived at read time (section 06); this only produces the raw cumulative rows.
public sealed class ChannelMetricPollingService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ChannelAnalyticsOptions> optionsMonitor,
    ILogger<ChannelMetricPollingService> logger) : BackgroundService
{
    private static readonly Platform[] AnalyticsPlatforms = [Platform.YouTube, Platform.Instagram, Platform.TikTok];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ChannelMetricPollingService scheduled daily at {Time} (host TZ {Tz})",
            optionsMonitor.CurrentValue.RunAtLocalTime, TimeZoneInfo.Local.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var runAt = TimeOnly.ParseExact(
                    optionsMonitor.CurrentValue.RunAtLocalTime, "HH:mm", CultureInfo.InvariantCulture);
                if (TimeOnly.FromDateTime(now.DateTime) >= runAt)
                    await PollAllAsync(now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Channel metric polling failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    internal async Task PollAllAsync(DateTimeOffset now, CancellationToken ct)
    {
        var options = optionsMonitor.CurrentValue;
        // Host-LOCAL date — lines up with RunAtLocalTime and the read-side trend math.
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var encryptor = sp.GetRequiredService<ITokenEncryptor>();

        var enabled = AnalyticsPlatforms.Where(p => IsEnabled(p, options)).ToHashSet();

        var candidates = await db.PlatformCredentials
            .Where(c => c.IsActive && c.Purpose == CredentialPurpose.Analytics)
            .ToListAsync(ct);

        foreach (var credential in candidates.Where(c => enabled.Contains(c.Platform)))
        {
            // Never throw out of the loop — one platform's failure must not stop the others.
            try
            {
                await PollCredentialAsync(sp, db, encryptor, credential, today, options.RecentVideoCount, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Channel metric poll failed for {Platform}; continuing", credential.Platform);
            }
        }
    }

    private async Task PollCredentialAsync(
        IServiceProvider sp, ApplicationDbContext db, ITokenEncryptor encryptor,
        PlatformCredential credential, DateOnly today, int recentVideoCount, DateTimeOffset now, CancellationToken ct)
    {
        var platform = credential.Platform;

        // a. Idempotency guard: the Account row is written LAST, so its presence means today is complete.
        if (await db.ChannelMetricSnapshots.AnyAsync(
                s => s.Platform == platform && s.SnapshotDate == today && s.Scope == SnapshotScope.Account, ct))
            return;

        var provider = sp.GetRequiredKeyedService<IOAuthProvider>(platform);

        // b. Ensure a valid token. Call the provider DIRECTLY (not IOAuthService.RefreshTokenAsync): the
        //    coordinator drops the RefreshFailureReason and would wrongly deactivate a no-refresh-token
        //    provider (Instagram). The poller owns revoked-only deactivation and persists the tokens itself.
        if (provider.NeedsRefresh(credential, now))
        {
            var refresh = await provider.RefreshAsync(credential, ct);
            if (!refresh.IsSuccess)
            {
                if (refresh.FailureReason == RefreshFailureReason.Revoked)
                {
                    credential.IsActive = false;
                    credential.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);
                    logger.LogWarning("Analytics credential for {Platform} revoked; reconnect required", platform);
                }
                else
                {
                    // Transient — leave IsActive true and retry tomorrow. Never deactivate on Transient.
                    logger.LogWarning("Transient refresh failure for {Platform}; skipping this run", platform);
                }
                return;
            }

            var tokens = refresh.Tokens!;
            credential.EncryptedAccessToken = encryptor.Encrypt(tokens.AccessToken);
            credential.AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn);
            if (tokens.RefreshToken is not null)   // TikTok rotates the refresh token
                credential.EncryptedRefreshToken = encryptor.Encrypt(tokens.RefreshToken);
            credential.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        // c. Poll.
        var service = sp.GetRequiredKeyedService<IChannelAnalyticsService>(platform);
        var result = await service.PollAsync(credential, recentVideoCount, ct);
        if (!result.IsSuccess)
        {
            logger.LogWarning("Poll failed for {Platform}: {Errors}", platform, string.Join("; ", result.Errors));
            return;   // do not write partial rows
        }

        var poll = result.Value!;
        // d. Dedup by VideoId (pagination overlap can repeat one), THEN cap. A duplicate VideoId would
        //    otherwise violate the (Platform, SnapshotDate, Scope, VideoId) unique index on Postgres, fail
        //    the whole write, and leave the platform permanently un-snapshotted.
        var videos = poll.RecentVideos
            .DistinctBy(v => v.VideoId)
            .Take(recentVideoCount)
            .ToList();

        // e. Write video rows FIRST, then the Account row LAST (completion sentinel). No explicit transaction:
        //    the sentinel + idempotent re-poll self-heals a crash between the two writes (portable to InMemory).
        //    Clear any partial-write leftover video rows first so a re-poll doesn't duplicate them.
        var staleVideos = await db.ChannelMetricSnapshots
            .Where(s => s.Platform == platform && s.SnapshotDate == today && s.Scope == SnapshotScope.Video)
            .ToListAsync(ct);
        if (staleVideos.Count > 0)
        {
            db.ChannelMetricSnapshots.RemoveRange(staleVideos);
            await db.SaveChangesAsync(ct);
        }

        db.ChannelMetricSnapshots.AddRange(videos.Select(v => new ChannelMetricSnapshot
        {
            Platform = platform,
            SnapshotDate = today,
            Scope = SnapshotScope.Video,
            VideoId = v.VideoId,
            VideoTitle = v.Title,
            Metrics = v.Metrics,
            CapturedAt = now
        }));
        await db.SaveChangesAsync(ct);

        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = platform,
            SnapshotDate = today,
            Scope = SnapshotScope.Account,
            VideoId = string.Empty,
            VideoTitle = null,
            Metrics = poll.Account.Metrics,
            CapturedAt = now
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Captured {Platform} snapshot for {Date}: {VideoCount} video rows", platform, today, videos.Count);
    }

    private static bool IsEnabled(Platform platform, ChannelAnalyticsOptions options) => platform switch
    {
        Platform.YouTube => options.YouTubeEnabled,
        Platform.Instagram => options.InstagramEnabled,
        Platform.TikTok => options.TikTokEnabled,
        _ => false
    };
}
