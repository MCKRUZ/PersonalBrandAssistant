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

public class RssPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RssPollingOptions> _options;
    private readonly ILogger<RssPollingService> _logger;

    public RssPollingService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RssPollingOptions> options,
        ILogger<RssPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(_options.CurrentValue.PollIntervalMinutes);
            await Task.Delay(interval, stoppingToken);

            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RSS polling cycle failed");
            }
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var feedReader = scope.ServiceProvider.GetRequiredService<IRssFeedReader>();

        var sources = await dbContext.IdeaSources
            .Where(s => s.IsEnabled && s.Type == IdeaSourceType.RSS && s.FeedUrl != null && s.FeedUrl != "")
            .ToListAsync(ct);

        if (sources.Count == 0)
        {
            _logger.LogDebug("No enabled RSS sources to poll");
            return;
        }

        var existingKeys = await dbContext.Ideas
            .Where(i => i.DeduplicationKey != "")
            .Select(i => i.DeduplicationKey)
            .ToHashSetAsync(ct);

        foreach (var source in sources)
        {
            try
            {
                var entries = await feedReader.ReadFeedAsync(source.FeedUrl!, ct);

                var newCount = 0;
                foreach (var entry in entries)
                {
                    var dedupKey = DeduplicationKeyGenerator.Generate(entry.Url, entry.Title);
                    if (existingKeys.Contains(dedupKey))
                        continue;

                    dbContext.Ideas.Add(new Idea
                    {
                        Title = entry.Title,
                        Description = TruncateContent(entry.Description, 2000),
                        Url = entry.Url,
                        SourceName = source.Name,
                        IdeaSourceId = source.Id,
                        ThumbnailUrl = entry.ThumbnailUrl,
                        Category = source.Category,
                        Tags = [],
                        Status = IdeaStatus.New,
                        DetectedAt = entry.PublishedAt,
                        DeduplicationKey = dedupKey,
                    });

                    existingKeys.Add(dedupKey);
                    newCount++;
                }

                source.LastPolledAt = DateTimeOffset.UtcNow;
                source.LastSuccessAt = DateTimeOffset.UtcNow;
                source.ConsecutiveFailures = 0;
                source.LastError = null;

                if (newCount > 0)
                    _logger.LogInformation("Source {SourceName}: {Count} new ideas", source.Name, newCount);
            }
            catch (Exception ex)
            {
                source.ConsecutiveFailures++;
                source.LastError = ex.Message;
                source.LastPolledAt = DateTimeOffset.UtcNow;

                var maxFailures = _options.CurrentValue.MaxConsecutiveFailures;
                if (source.ConsecutiveFailures >= maxFailures)
                {
                    source.IsEnabled = false;
                    _logger.LogError("Source {SourceName} disabled after {Count} consecutive failures",
                        source.Name, source.ConsecutiveFailures);
                }
                else
                {
                    _logger.LogWarning(ex, "Source {SourceName} poll failed ({Count}/{Max})",
                        source.Name, source.ConsecutiveFailures, maxFailures);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static string? TruncateContent(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return content;
        return content.Length <= maxLength ? content : content[..maxLength];
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<HashSet<T>> ToHashSetAsync<T>(
        this IQueryable<T> source, CancellationToken ct = default)
    {
        var set = new HashSet<T>();
        await foreach (var item in source.AsAsyncEnumerable().WithCancellation(ct))
            set.Add(item);
        return set;
    }
}
