using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

[Collection("Postgres")]
public class SubstackPrepServiceTests
{
    private readonly PostgresFixture _fixture;

    public SubstackPrepServiceTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(SubstackPrepService sut, ApplicationDbContext db)> CreateSutAsync()
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var sut = new SubstackPrepService(db, NullLogger<SubstackPrepService>.Instance);
        return (sut, db);
    }

    private static Content CreateBlogContent(string? title = "Test Blog Post", string body = "# Heading\n\nFirst paragraph of the blog post.\n\nSecond paragraph with more content.")
    {
        var content = Content.Create(ContentType.BlogPost, body, title, [PlatformType.Substack, PlatformType.PersonalBlog]);
        content.Metadata = new ContentMetadata { Tags = ["ai", "enterprise", "agents"] };
        return content;
    }

    [Fact]
    public async Task PrepareAsync_GeneratesAllSubstackFields()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Blog Post", result.Value!.Title);
        Assert.NotEmpty(result.Value.Body);
        Assert.NotEmpty(result.Value.PreviewText);
        Assert.Equal(3, result.Value.Tags.Length);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_ExtractsSubtitleFromFirstParagraph()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Contains("First paragraph", result.Value!.Subtitle);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_TruncatesPreviewTextAt200Chars()
    {
        var (sut, db) = await CreateSutAsync();
        var longBody = "# Title\n\n" + new string('A', 50) + " " + new string('B', 50) + " " + new string('C', 50) + " " + new string('D', 50) + " " + new string('E', 50) + " end.";
        var content = CreateBlogContent(body: longBody);
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.PreviewText.Length <= 210); // 200 + "..."
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_ProducesCleanMarkdown()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent(body: "# Title\n\n<div>HTML content</div>\n\nClean paragraph.");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("<div>", result.Value!.Body);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_ExtractsTagsFromMetadata()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Contains("ai", result.Value!.Tags);
        Assert.Contains("enterprise", result.Value.Tags);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_ReturnsNullCanonicalUrl_WhenBlogNotPublished()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CanonicalUrl);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_ReturnsFailure_ForNonExistentContent()
    {
        var (sut, db) = await CreateSutAsync();
        var result = await sut.PrepareAsync(Guid.NewGuid(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_HandlesContentWithNoMetadata()
    {
        var (sut, db) = await CreateSutAsync();
        var content = Content.Create(ContentType.BlogPost, "Simple body", "Simple Title");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.PrepareAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Tags);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MarkPublishedAsync_SetsSubstackPostUrlOnContent()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var url = "https://matthewkruczek.substack.com/p/test-post";
        var result = await sut.MarkPublishedAsync(content.Id, url, default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.WasAlreadyPublished);

        var updated = await db.Contents.FirstAsync(c => c.Id == content.Id);
        Assert.Equal(url, updated.SubstackPostUrl);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MarkPublishedAsync_CreatesSubstackDetectionRecord()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var url = $"https://matthewkruczek.substack.com/p/test-{Guid.NewGuid():N}";
        await sut.MarkPublishedAsync(content.Id, url, default);

        var detection = await db.SubstackDetections.FirstOrDefaultAsync(d => d.ContentId == content.Id);
        Assert.NotNull(detection);
        Assert.Equal(MatchConfidence.High, detection.Confidence);
        Assert.Equal(url, detection.SubstackUrl);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MarkPublishedAsync_TriggersNotification_WhenPersonalBlogInTargets()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var url = $"https://matthewkruczek.substack.com/p/notify-{Guid.NewGuid():N}";
        await sut.MarkPublishedAsync(content.Id, url, default);

        var notification = await db.UserNotifications.FirstOrDefaultAsync(n => n.ContentId == content.Id);
        Assert.NotNull(notification);
        Assert.Equal(nameof(NotificationType.BlogReady), notification.Type);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MarkPublishedAsync_IsIdempotent()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        content.SubstackPostUrl = "https://matthewkruczek.substack.com/p/existing";
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.MarkPublishedAsync(content.Id, "https://other.url", default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasAlreadyPublished);
        Assert.Equal("https://matthewkruczek.substack.com/p/existing", result.Value.SubstackPostUrl);
        await db.DisposeAsync();
    }
}
