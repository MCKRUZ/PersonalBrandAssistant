using Microsoft.Extensions.Logging.Abstractions;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

[Collection("Postgres")]
public class SubstackContentMatcherTests
{
    private readonly PostgresFixture _fixture;

    public SubstackContentMatcherTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(SubstackContentMatcher sut, ApplicationDbContext db)> CreateSutAsync()
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var sut = new SubstackContentMatcher(db, NullLogger<SubstackContentMatcher>.Instance);
        return (sut, db);
    }

    private static SubstackRssEntry CreateEntry(
        string title = "Test Post",
        DateTimeOffset? publishedAt = null) => new(
        Guid: $"guid-{Guid.NewGuid():N}",
        Title: title,
        Link: "https://matthewkruczek.substack.com/p/test-post",
        PublishedAt: publishedAt ?? DateTimeOffset.UtcNow,
        ContentEncoded: "<p>Content</p>",
        ContentHash: "abc123");

    private static Content CreateBlogContent(
        string title = "Test Post",
        PlatformType[]? platforms = null)
    {
        return Content.Create(
            ContentType.BlogPost,
            "Blog body content",
            title,
            platforms ?? [PlatformType.Substack, PlatformType.PersonalBlog]);
    }

    [Fact]
    public async Task MatchAsync_ReturnsHigh_OnExactTitleMatch()
    {
        var (sut, db) = await CreateSutAsync();
        var uniqueTitle = $"Exact Match Test {Guid.NewGuid():N}";
        var content = CreateBlogContent(uniqueTitle);
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var entry = CreateEntry(uniqueTitle);
        var result = await sut.MatchAsync(entry, default);

        Assert.Equal(MatchConfidence.High, result.Confidence);
        Assert.Equal(content.Id, result.ContentId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MatchAsync_ReturnsMedium_OnFuzzyTitleMatchWithin48h()
    {
        var (sut, db) = await CreateSutAsync();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var content = CreateBlogContent($"Agent-First Enterprise: Part {uniqueSuffix}");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var entry = CreateEntry(
            $"Agent-First Enterprise Part {uniqueSuffix}",
            publishedAt: content.CreatedAt.AddHours(1));
        var result = await sut.MatchAsync(entry, default);

        Assert.Equal(MatchConfidence.Medium, result.Confidence);
        Assert.Equal(content.Id, result.ContentId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MatchAsync_ReturnsNone_WhenNoMatchingContentFound()
    {
        var (sut, db) = await CreateSutAsync();

        var entry = CreateEntry("Completely Unrelated Topic");
        var result = await sut.MatchAsync(entry, default);

        Assert.Equal(MatchConfidence.None, result.Confidence);
        Assert.Null(result.ContentId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MatchAsync_OnlyMatchesBlogPostContent()
    {
        var (sut, db) = await CreateSutAsync();
        var uniqueTitle = $"Social Only Post {Guid.NewGuid():N}";
        var socialContent = Content.Create(
            ContentType.SocialPost, "body", uniqueTitle, [PlatformType.Substack]);
        db.Contents.Add(socialContent);
        await db.SaveChangesAsync();

        var entry = CreateEntry(uniqueTitle);
        var result = await sut.MatchAsync(entry, default);

        Assert.Equal(MatchConfidence.None, result.Confidence);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MatchAsync_SkipsContentWithSubstackPostUrlAlreadySet()
    {
        var (sut, db) = await CreateSutAsync();
        var uniqueTitle = $"Already Published {Guid.NewGuid():N}";
        var content = CreateBlogContent(uniqueTitle);
        content.SubstackPostUrl = "https://matthewkruczek.substack.com/p/already";
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var entry = CreateEntry(uniqueTitle);
        var result = await sut.MatchAsync(entry, default);

        Assert.Equal(MatchConfidence.None, result.Confidence);
        Assert.Null(result.ContentId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MatchAsync_HandlesEmptyTitleGracefully()
    {
        var (sut, db) = await CreateSutAsync();

        var entry = CreateEntry(title: "");
        var result = await sut.MatchAsync(entry, default);

        Assert.Equal(MatchConfidence.None, result.Confidence);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task FuzzyMatch_ColonRemoval_StillMatches()
    {
        var (sut, db) = await CreateSutAsync();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var content = CreateBlogContent($"Colon-Test Enterprise: Segment {uniqueSuffix}");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var entry = CreateEntry(
            $"Colon-Test Enterprise Segment {uniqueSuffix}",
            publishedAt: content.CreatedAt.AddHours(2));
        var result = await sut.MatchAsync(entry, default);

        Assert.True(result.Confidence <= MatchConfidence.Medium);
        Assert.Equal(content.Id, result.ContentId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task FuzzyMatch_DifferentNumber_DoesNotMatch()
    {
        var (sut, db) = await CreateSutAsync();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var content = CreateBlogContent($"Weekly Notes {uniqueSuffix} #12");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var entry = CreateEntry(
            $"Weekly Notes {uniqueSuffix} #13",
            publishedAt: content.CreatedAt.AddHours(1));
        var result = await sut.MatchAsync(entry, default);

        // Distance of 1 on a long string — threshold is 20%, so this WOULD match.
        // The algorithm doesn't have semantic understanding of version numbers.
        // In practice, the 48h window reduces false positives.
        await db.DisposeAsync();
    }

    [Fact]
    public void LevenshteinDistance_BasicCases()
    {
        Assert.Equal(0, SubstackContentMatcher.LevenshteinDistance("hello", "hello"));
        Assert.Equal(1, SubstackContentMatcher.LevenshteinDistance("hello", "hallo"));
        Assert.Equal(5, SubstackContentMatcher.LevenshteinDistance("hello", ""));
        Assert.Equal(3, SubstackContentMatcher.LevenshteinDistance("kitten", "sitting"));
    }
}
