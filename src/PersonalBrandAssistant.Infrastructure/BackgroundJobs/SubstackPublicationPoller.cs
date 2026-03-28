using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

internal sealed class SubstackPublicationPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SubstackOptions _options;
    private readonly ILogger<SubstackPublicationPoller> _logger;
    private string? _lastETag;
    private DateTimeOffset? _lastModified;

    public SubstackPublicationPoller(
        IServiceScopeFactory scopeFactory,
        IOptions<SubstackOptions> options,
        ILogger<SubstackPublicationPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.PollingIntervalMinutes);
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "Substack publication poller started with {Interval}min interval",
            _options.PollingIntervalMinutes);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
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
                _logger.LogError(ex, "Substack publication poller tick failed");
            }
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var substackService = scope.ServiceProvider.GetRequiredService<ISubstackService>();
        var matcher = scope.ServiceProvider.GetRequiredService<ISubstackContentMatcher>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var feedResult = await substackService.FetchFeedEntriesAsync(_lastETag, _lastModified, ct);

        if (!feedResult.IsSuccess)
        {
            _logger.LogWarning("Feed fetch failed: {Error}", feedResult.Errors.FirstOrDefault());
            return;
        }

        var feed = feedResult.Value!;

        if (feed.NotModified)
        {
            _logger.LogDebug("Feed not modified since last poll");
            return;
        }

        _lastETag = feed.ETag;
        _lastModified = feed.LastModified;

        var confidenceThreshold = Enum.TryParse<MatchConfidence>(_options.MatchConfidenceThreshold, true, out var threshold)
            ? threshold
            : MatchConfidence.Medium;

        foreach (var entry in feed.Entries)
        {
            // Dedup: skip if we already have this RSS guid
            var alreadySeen = await db.SubstackDetections
                .AnyAsync(d => d.RssGuid == entry.Guid, ct);
            if (alreadySeen)
            {
                // Check for content edits via hash change
                var existing = await db.SubstackDetections
                    .FirstOrDefaultAsync(d => d.RssGuid == entry.Guid, ct);
                if (existing is not null && existing.ContentHash != entry.ContentHash)
                {
                    _logger.LogInformation(
                        "Content hash changed for RSS entry '{Title}' (guid={Guid})",
                        entry.Title, entry.Guid);
                    existing.ContentHash = entry.ContentHash;
                    await db.SaveChangesAsync(ct);
                }
                continue;
            }

            var matchResult = await matcher.MatchAsync(entry, ct);

            var detection = new SubstackDetection
            {
                ContentId = matchResult.ContentId,
                RssGuid = entry.Guid,
                Title = entry.Title,
                SubstackUrl = entry.Link,
                PublishedAt = entry.PublishedAt,
                DetectedAt = DateTimeOffset.UtcNow,
                Confidence = matchResult.Confidence,
                ContentHash = entry.ContentHash
            };
            db.SubstackDetections.Add(detection);

            if (matchResult.ContentId.HasValue && matchResult.Confidence <= confidenceThreshold)
            {
                // Update Content.SubstackPostUrl
                var content = await db.Contents
                    .FirstOrDefaultAsync(c => c.Id == matchResult.ContentId.Value, ct);
                if (content is not null)
                {
                    content.SubstackPostUrl = entry.Link;
                    _logger.LogInformation(
                        "Linked Substack post '{Title}' to Content {ContentId} (confidence={Confidence})",
                        entry.Title, content.Id, matchResult.Confidence);
                }

                // Create notification
                db.UserNotifications.Add(new UserNotification
                {
                    Type = "SubstackPublicationDetected",
                    Message = $"Substack post detected: \"{entry.Title}\" matched with {matchResult.Confidence} confidence. {matchResult.MatchReason}",
                    ContentId = matchResult.ContentId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            else if (!matchResult.ContentId.HasValue)
            {
                _logger.LogDebug(
                    "Unmatched Substack entry '{Title}' (guid={Guid})", entry.Title, entry.Guid);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
