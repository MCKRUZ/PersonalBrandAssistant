using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

/// <summary>
/// What this protects: a YouTube upload costs 1,600 units of a 10,000-unit daily allowance shared
/// with the analytics polling. Overrun does not fail one video politely — the API returns
/// quotaExceeded for the rest of the day, so everything after it fails too. Handing a launch's
/// worth of clips over at once is exactly how that happens.
/// </summary>
public class YouTubeUploadPacerTests : IDisposable
{
    // 10:00 UTC = 02:00 Pacific, comfortably inside one quota day either side of the boundary.
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    private readonly ApplicationDbContext _db;

    public YouTubeUploadPacerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
    }

    private YouTubeUploadPacer CreatePacer(int budget = 4)
    {
        var options = new Mock<IOptionsMonitor<YouTubePublishingOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new YouTubePublishingOptions { DailyUploadBudget = budget });
        return new YouTubeUploadPacer(_db, options.Object, new FixedClock(Now));
    }

    private async Task BookAsync(int count, DateTimeOffset handoverAt,
        Platform platform = Platform.YouTube, bool deleted = false)
    {
        for (var i = 0; i < count; i++)
        {
            _db.Contents.Add(new Content
            {
                Title = $"booked {i}",
                Body = "x",
                PrimaryPlatform = platform,
                Status = ContentStatus.Scheduled,
                HandoverAt = handoverAt,
                IsDeleted = deleted
            });
        }

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task NextHandoverSlot_WithNothingBooked_UploadsImmediately()
    {
        // Earliest possible is the right bias: the video stays private until its publish time
        // either way, so every day of slack is a day for a failed upload to be noticed and retried.
        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        Assert.Equal(Now, slot);
    }

    [Fact]
    public async Task NextHandoverSlot_WhenTodayIsFull_WaitsForTheNextQuotaDay()
    {
        await BookAsync(4, Now.AddHours(1));

        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        // Quota resets at midnight Pacific, which is 08:00 UTC — NOT midnight UTC and not midnight
        // local. Getting this wrong hands the next batch an allowance that has not reset yet.
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero), slot);
    }

    [Fact]
    public async Task NextHandoverSlot_SpreadsAcrossConsecutiveDays_WhenSeveralAreFull()
    {
        await BookAsync(4, Now.AddHours(1));                                   // today
        await BookAsync(4, new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero));  // tomorrow

        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero), slot);
    }

    // The refusal that matters. Accepting this clip would mean uploading it after it was supposed to
    // be live, which YouTube rejects outright — days later, unattended, with nobody watching a log.
    [Fact]
    public async Task NextHandoverSlot_WhenEveryDayBeforeGoLiveIsFull_RefusesRatherThanOverbooking()
    {
        await BookAsync(4, Now.AddHours(1));

        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddHours(6), CancellationToken.None);

        Assert.Null(slot);
    }

    [Fact]
    public async Task NextHandoverSlot_ForAGoLiveAlreadyPast_Refuses()
    {
        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddMinutes(-1), CancellationToken.None);

        Assert.Null(slot);
    }

    // The budget is YouTube's, not PBA's. A TikTok or Instagram clip held for the same day costs
    // Google nothing, and counting it would starve the lane that actually has a limit.
    [Fact]
    public async Task NextHandoverSlot_IgnoresClipsBookedForOtherPlatforms()
    {
        await BookAsync(4, Now.AddHours(1), Platform.Instagram);

        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        Assert.Equal(Now, slot);
    }

    [Fact]
    public async Task NextHandoverSlot_IgnoresDeletedClips()
    {
        await BookAsync(4, Now.AddHours(1), deleted: true);

        var slot = await CreatePacer().NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        Assert.Equal(Now, slot);
    }

    [Fact]
    public async Task NextHandoverSlot_HonoursAConfiguredBudget()
    {
        await BookAsync(1, Now.AddHours(1));

        var slot = await CreatePacer(budget: 1).NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero), slot);
    }

    // A budget of zero would refuse everything forever, which reads as "YouTube is broken" rather
    // than "somebody put 0 in a config file". One is the floor.
    [Fact]
    public async Task NextHandoverSlot_TreatsAZeroBudgetAsOne()
    {
        var slot = await CreatePacer(budget: 0).NextHandoverSlotAsync(Now.AddDays(5), CancellationToken.None);

        Assert.Equal(Now, slot);
    }

    public void Dispose() => _db.Dispose();
}
