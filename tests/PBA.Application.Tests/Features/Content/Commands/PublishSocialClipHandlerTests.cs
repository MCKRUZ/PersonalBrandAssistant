using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Commands;

public class PublishSocialClipHandlerTests
{
    private readonly Mock<IContentPublisher> _publisher = new();

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static MediaAttachment Clip() => new([1, 2, 3], "clip.mp4", "video/mp4");

    private void PublisherReturns(PublishResult result) =>
        _publisher.Setup(p => p.PublishAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
                It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    // The whole reason this command exists instead of the normal draft/review flow: the caption was
    // written and approved upstream and must reach TikTok byte-for-byte. TikTokFormatter appends
    // Content.Tags as hashtags, so tags MUST stay empty — otherwise captions that already carry
    // their own hashtags get a duplicated set silently appended.
    [Fact]
    public async Task Handle_StoresCaptionVerbatimWithNoTags()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, "https://tiktok.com/@x/video/1", []));
        const string caption = "You'd never tell a new hire to \"go fix bugs\".\n\n#AI #softwareengineering";

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", caption, Clip()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await context.Contents.SingleAsync();
        Assert.Equal(caption, stored.Body);
        Assert.Empty(stored.Tags);
    }

    // ContentPublisher refuses anything that is not Approved or Scheduled, so a clip left in Idea
    // or Draft would be silently skipped rather than published.
    [Fact]
    public async Task Handle_LeavesClipApprovedSoThePublisherWillAcceptIt()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, "https://tiktok.com/@x/video/1", []));

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip()), CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(ContentStatus.Approved, stored.Status);
        Assert.Equal(ContentType.SocialClip, stored.ContentType);
    }

    [Fact]
    public async Task Handle_NoTargetPlatforms_DefaultsToTikTok()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, "https://tiktok.com/@x/video/1", []));

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip()), CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(Platform.TikTok, stored.PrimaryPlatform);
        Assert.Equal([Platform.TikTok], stored.TargetPlatforms);
    }

    [Fact]
    public async Task Handle_ExplicitPlatforms_OverridesTheTikTokDefault()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, "https://linkedin.com/post/1", []));

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(), [Platform.LinkedIn]),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(Platform.LinkedIn, stored.PrimaryPlatform);
    }

    // ContentPublisher reads ScheduledAt off the Content record to build PlatformPublishRequest,
    // and BufferConnector turns that into mode:customScheduled + dueAt. So the assignment here is
    // the whole scheduling feature. It must land AFTER the state transitions: entering Draft nulls
    // ScheduledAt (ContentStateMachine), so setting it on the initializer would silently vanish.
    [Fact]
    public async Task Handle_FutureScheduledAt_IsStoredSoTheConnectorSchedulesRatherThanPostsNow()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, null, []));
        var when = DateTimeOffset.UtcNow.AddDays(2);

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(), ScheduledAt: when),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await context.Contents.SingleAsync();
        Assert.Equal(when, stored.ScheduledAt);
    }

    // Buffer holds the post and fires it itself, so PBA has already done its job at hand-off time.
    // If the record were left Scheduled, ScheduledPublishReconciler would sweep it once the time
    // passed and publish it a SECOND time — the exact duplicate-post failure that hit TikTok on
    // 2026-08-03, which cannot be undone because Buffer has no dedup key.
    [Fact]
    public async Task Handle_ScheduledClip_StaysApprovedSoTheReconcilerCannotRepublishIt()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, null, []));

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(),
                ScheduledAt: DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.NotEqual(ContentStatus.Scheduled, stored.Status);
        Assert.Equal(ContentStatus.Approved, stored.Status);
    }

    // A drip run that fires late hands over an item whose slot has already passed. Posting it now
    // is what the old hourly runner did, and it is what the caller wants — a past dueAt would
    // instead be handed to Buffer as a scheduled post, whose behaviour we have not verified.
    [Fact]
    public async Task Handle_PastScheduledAt_IsDroppedSoTheClipPostsImmediately()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, null, []));

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(),
                ScheduledAt: DateTimeOffset.UtcNow.AddHours(-3)),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Null(stored.ScheduledAt);
    }

    // The state machine only permits Approve with a non-empty body, so an empty caption would
    // otherwise fail deep in the transition with an opaque message.
    [Fact]
    public async Task Handle_EmptyCaption_ReturnsValidationFailureAndCreatesNothing()
    {
        await using var context = CreateContext();

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "   ", Clip()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
        Assert.Empty(await context.Contents.ToListAsync());
    }

    // BufferConnector rejects non-video outright; catching it here keeps a doomed Content record
    // from being created and left behind as Approved-but-never-published.
    [Fact]
    public async Task Handle_NonVideoMedia_ReturnsValidationFailureAndCreatesNothing()
    {
        await using var context = CreateContext();
        var image = new MediaAttachment([1, 2, 3], "card.png", "image/png");

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", image), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
        Assert.Empty(await context.Contents.ToListAsync());
    }

    // PublishResult drops the connector's message. A caller told only "publish failed" cannot tell
    // a disconnected Buffer channel from a rejected file — which is exactly how the previous
    // TikTok lane stayed broken while reporting green.
    [Fact]
    public async Task Handle_PublisherFails_SurfacesTheConnectorsRecordedReason()
    {
        await using var context = CreateContext();
        const string reason = "No TikTok channel is connected in Buffer for this organization.";

        _publisher.Setup(p => p.PublishAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
                It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, IReadOnlyList<Platform>? _, MediaAttachment? _, CancellationToken _) =>
            {
                // Mirror what ContentPublisher does on failure: record the row, drop the message.
                context.ContentPlatformPublishes.Add(new ContentPlatformPublish
                {
                    ContentId = id,
                    Platform = Platform.TikTok,
                    Status = PublishStatus.Failed,
                    ErrorMessage = reason,
                    PublishedAt = DateTimeOffset.UtcNow
                });
                context.SaveChanges();
                return new PublishResult(false, null, []);
            });

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(reason, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task Handle_PublisherFailsWithNoRecordedReason_StillSaysWhichPlatformFailed()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(false, null, []));

        var handler = new PublishSocialClip.Handler(context, _publisher.Object);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("TikTok", Assert.Single(result.Errors));
    }
}
