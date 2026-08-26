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
    private readonly Mock<IHandoverPlanner> _planner = new();
    private readonly Mock<IContentScheduler> _scheduler = new();

    public PublishSocialClipHandlerTests()
    {
        // Default to a platform that takes the clip now and holds the post itself (TikTok via
        // Buffer) — the original behaviour of this command, and what every test predating PBA-held
        // scheduling assumes.
        _planner.Setup(p => p.PlanAsync(
                It.IsAny<Platform>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandoverPlan.Now());

        _scheduler.Setup(s => s.SchedulePublish(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>()))
            .Returns("job-1");
    }

    private PublishSocialClip.Handler CreateHandler(ApplicationDbContext context) =>
        new(context, _publisher.Object, _planner.Object, _scheduler.Object);

    /// <summary>Makes PBA hold the post and the media until the go-live moment — Instagram's shape,
    /// where Meta has no scheduling concept, so the handover IS the moment of posting.</summary>
    private void PbaHoldsUntilTheSlot() =>
        _planner.Setup(p => p.PlanAsync(
                It.IsAny<Platform>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Platform _, DateTimeOffset goLive, CancellationToken _) =>
                HandoverPlan.Hold(goLive));

    /// <summary>Makes PBA hold the clip and hand it over EARLY — YouTube's shape, where the platform
    /// schedules the release itself but rations how many videos it will accept in a day.</summary>
    private void PbaHoldsUntil(DateTimeOffset handoverAt) =>
        _planner.Setup(p => p.PlanAsync(
                It.IsAny<Platform>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandoverPlan.Hold(handoverAt));

    /// <summary>Makes PBA hold the clip AND keep the staged media alive past the hand-over, because
    /// the platform only ever fetches it from the URL when the post fires — TikTok via Buffer.</summary>
    private void PbaHoldsUntil(DateTimeOffset handoverAt, DateTimeOffset mediaNeededUntil) =>
        _planner.Setup(p => p.PlanAsync(
                It.IsAny<Platform>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandoverPlan.Hold(handoverAt, mediaNeededUntil));

    /// <summary>Makes the planner refuse — no upload capacity before the go-live moment.</summary>
    private void PlannerRefuses(string reason) =>
        _planner.Setup(p => p.PlanAsync(
                It.IsAny<Platform>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandoverPlan.Refuse(reason));

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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(), ScheduledAt: when),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await context.Contents.SingleAsync();
        Assert.Equal(when, stored.ScheduledAt);
    }

    // Npgsql writes DateTimeOffset to timestamptz and REJECTS a non-zero offset outright, so a
    // -04:00 slot — what every America/New_York campaign queue sends — kills the insert with a 500
    // that never mentions scheduling. This is asserted here rather than caught by an integration
    // test because the in-memory provider these tests run on does not enforce the rule: the first
    // version of this feature passed every test and then failed on all five real clips.
    [Theory]
    [InlineData(-4)]
    [InlineData(-7)]
    [InlineData(5.5)]
    public async Task Handle_NonUtcOffset_IsNormalisedToUtcForPostgres(double offsetHours)
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, null, []));
        var offset = TimeSpan.FromHours(offsetHours);
        var when = new DateTimeOffset(DateTime.UtcNow.AddDays(2).Ticks, TimeSpan.Zero)
            .ToOffset(offset);

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(), ScheduledAt: when),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(TimeSpan.Zero, stored.ScheduledAt!.Value.Offset);
        // Same instant, different clock face — normalising must not move the slot.
        Assert.Equal(when.UtcDateTime, stored.ScheduledAt!.Value.UtcDateTime);
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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip(),
                ScheduledAt: DateTimeOffset.UtcNow.AddHours(-3)),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Null(stored.ScheduledAt);
    }

    // ---- PBA-held scheduling: for platforms that cannot schedule at all (Instagram) ----

    // Meta has no scheduling concept, so nobody but PBA can hold this post. Handing it to the
    // connector now would publish it immediately — days early, publicly, un-undoably.
    [Fact]
    public async Task Handle_PlatformCannotSchedule_PbaHoldsThePostInsteadOfPublishingNow()
    {
        await using var context = CreateContext();
        PbaHoldsUntilTheSlot();

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram],
                DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _publisher.Verify(p => p.PublishAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
            It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The video arrives with THIS request and is gone by the slot. Keeping it is the only thing that
    // makes a held post publishable later — without it the job fires with no media.
    //
    // It is kept beside the record rather than on public storage, and nothing public is created yet.
    // Public objects are reaped on a short lifecycle rule, so staging one at request time made the
    // reachable campaign length a storage setting rather than a fact about the platform.
    [Fact]
    public async Task Handle_PbaHoldsThePost_KeepsTheClipBesideTheRecordAndNothingPublicYet()
    {
        await using var context = CreateContext();
        PbaHoldsUntilTheSlot();

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram],
                DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        var held = await context.HeldMedia.SingleAsync();
        Assert.Equal(stored.Id, held.ContentId);
        Assert.Equal("clip.mp4", held.FileName);
        Assert.Equal("video/mp4", held.ContentType);
        Assert.Equal(Clip().Data, held.Data);

        Assert.Null(stored.StagedMediaUrl);
        Assert.Null(stored.StagedMediaKey);
    }

    // Scheduled is what makes the record PBA's responsibility: it is the status the Hangfire job and
    // the startup reconciler both act on. Left Approved, the slot would simply never fire.
    [Fact]
    public async Task Handle_PbaHoldsThePost_MovesToScheduledAndBooksTheJob()
    {
        await using var context = CreateContext();
        PbaHoldsUntilTheSlot();
        var when = DateTimeOffset.UtcNow.AddDays(2);

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram], when),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(ContentStatus.Scheduled, stored.Status);
        Assert.Equal("job-1", stored.HangfireJobId);
        _scheduler.Verify(s => s.SchedulePublish(stored.Id, when.ToUniversalTime()), Times.Once);
    }

    // YouTube's shape, and the distinction the whole handover idea rests on: the job must fire when
    // the CLIP is handed over, not when the post goes live. Those are the same instant for Instagram
    // and days apart here — booking the job at go-live instead would upload the video after the
    // moment it was supposed to appear, which YouTube rejects outright.
    [Fact]
    public async Task Handle_WhenTheHandoverIsEarlierThanTheSlot_BooksTheJobForTheHandover()
    {
        await using var context = CreateContext();
        var goLive = DateTimeOffset.UtcNow.AddDays(6);
        var handover = DateTimeOffset.UtcNow.AddDays(1);
        PbaHoldsUntil(handover);

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Short 3", "description", Clip(), [Platform.YouTube], goLive),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(handover, stored.HandoverAt);
        // The go-live time survives on the record: it is what travels to YouTube as the publish
        // time, days after the upload.
        Assert.Equal(goLive.ToUniversalTime(), stored.ScheduledAt);
        _scheduler.Verify(s => s.SchedulePublish(stored.Id, handover), Times.Once);
        _scheduler.Verify(s => s.SchedulePublish(stored.Id, goLive.ToUniversalTime()), Times.Never);
    }

    // TikTok's new shape, and the reason a two-week campaign used to need a human on the right day:
    // Buffer refuses a post more than a week out, so PBA holds the clip and hands it over once the
    // slot is near. Nothing about the handler is TikTok-specific here on purpose — the planner is the
    // only thing that decides handover timing, and this proves the held path serves it unchanged.
    [Fact]
    public async Task Handle_TikTokSlotBeyondBuffersHorizon_IsHeldAndBookedRatherThanRefused()
    {
        await using var context = CreateContext();
        var goLive = DateTimeOffset.UtcNow.AddDays(10);
        var handover = DateTimeOffset.UtcNow.AddDays(4);
        PbaHoldsUntil(handover);

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Drip 8", "caption", Clip(), [Platform.TikTok], goLive),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await context.Contents.SingleAsync();
        Assert.Equal(handover, stored.HandoverAt);
        Assert.Equal(goLive.ToUniversalTime(), stored.ScheduledAt);
        Assert.Equal(stored.Id, (await context.HeldMedia.SingleAsync()).ContentId);
        _scheduler.Verify(s => s.SchedulePublish(stored.Id, handover), Times.Once);
        // Nothing went to Buffer today — that call is what used to come back refused.
        _publisher.Verify(p => p.PublishAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
            It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The point of holding the clip beside the record: how far ahead a campaign can be booked is now
    // a fact about the platform, not about a storage lifetime. A two-week TikTok queue used to be
    // refused at the far end for a reason that had nothing to do with TikTok.
    [Fact]
    public async Task Handle_WhenTheClipIsReadLongAfterTheHandover_IsAcceptedRatherThanRefused()
    {
        await using var context = CreateContext();
        var goLive = DateTimeOffset.UtcNow.AddDays(13);
        PbaHoldsUntil(DateTimeOffset.UtcNow.AddDays(6), mediaNeededUntil: goLive);

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Drip 9", "caption", Clip(), [Platform.TikTok], goLive),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await context.Contents.SingleAsync();
        Assert.Equal(goLive.ToUniversalTime(), stored.ScheduledAt);
        Assert.Equal(stored.Id, (await context.HeldMedia.SingleAsync()).ContentId);
    }

    // Refusing beats accepting something that cannot work. A clip with no upload capacity before its
    // slot would sit staged, cost storage, and fail unattended days later.
    [Fact]
    public async Task Handle_WhenThePlannerRefuses_ReturnsTheReasonAndStoresNothing()
    {
        await using var context = CreateContext();
        PlannerRefuses("YouTube has no upload capacity left before then.");

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Short 4", "description", Clip(), [Platform.YouTube],
                DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("no upload capacity", string.Join(" ", result.Errors));
        Assert.Empty(context.Contents);
        Assert.Empty(context.HeldMedia);
    }

    // Keywords have a real field on YouTube and drive discovery there. The caption lanes must stay
    // empty — TikTokFormatter turns tags into hashtags, and these captions carry their own.
    [Fact]
    public async Task Handle_StoresSuppliedTags_ForPlatformsThatHaveATagsField()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, "https://youtu.be/abc", []));

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Short 5", "description", Clip(), [Platform.YouTube],
                Tags: ["impostor syndrome", "engineering"]),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(["impostor syndrome", "engineering"], stored.Tags);
    }

    // The mirror of the TikTok rule. There, Scheduled would double-post because Buffer also fires.
    // Here nothing else fires, so PBA must own it — and staging without scheduling would leave a
    // clip paid for in storage that never posts.
    [Fact]
    public async Task Handle_PlatformCanSchedule_HandsOffImmediatelyAndStagesNothing()
    {
        await using var context = CreateContext();
        PublisherReturns(new PublishResult(true, null, []));

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.TikTok],
                DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(ContentStatus.Approved, stored.Status);
        Assert.Null(stored.StagedMediaUrl);
        _scheduler.Verify(s => s.SchedulePublish(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>()), Times.Never);
        Assert.Empty(context.HeldMedia);
    }

    // No slot means post now, whatever the platform can or cannot schedule. Staging here would hold
    // a clip nobody is waiting for.
    [Fact]
    public async Task Handle_NoSlotOnANonSchedulingPlatform_PublishesNowWithoutStaging()
    {
        await using var context = CreateContext();
        PbaHoldsUntilTheSlot();
        PublisherReturns(new PublishResult(true, null, []));

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram]),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(ContentStatus.Approved, stored.Status);
        Assert.Null(stored.StagedMediaUrl);
        _publisher.Verify(p => p.PublishAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
            It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Held bytes live as long as their record, so a distant slot is no longer a reason to refuse. The
    // public copy is made at hand-over, and it is that copy — not this one — the lifecycle rule
    // reaps. This case used to be rejected outright.
    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(30)]
    public async Task Handle_HeldPost_IsAcceptedHoweverFarOutTheSlotIs(int daysOut)
    {
        await using var context = CreateContext();
        PbaHoldsUntilTheSlot();

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram],
                DateTimeOffset.UtcNow.AddDays(daysOut)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await context.HeldMedia.CountAsync());
    }

    // Persisted, not just passed through: a held post is published days later, and by then this
    // record is the only thing that remembers which frame was chosen.
    [Fact]
    public async Task Handle_HeldPost_RemembersTheChosenCoverFrame()
    {
        await using var context = CreateContext();
        PbaHoldsUntilTheSlot();

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram],
                DateTimeOffset.UtcNow.AddDays(2), CoverFrameOffsetMs: 2334),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(2334, stored.CoverFrameOffsetMs);
    }

    [Fact]
    public async Task Handle_NegativeCoverFrame_IsRejectedRatherThanSentToThePlatform()
    {
        await using var context = CreateContext();

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), null, null,
                CoverFrameOffsetMs: -1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
        Assert.Empty(await context.Contents.ToListAsync());
    }

    // The state machine only permits Approve with a non-empty body, so an empty caption would
    // otherwise fail deep in the transition with an opaque message.
    [Fact]
    public async Task Handle_EmptyCaption_ReturnsValidationFailureAndCreatesNothing()
    {
        await using var context = CreateContext();

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
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

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Part 6", "caption", Clip()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("TikTok", Assert.Single(result.Errors));
    }
}
