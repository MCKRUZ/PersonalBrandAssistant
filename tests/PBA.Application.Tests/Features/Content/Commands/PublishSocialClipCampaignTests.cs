using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Commands;

/// <summary>
/// The behaviour the whole hand-over design exists for, exercised through the REAL planner and the
/// REAL capability reader rather than a mock of either.
///
/// A campaign is scheduled once, as a batch, and then runs for a fortnight. Buffer refuses any post
/// more than a week ahead, so every slot past the first week used to come back rejected and somebody
/// had to sit down and run the hand-off again on the right day — the exact human dependency moving
/// this into PBA was meant to remove.
///
/// Each piece is tested on its own elsewhere. This is here because the pieces individually passing
/// is what the previous version of this feature had, and the campaign still could not be booked.
/// </summary>
public class PublishSocialClipCampaignTests
{
    private static readonly TimeSpan BufferWindow = TimeSpan.FromDays(7);

    private readonly Mock<IContentPublisher> _publisher = new();
    private readonly Mock<IContentScheduler> _scheduler = new();
    private readonly Mock<IPlatformConnector> _buffer = new();

    public PublishSocialClipCampaignTests()
    {
        _scheduler.Setup(s => s.SchedulePublish(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>()))
            .Returns("job-1");

        // Slots already inside Buffer's week go straight out, so the publisher runs for real on
        // those and has to answer.
        _publisher.Setup(p => p.PublishAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
                It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResult(true, null, []));
    }

    /// <summary>TikTok's real shape: Buffer schedules, but only a week out, and it fetches the video
    /// from our URL when the post fires rather than taking the bytes at hand-over.</summary>
    private PublishSocialClip.Handler CreateHandler(ApplicationDbContext context)
    {
        _buffer.Setup(c => c.GetCapabilities()).Returns(new PlatformCapabilities(
            MaxCharacters: 2200,
            SupportsMarkdown: false,
            SupportsHtml: false,
            SupportsImages: false,
            SupportsScheduling: true,
            SupportsThreads: false,
            SupportedMediaTypes: ["video/mp4"],
            FetchesHostedMediaAtPostTime: true,
            SchedulingHorizon: BufferWindow));

        var services = new ServiceCollection();
        services.AddKeyedSingleton(Platform.TikTok, _buffer.Object);
        var provider = services.BuildServiceProvider();

        var planner = new HandoverPlanner(
            new PlatformCapabilityReader(provider), provider, TimeProvider.System);

        return new PublishSocialClip.Handler(context, _publisher.Object, planner, _scheduler.Object);
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MediaAttachment Clip() => new([1, 2, 3], "clip.mp4", "video/mp4");

    // The nine-item drip, handed over in one run. Every slot books: the near ones go straight to
    // Buffer, the far ones are held by PBA and booked to be handed over once their date comes inside
    // Buffer's week. Two weeks of TikTok posts, and nothing on a desktop has to run again.
    [Fact]
    public async Task AWholeFortnightQueue_BooksInASingleRun()
    {
        await using var context = CreateContext();
        var handler = CreateHandler(context);
        var teaser = DateTimeOffset.UtcNow.AddDays(5);
        var slots = new[] { teaser }
            .Concat(Enumerable.Range(0, 8).Select(i => DateTimeOffset.UtcNow.AddDays(7 + i)))
            .ToList();

        foreach (var slot in slots)
        {
            var result = await handler.Handle(
                new PublishSocialClip.Command($"Drip {slot:MM-dd}", "caption", Clip(),
                    [Platform.TikTok], slot),
                CancellationToken.None);

            Assert.True(result.IsSuccess, $"slot {slot:u} was refused: {string.Join(" ", result.Errors)}");
        }

        Assert.Equal(slots.Count, await context.Contents.CountAsync());
    }

    // The near end of the campaign goes to Buffer today: it is already inside the week Buffer
    // accepts, so holding it at PBA would gain nothing and cost a staged object.
    [Fact]
    public async Task ASlotInsideBuffersWeek_GoesStraightToBufferWithNothingHeld()
    {
        await using var context = CreateContext();
        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Teaser", "caption", Clip(), [Platform.TikTok],
                DateTimeOffset.UtcNow.AddDays(5)),
            CancellationToken.None);

        Assert.Empty(context.HeldMedia);
        _publisher.Verify(p => p.PublishAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
            It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The far end is the case that used to fail. PBA keeps it, and books the hand-over for a moment
    // that is strictly inside Buffer's week — not on its edge, where a job firing a minute late
    // would be refused, and not so early that Buffer refuses it on arrival.
    [Fact]
    public async Task ASlotBeyondBuffersWeek_IsHeldAndHandedOverOnceItComesInsideTheWindow()
    {
        await using var context = CreateContext();
        var goLive = DateTimeOffset.UtcNow.AddDays(14);

        var handler = CreateHandler(context);
        var result = await handler.Handle(
            new PublishSocialClip.Command("Drip 9", "caption", Clip(), [Platform.TikTok], goLive),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await context.Contents.SingleAsync();
        Assert.Equal(goLive.ToUniversalTime(), stored.ScheduledAt);
        Assert.NotNull(stored.HandoverAt);

        var handover = stored.HandoverAt!.Value;
        Assert.True(handover > DateTimeOffset.UtcNow, "a hand-over already due is not a hold");
        Assert.True(goLive - handover < BufferWindow,
            "the hand-over must land inside Buffer's window, or Buffer refuses the post on arrival");

        // The clip waits beside its record, and nothing public exists yet — the public copy is made
        // at hand-over, so the short lifecycle that reaps it never has to cover this wait.
        Assert.Equal(stored.Id, (await context.HeldMedia.SingleAsync()).ContentId);
        Assert.Null(stored.StagedMediaUrl);

        // Booked for the hand-over, not the go-live: firing at the go-live would ask Buffer to
        // schedule a post for a moment that has already arrived.
        _scheduler.Verify(s => s.SchedulePublish(stored.Id, handover), Times.Once);

        // Nothing was pushed at Buffer today. That call is what used to come back refused.
        _publisher.Verify(p => p.PublishAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Platform>?>(),
            It.IsAny<MediaAttachment?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The record stays out of the platform's hands until the hand-over, but it must be PBA's
    // responsibility in the meantime: Scheduled is the status both the Hangfire job and the startup
    // sweep act on. Left Approved, the slot would simply never fire.
    [Fact]
    public async Task AHeldSlot_BecomesPbasResponsibilityRatherThanStayingApproved()
    {
        await using var context = CreateContext();

        var handler = CreateHandler(context);
        await handler.Handle(
            new PublishSocialClip.Command("Drip 9", "caption", Clip(), [Platform.TikTok],
                DateTimeOffset.UtcNow.AddDays(14)),
            CancellationToken.None);

        Assert.Equal(Domain.Enums.ContentStatus.Scheduled, (await context.Contents.SingleAsync()).Status);
    }
}
