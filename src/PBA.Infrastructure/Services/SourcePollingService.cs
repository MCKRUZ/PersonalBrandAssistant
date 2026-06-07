using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services;

/// <summary>Polls every enabled idea source, dispatching to the scraper registered (keyed DI) for the
/// source's type, then dedups and creates Ideas. Generalizes the former RSS-only polling service.</summary>
public class SourcePollingService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<RssPollingOptions> options,
    ILogger<SourcePollingService> logger) : BackgroundService
{
    private const int LookbackHours = 48;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(options.CurrentValue.PollIntervalMinutes), stoppingToken);
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Source polling cycle failed");
            }
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sources = await db.IdeaSources.Where(s => s.IsEnabled).ToListAsync(ct);
        if (sources.Count == 0) return;

        var existingKeys = await db.Ideas
            .Where(i => i.DeduplicationKey != "")
            .Select(i => i.DeduplicationKey)
            .ToHashSetAsync(ct);

        var maxFailures = options.CurrentValue.MaxConsecutiveFailures;

        foreach (var source in sources)
        {
            var scraper = scope.ServiceProvider.GetKeyedService<ISourceScraper>(source.Type);
            if (scraper is null)
            {
                logger.LogWarning("No scraper registered for source type {Type} ({Name})", source.Type, source.Name);
                continue;
            }

            try
            {
                var since = source.LastSuccessAt ?? DateTimeOffset.UtcNow.AddHours(-LookbackHours);
                var items = await scraper.FetchAsync(source, since, ct);

                var newCount = 0;
                foreach (var item in items)
                {
                    var dedupKey = DeduplicationKeyGenerator.Generate(item.Url, item.Title);
                    if (!existingKeys.Add(dedupKey)) continue;

                    db.Ideas.Add(new Idea
                    {
                        Title = item.Title,
                        Description = Truncate(item.Description, 2000),
                        Url = item.Url,
                        SourceName = source.Name,
                        IdeaSourceId = source.Id,
                        ThumbnailUrl = item.ThumbnailUrl,
                        Category = source.Category,
                        Tags = [],
                        Status = IdeaStatus.New,
                        DetectedAt = item.PublishedAt,
                        DeduplicationKey = dedupKey,
                    });
                    newCount++;
                }

                source.LastPolledAt = DateTimeOffset.UtcNow;
                source.LastSuccessAt = DateTimeOffset.UtcNow;
                source.ConsecutiveFailures = 0;
                source.LastError = null;
                if (newCount > 0)
                    logger.LogInformation("Source {Name}: {Count} new ideas", source.Name, newCount);
            }
            catch (Exception ex)
            {
                source.ConsecutiveFailures++;
                source.LastError = ex.Message;
                source.LastPolledAt = DateTimeOffset.UtcNow;
                if (source.ConsecutiveFailures >= maxFailures)
                {
                    source.IsEnabled = false;
                    logger.LogError("Source {Name} disabled after {Count} consecutive failures",
                        source.Name, source.ConsecutiveFailures);
                }
                else
                {
                    logger.LogWarning(ex, "Source {Name} poll failed ({Count}/{Max})",
                        source.Name, source.ConsecutiveFailures, maxFailures);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? content, int maxLength) =>
        string.IsNullOrEmpty(content) || content.Length <= maxLength ? content : content[..maxLength];
}
