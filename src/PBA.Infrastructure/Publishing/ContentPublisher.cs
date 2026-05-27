using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

public sealed class ContentPublisher(
    IAppDbContext db,
    IServiceProvider serviceProvider,
    IContentTransformer transformer,
    ILogger<ContentPublisher> logger) : IContentPublisher
{
    public async Task PublishAsync(Guid contentId)
    {
        var content = await db.Contents.FindAsync(contentId);
        if (content is null)
        {
            logger.LogWarning("Content {ContentId} not found for scheduled publish", contentId);
            return;
        }

        if (content.Status != ContentStatus.Scheduled)
        {
            logger.LogWarning("Content {ContentId} is {Status}, skipping scheduled publish", contentId, content.Status);
            return;
        }

        await PublishAsync(contentId, targetPlatforms: null, CancellationToken.None);
    }

    public async Task<PublishResult> PublishAsync(
        Guid contentId,
        IReadOnlyList<Platform>? targetPlatforms,
        CancellationToken ct)
    {
        var content = await db.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            logger.LogWarning("Content {ContentId} not found for publish", contentId);
            return new PublishResult(false, null, []);
        }

        if (content.Status != ContentStatus.Scheduled && content.Status != ContentStatus.Approved)
        {
            logger.LogWarning("Content {ContentId} is {Status}, skipping publish", contentId, content.Status);
            return new PublishResult(false, null, []);
        }

        var platforms = DetermineTargetPlatforms(content, targetPlatforms);
        var primaryPlatform = content.PrimaryPlatform;

        var publishedPrimary = await db.ContentPlatformPublishes
            .AnyAsync(p => p.ContentId == contentId && p.Platform == primaryPlatform && p.Status == PublishStatus.Published, ct);

        PlatformPublishResult? primaryResult = null;
        string? primaryUrl = null;

        if (platforms.Contains(primaryPlatform) && !publishedPrimary)
        {
            primaryResult = await PublishToPlatformAsync(content, primaryPlatform, canonicalUrl: null, ct);

            db.ContentPlatformPublishes.Add(new ContentPlatformPublish
            {
                ContentId = contentId,
                Platform = primaryPlatform,
                Status = primaryResult.Success ? PublishStatus.Published : PublishStatus.Failed,
                PublishedUrl = primaryResult.PublishedUrl,
                PlatformPostId = primaryResult.PlatformPostId,
                ErrorMessage = primaryResult.ErrorMessage,
                PublishedAt = DateTimeOffset.UtcNow
            });

            if (!primaryResult.Success)
            {
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Failed to publish content {ContentId} to primary {Platform}: {Error}",
                    contentId, primaryPlatform, primaryResult.ErrorMessage);
                return new PublishResult(false, null, []);
            }

            primaryUrl = primaryResult.PublishedUrl;
        }
        else if (publishedPrimary)
        {
            var existingRecord = await db.ContentPlatformPublishes
                .FirstAsync(p => p.ContentId == contentId && p.Platform == primaryPlatform && p.Status == PublishStatus.Published, ct);
            primaryUrl = existingRecord.PublishedUrl;
        }

        if (content.Status != ContentStatus.Published)
        {
            var trigger = content.Status == ContentStatus.Scheduled
                ? ContentTrigger.Publish
                : ContentTrigger.PublishNow;
            var machine = ContentStateMachine.Create(content);
            await machine.FireAsync(trigger);
        }

        var secondaryPlatforms = platforms
            .Where(p => p != primaryPlatform)
            .ToList();

        var secondaryOutcomes = new List<PlatformPublishOutcome>();

        if (secondaryPlatforms.Count > 0)
        {
            var alreadyPublishedSet = (await db.ContentPlatformPublishes
                .Where(p => p.ContentId == contentId && p.Status == PublishStatus.Published)
                .Select(p => p.Platform)
                .ToListAsync(ct))
                .ToHashSet();

            var platformsToPublish = secondaryPlatforms
                .Where(p => !alreadyPublishedSet.Contains(p))
                .ToList();

            var publishTasks = platformsToPublish.Select(async platform =>
            {
                try
                {
                    var result = await PublishToPlatformAsync(content, platform, primaryUrl, ct);
                    return new PlatformPublishOutcome(platform, result.Success, result.PublishedUrl, result.ErrorMessage);
                }
                catch (Exception ex)
                {
                    return new PlatformPublishOutcome(platform, false, null, ex.Message);
                }
            });

            var outcomes = await Task.WhenAll(publishTasks);

            foreach (var outcome in outcomes)
            {
                secondaryOutcomes.Add(outcome);
                db.ContentPlatformPublishes.Add(new ContentPlatformPublish
                {
                    ContentId = contentId,
                    Platform = outcome.Platform,
                    Status = outcome.Success ? PublishStatus.Published : PublishStatus.Failed,
                    PublishedUrl = outcome.Url,
                    ErrorMessage = outcome.Error,
                    PublishedAt = DateTimeOffset.UtcNow,
                    RetryCount = 0
                });
            }

            foreach (var skipped in secondaryPlatforms.Where(p => alreadyPublishedSet.Contains(p)))
                secondaryOutcomes.Add(new PlatformPublishOutcome(skipped, true, null, null));
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Published content {ContentId} to {Platform} (primary) + {SecondaryCount} secondaries",
            contentId, primaryPlatform, secondaryOutcomes.Count);

        return new PublishResult(
            primaryResult?.Success ?? publishedPrimary,
            primaryUrl,
            secondaryOutcomes.AsReadOnly());
    }

    private async Task<PlatformPublishResult> PublishToPlatformAsync(
        Content content,
        Platform platform,
        string? canonicalUrl,
        CancellationToken ct)
    {
        var connector = serviceProvider.GetKeyedService<IPlatformConnector>(platform);
        if (connector is null)
        {
            logger.LogWarning("No connector registered for platform {Platform}", platform);
            return new PlatformPublishResult(false, null, null, $"No connector registered for {platform}");
        }

        var transformed = await transformer.TransformAsync(content, platform, ct);
        var request = new PlatformPublishRequest(
            Content: content,
            TransformedContent: transformed,
            Tags: content.Tags.AsReadOnly(),
            CanonicalUrl: canonicalUrl,
            Mode: PublishMode.Publish,
            ScheduledAt: content.ScheduledAt);

        return await connector.PublishAsync(request, ct);
    }

    private static IReadOnlyList<Platform> DetermineTargetPlatforms(Content content, IReadOnlyList<Platform>? explicitTargets)
    {
        if (explicitTargets is { Count: > 0 })
            return explicitTargets;

        if (content.TargetPlatforms is { Count: > 0 })
            return content.TargetPlatforms.AsReadOnly();

        return [content.PrimaryPlatform];
    }
}
