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
    private readonly Mock<IPlatformCapabilityReader> _capabilities = new();
    private readonly Mock<IMediaHost> _mediaHost = new();
    private readonly Mock<IContentScheduler> _scheduler = new();

    public PublishSocialClipHandlerTests()
    {
        // Default to a platform that holds the post itself (TikTok via Buffer) — the original
        // behaviour of this command, and what every test predating PBA-held scheduling assumes.
        _capabilities.Setup(c => c.SupportsScheduling(It.IsAny<Platform>())).Returns(true);

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/ig/x.mp4", "ig/x.mp4"));

        _scheduler.Setup(s => s.SchedulePublish(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>()))
            .Returns("job-1");
    }

    private PublishSocialClip.Handler CreateHandler(ApplicationDbContext context) =>
        new(context, _publisher.Object, _capabilities.Object, _mediaHost.Object, _scheduler.Object);

    /// <summary>Makes the target platform one that cannot schedule — Instagram, where Meta has no
    /// scheduling concept — so PBA has to hold the post and the media itself.</summary>
    private void PlatformCannotSchedule() =>
        _capabilities.Setup(c => c.SupportsScheduling(It.IsAny<Platform>())).Returns(false);

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
        PlatformCannotSchedule();

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

    // The video arrives with THIS request and is gone by the slot. Staging it up front is the only
    // thing that makes a held post publishable later — without it the job fires with no media.
    [Fact]
    public async Task Handle_PbaHoldsThePost_StagesTheClipSoItSurvivesUntilTheSlot()
    {
        await using var context = CreateContext();
        PlatformCannotSchedule();

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Beat 2", "caption", Clip(), [Platform.Instagram],
                DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal("https://media.matthewkruczek.ai/ig/x.mp4", stored.StagedMediaUrl);
        Assert.Equal("ig/x.mp4", stored.StagedMediaKey);
    }

    // Scheduled is what makes the record PBA's responsibility: it is the status the Hangfire job and
    // the startup reconciler both act on. Left Approved, the slot would simply never fire.
    [Fact]
    public async Task Handle_PbaHoldsThePost_MovesToScheduledAndBooksTheJob()
    {
        await using var context = CreateContext();
        PlatformCannotSchedule();
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
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // No slot means post now, whatever the platform can or cannot schedule. Staging here would hold
    // a clip nobody is waiting for.
    [Fact]
    public async Task Handle_NoSlotOnANonSchedulingPlatform_PublishesNowWithoutStaging()
    {
        await using var context = CreateContext();
        PlatformCannotSchedule();
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
