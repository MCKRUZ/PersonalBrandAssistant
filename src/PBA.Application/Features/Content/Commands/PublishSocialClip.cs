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
    public record Command(
        string Title,
        string Caption,
        MediaAttachment Media,
        IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result<PublishResult>>;

    internal sealed class Handler(IAppDbContext db, IContentPublisher publisher)
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
