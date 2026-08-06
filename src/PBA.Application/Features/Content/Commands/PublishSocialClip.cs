using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Features.Content.Commands;

/// <summary>
/// Publishes a video clip that was written, reviewed and approved OUTSIDE PBA — ai-video-producer
/// renders the clip and owns its caption. The normal idea → draft → review → approve flow has
/// already happened upstream, so this walks a fresh Content record straight to Approved rather
/// than opening general ad-hoc content creation on the external API.
///
/// The caption becomes the body with NO tags. That is deliberate: TikTokFormatter appends
/// Content.Tags as hashtags, and these captions already carry their own — empty tags is what
/// makes the caption publish verbatim.
/// </summary>
public static class PublishSocialClip
{
    /// <summary>
    /// <paramref name="ScheduledAt"/> asks the platform to hold the post until then, rather than
    /// PBA holding it — the connector decides. For TikTok, BufferConnector turns it into
    /// mode:customScheduled and Buffer fires the post itself, which is the point: no machine here
    /// has to be awake at the slot. Connectors that cannot schedule ignore it and post now.
    /// Past timestamps are dropped (see the handler), so a late caller still posts.
    /// </summary>
    public record Command(
        string Title,
        string Caption,
        MediaAttachment Media,
        IReadOnlyList<Platform>? TargetPlatforms = null,
        DateTimeOffset? ScheduledAt = null) : IRequest<Result<PublishResult>>;

    internal sealed class Handler(
        IAppDbContext db,
        IContentPublisher publisher,
        IPlatformCapabilityReader capabilities,
        IMediaHost mediaHost,
        IContentScheduler scheduler)
        : IRequestHandler<Command, Result<PublishResult>>
    {
        public async Task<Result<PublishResult>> Handle(Command request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Caption))
                return Result<PublishResult>.ValidationFailure(["Caption is required."]);

            if (!request.Media.IsVideo)
                return Result<PublishResult>.ValidationFailure(
                    ["A social clip must be a video (mp4/mov)."]);

            IReadOnlyList<Platform> platforms = request.TargetPlatforms is { Count: > 0 }
                ? request.TargetPlatforms
                : [Platform.TikTok];

            var content = new ContentEntity
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Social clip" : request.Title,
                Body = request.Caption,
                ContentType = ContentType.SocialClip,
                PrimaryPlatform = platforms[0],
                TargetPlatforms = [.. platforms],
                Tags = []
            };

            // Walk the real state machine rather than assigning Status directly: Approve is only
            // legal from Draft and only with a non-empty body, and that guard is worth keeping.
            var machine = ContentStateMachine.Create(content);
            try
            {
                await machine.FireAsync(ContentTrigger.StartDraft);
                await machine.FireAsync(ContentTrigger.Approve);
            }
            catch (InvalidOperationException)
            {
                return Result<PublishResult>.Fail("Could not move the clip to Approved.");
            }

            // AFTER the transitions, never in the initializer: entering Draft nulls ScheduledAt.
            //
            // The record deliberately stays Approved rather than moving to Scheduled. Scheduled
            // means "PBA will publish this later", and ScheduledPublishReconciler sweeps exactly
            // that set on startup — the platform would fire the post and PBA would fire it again.
            // Here the hand-off has already happened and the platform owns the timing.
            //
            // A slot that has already passed becomes an immediate post, matching what a late drip
            // run did before: a past dueAt is not something we have verified the platform accepts.
            //
            // ToUniversalTime is required, not tidiness. Npgsql writes DateTimeOffset to timestamptz
            // and REJECTS any non-zero offset outright, so a caller sending -04:00 — which every
            // America/New_York campaign queue does — fails the insert with a 500 that says nothing
            // about scheduling. The in-memory provider the tests use does not enforce this.
            var slot = request.ScheduledAt is { } at && at > DateTimeOffset.UtcNow
                ? at.ToUniversalTime()
                : (DateTimeOffset?)null;

            if (slot is not null)
                content.ScheduledAt = slot;

            // Who holds the post until its slot depends on whether the platform CAN. Buffer holds a
            // TikTok post itself, so PBA hands the clip over now and is done. Meta has no scheduling
            // concept at all, so PBA must hold it — which also means holding the VIDEO, because the
            // bytes arrive with this request and are gone by the time the slot comes round.
            var platformHoldsIt = capabilities.SupportsScheduling(platforms[0]);

            if (slot is not null && !platformHoldsIt)
            {
                var staged = await mediaHost.UploadAsync(
                    request.Media.Data, request.Media.FileName, request.Media.ContentType, ct);
                content.StagedMediaUrl = staged.Url;
                content.StagedMediaKey = staged.Key;

                try
                {
                    await machine.FireAsync(ContentTrigger.Schedule);
                }
                catch (InvalidOperationException)
                {
                    return Result<PublishResult>.Fail("Could not move the clip to Scheduled.");
                }

                db.Contents.Add(content);
                await db.SaveChangesAsync(ct);

                content.HangfireJobId = scheduler.SchedulePublish(content.Id, slot.Value);
                await db.SaveChangesAsync(ct);

                return Result<PublishResult>.Success(new PublishResult(true, null, []));
            }

            db.Contents.Add(content);
            await db.SaveChangesAsync(ct);

            var result = await publisher.PublishAsync(content.Id, platforms, request.Media, ct);
            if (result.PrimarySuccess)
                return Result<PublishResult>.Success(result);

            // PublishResult drops the connector's message, but ContentPublisher persists it on the
            // platform-publish row. Read it back: a caller that only hears "publish failed" cannot
            // tell a disconnected channel from a rejected file, and guesses instead of fixing.
            var reason = await db.ContentPlatformPublishes
                .Where(p => p.ContentId == content.Id && p.Platform == platforms[0])
                .OrderByDescending(p => p.PublishedAt)
                .Select(p => p.ErrorMessage)
                .FirstOrDefaultAsync(ct);

            return Result<PublishResult>.Fail(
                $"Publish to {platforms[0]} failed: {reason ?? "no reason recorded by the connector."}");
        }
    }
}
