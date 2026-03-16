using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class RepurposeOnPublishProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<RepurposeOnPublishProcessor> _logger;

    // Fixed lookback window instead of volatile watermark.
    // Safe because RepurposingService is idempotent (returns Conflict for duplicates).
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(2);

    public RepurposeOnPublishProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<RepurposeOnPublishProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during repurpose-on-publish processing");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var repurposingService = scope.ServiceProvider.GetRequiredService<IRepurposingService>();
        var now = _dateTimeProvider.UtcNow;

        var autonomyConfig = await context.AutonomyConfigurations.FirstOrDefaultAsync(ct)
                             ?? AutonomyConfiguration.CreateDefault();

        var lookbackStart = now - LookbackWindow;
        var recentlyPublished = await context.Contents
            .Where(c => c.Status == ContentStatus.Published && c.PublishedAt >= lookbackStart)
            .ToListAsync(ct);

        foreach (var content in recentlyPublished)
        {
            try
            {
                var level = autonomyConfig.ResolveLevel(content.ContentType, null);

                if (level == AutonomyLevel.Manual)
                {
                    _logger.LogDebug("Skipping repurpose for {ContentId} — Manual autonomy", content.Id);
                    continue;
                }

                var targetPlatforms = content.TargetPlatforms.Length > 0
                    ? content.TargetPlatforms
                    : await GetSeriesPlatforms(context, content.Id, ct);

                if (targetPlatforms.Length == 0)
                {
                    _logger.LogDebug("No target platforms for content {ContentId}, skipping repurpose", content.Id);
                    continue;
                }

                var result = await repurposingService.RepurposeAsync(content.Id, targetPlatforms, ct);

                if (result.IsSuccess)
                {
                    _logger.LogInformation(
                        "Repurposed content {ContentId} to {Count} platform(s)",
                        content.Id, result.Value!.Count);
                }
                else
                {
                    _logger.LogWarning(
                        "Repurpose failed for {ContentId}: {Errors}",
                        content.Id, string.Join(", ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error repurposing content {ContentId}", content.Id);
            }
        }

    }

    private static async Task<PlatformType[]> GetSeriesPlatforms(
        IApplicationDbContext context, Guid contentId, CancellationToken ct)
    {
        var platforms = await context.CalendarSlots
            .Where(s => s.ContentId == contentId && s.ContentSeriesId != null)
            .Join(context.ContentSeries,
                slot => slot.ContentSeriesId,
                series => series.Id,
                (_, series) => series.TargetPlatforms)
            .FirstOrDefaultAsync(ct);

        return platforms ?? [];
    }
}
