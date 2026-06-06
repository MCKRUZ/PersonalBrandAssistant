using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

/// <summary>
/// Pushes an instant alert when an idea scores at or above the threshold. Dedupes via
/// <c>Idea.AlertedAt</c> and enforces a shared daily cap so a busy news day cannot spam.
/// </summary>
public sealed class HighScoreAlertService(
    IServiceScopeFactory scopeFactory,
    IOptions<DigestDeliveryOptions> options,
    ILogger<HighScoreAlertService> logger) : BackgroundService
{
    private readonly AlertDeliveryOptions _options = options.Value.Alerts;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "High-score alert sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.SweepIntervalMinutes), stoppingToken);
        }
    }

    internal async Task SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!_options.Enabled) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var usedToday = await db.Ideas.CountAsync(i => i.AlertedAt >= startOfDay, ct);
        var remaining = _options.MaxPerDay - usedToday;
        if (remaining <= 0) return;

        var candidates = await db.Ideas
            .Where(i => i.Score >= _options.ScoreThreshold && i.AlertedAt == null && i.DuplicateOfId == null)
            .OrderByDescending(i => i.Score)
            .ThenByDescending(i => i.ScoredAt)
            .Take(remaining)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDeliveryDispatcher>();

        foreach (var idea in candidates)
        {
            var notification = new DeliveryNotification(
                DeliveryKind.Alert,
                idea.Title,
                idea.ScoreReason ?? idea.Summary ?? string.Empty,
                new[]
                {
                    new DeliveryItem(null, idea.Score ?? 0, idea.Title, idea.Summary ?? string.Empty, idea.Url)
                });

            await dispatcher.DispatchAsync(notification, ct);
            idea.AlertedAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Dispatched {Count} high-score alerts", candidates.Count);
    }
}
