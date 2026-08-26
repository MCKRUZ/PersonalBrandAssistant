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
    IMediaHost mediaHost,
    IPlatformCapabilityReader capabilities,
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

        await PublishAsync(contentId, targetPlatforms: null, media: null, CancellationToken.None);
    }

    public async Task<PublishResult> PublishAsync(
        Guid contentId,
        IReadOnlyList<Platform>? targetPlatforms,
        MediaAttachment? media,
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
            primaryResult = await PublishToPlatformAsync(content, primaryPlatform, canonicalUrl: null, media, ct);

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
                await ResetStagedCopyForRetryAsync(content, ct);
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
                    var result = await PublishToPlatformAsync(content, platform, primaryUrl, media, ct);
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

        if (content.Status == ContentStatus.Published)
            await ReleaseHeldMediaAsync(content, primaryPlatform, ct);
        else
            await ResetStagedCopyForRetryAsync(content, ct);


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
        MediaAttachment? media,
        CancellationToken ct)
    {
        var connector = serviceProvider.GetKeyedService<IPlatformConnector>(platform);
        if (connector is null)
        {
            logger.LogWarning("No connector registered for platform {Platform}", platform);
            return new PlatformPublishResult(false, null, null, $"No connector registered for {platform}");
        }

        var transformed = await transformer.TransformAsync(content, platform, ct);

        // A record PBA held has no bytes on THIS request — they arrived with the original one, days
        // ago, and have been sitting beside the record ever since. This is the moment they become a
        // public URL: connectors in these lanes only ever fetch media by link, and the link's short
        // lifecycle now only has to outlast the platform reading it rather than the whole wait.
        var staged = media is null
            ? content.StagedMediaUrl ?? await PublishHeldMediaAsync(content, ct)
            : null;

        // Whether the connector still needs to be told the go-live time depends on who is doing the
        // waiting, and only the connector knows that. Instagram cannot schedule, so PBA held the
        // post and this call IS the moment — passing a time would ask Meta for something it has no
        // concept of. YouTube can schedule but had to be handed the video early to fit its upload
        // quota, so the release time still has to travel with it or the clip goes public on upload.
        // Deciding from "was it staged?" instead conflated the two and only worked while Instagram
        // was the only platform PBA held anything for.
        var platformWaits = capabilities.SupportsScheduling(platform);

        var request = new PlatformPublishRequest(
            Content: content,
            TransformedContent: transformed,
            Tags: content.Tags.AsReadOnly(),
            CanonicalUrl: canonicalUrl,
            Mode: PublishMode.Publish,
            ScheduledAt: platformWaits ? content.ScheduledAt : null,
            Media: media,
            HostedMediaUrl: staged,
            CoverFrameOffsetMs: content.CoverFrameOffsetMs);

        return await connector.PublishAsync(request, ct);
    }

    /// <summary>
    /// Turns the bytes PBA has been holding into a public URL, at the moment of hand-over. Returns
    /// null when there is nothing held, which is the ordinary case for a post published immediately.
    /// </summary>
    private async Task<string?> PublishHeldMediaAsync(Content content, CancellationToken ct)
    {
        var held = await db.HeldMedia.FindAsync([content.Id], ct);
        if (held is null)
            return null;

        var hosted = await mediaHost.UploadAsync(held.Data, held.FileName, held.ContentType, ct);
        content.StagedMediaUrl = hosted.Url;
        content.StagedMediaKey = hosted.Key;
        return hosted.Url;
    }

    /// <summary>
    /// Drops the pointer to the public copy after a failed publish, so a retry makes a fresh one
    /// from the bytes PBA is still holding.
    ///
    /// The public copy is on a short lifecycle rule. A retry days later would otherwise reuse a URL
    /// whose object has since been reaped — the failure would stop being "the publish failed" and
    /// become "the publish succeeded and the video is a dead link", which is far worse and far
    /// quieter. The held bytes are the source of truth for as long as the post is unpublished, so
    /// re-promoting is always available and always correct. The abandoned object is left to the
    /// lifecycle rule, which is what it is for.
    /// </summary>
    private async Task ResetStagedCopyForRetryAsync(Content content, CancellationToken ct)
    {
        if (content.StagedMediaKey is null)
            return;

        if (await db.HeldMedia.FindAsync([content.Id], ct) is null)
            return;

        content.StagedMediaUrl = null;
        content.StagedMediaKey = null;
    }

    /// <summary>
    /// Cleans up after a successful publish. The two copies are released on different schedules, and
    /// conflating them is how a clip disappears before anyone reads it:
    ///
    /// The HELD bytes are always dropped now — the public copy exists and is what gets read, and a
    /// held clip is tens of megabytes sitting in the database for no further purpose.
    ///
    /// The PUBLIC copy is only removed for a platform that has already consumed it. Instagram and
    /// YouTube take the video during the hand-over, so by this line they are finished with the URL.
    /// Buffer is not: it stores the link and follows it when the post fires, up to a week later, so
    /// deleting here would hand it a URL that 404s on the day. Its copy is left to the bucket's
    /// lifecycle rule, which is sized for exactly that wait.
    ///
    /// Cleanup failure must never fail a live publish.
    /// </summary>
    private async Task ReleaseHeldMediaAsync(Content content, Platform primaryPlatform, CancellationToken ct)
    {
        var held = await db.HeldMedia.FindAsync([content.Id], ct);
        if (held is not null)
            db.HeldMedia.Remove(held);

        if (capabilities.FetchesHostedMediaAtPostTime(primaryPlatform))
        {
            logger.LogInformation(
                "Leaving staged clip {Key} for content {ContentId} in place: {Platform} fetches it " +
                "when the post fires, and the lifecycle rule will reap it after",
                content.StagedMediaKey, content.Id, primaryPlatform);
            return;
        }

        if (content.StagedMediaKey is not { } stagedKey)
            return;

        try
        {
            await mediaHost.DeleteAsync(stagedKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not unstage {Key} for content {ContentId}; " +
                "the lifecycle rule will reap it", stagedKey, content.Id);
        }

        content.StagedMediaKey = null;
        content.StagedMediaUrl = null;
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
