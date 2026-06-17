using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Digests.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Digests;

public class DigestQueriesTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task GetLatestDigest_ReturnsMostRecentWithItems()
    {
        using var db = NewDb();
        var idea = new Idea { Title = "Story", SourceName = "S", DeduplicationKey = "k" };
        db.Ideas.Add(idea);
        var older = new Digest { Date = new DateOnly(2026, 6, 1), Title = "Old", Intro = "i", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var newer = new Digest { Date = new DateOnly(2026, 6, 4), Title = "New", Intro = "i", CreatedAt = DateTimeOffset.UtcNow };
        newer.Items.Add(new DigestItem { IdeaId = idea.Id, Rank = 1, Score = 9, WhyItMatters = "w" });
        db.Digests.AddRange(older, newer);
        await db.SaveChangesAsync();

        var handler = new GetLatestDigest.Handler(db);
        var result = await handler.Handle(new GetLatestDigest.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("New", result.Value!.Title);
        Assert.Single(result.Value.Items);
        Assert.Equal("Story", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task ListDigests_ReturnsSummariesNewestFirst()
    {
        using var db = NewDb();
        db.Digests.AddRange(
            new Digest { Date = new DateOnly(2026, 6, 1), Title = "A", Intro = "i", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new Digest { Date = new DateOnly(2026, 6, 2), Title = "B", Intro = "i", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var handler = new ListDigests.Handler(db);
        var result = await handler.Handle(new ListDigests.Query(), default);

        Assert.Equal("B", result.Value![0].Title);
    }

    [Fact]
    public async Task GetDigest_UnknownId_ReturnsFailure()
    {
        using var db = NewDb();
        var handler = new GetDigest.Handler(db);
        var result = await handler.Handle(new GetDigest.Query(Guid.NewGuid()), default);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetLatestDigest_MicrosoftKind_ReturnsMicrosoftBriefNotMain()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 6, 4);
        db.Digests.AddRange(
            new Digest { Date = date, Kind = DigestKind.Main, Title = "Main", Intro = "i", CreatedAt = DateTimeOffset.UtcNow },
            new Digest { Date = date, Kind = DigestKind.Microsoft, Title = "MS", Intro = "i", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new GetLatestDigest.Handler(db).Handle(new GetLatestDigest.Query(DigestKind.Microsoft), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("MS", result.Value!.Title);
    }

    [Fact]
    public async Task ListDigests_ExcludesMicrosoftBriefs()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 6, 4);
        db.Digests.AddRange(
            new Digest { Date = date, Kind = DigestKind.Main, Title = "Main", Intro = "i", CreatedAt = DateTimeOffset.UtcNow },
            new Digest { Date = date, Kind = DigestKind.Microsoft, Title = "MS", Intro = "i", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new ListDigests.Handler(db).Handle(new ListDigests.Query(), default);

        Assert.Single(result.Value!);
        Assert.Equal("Main", result.Value![0].Title);
    }

    [Fact]
    public async Task GetDigestByDate_ReturnsRequestedDateAndKind()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 6, 4);
        db.Digests.AddRange(
            new Digest { Date = date, Kind = DigestKind.Main, Title = "Main", Intro = "i", CreatedAt = DateTimeOffset.UtcNow },
            new Digest { Date = date, Kind = DigestKind.Microsoft, Title = "MS", Intro = "i", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new GetDigestByDate.Handler(db).Handle(new GetDigestByDate.Query(date, DigestKind.Microsoft), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("MS", result.Value!.Title);
    }

    [Fact]
    public async Task GetDigestByDate_MissingKindForDate_ReturnsFailure()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 6, 4);
        db.Digests.Add(new Digest { Date = date, Kind = DigestKind.Main, Title = "Main", Intro = "i", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await new GetDigestByDate.Handler(db).Handle(new GetDigestByDate.Query(date, DigestKind.Microsoft), default);

        Assert.False(result.IsSuccess); // no Microsoft brief that day
    }
}
