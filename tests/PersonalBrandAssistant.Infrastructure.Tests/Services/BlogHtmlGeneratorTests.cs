using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

[Collection("Postgres")]
public class BlogHtmlGeneratorTests
{
    private readonly PostgresFixture _fixture;

    public BlogHtmlGeneratorTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(BlogHtmlGenerator sut, ApplicationDbContext db)> CreateSutAsync(
        BlogPublishOptions? options = null)
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var opts = Options.Create(options ?? new BlogPublishOptions
        {
            ContentPath = "content/blog",
            TemplatePath = "nonexistent-template.html",
            AuthorName = "Matthew Kruczek"
        });
        var sut = new BlogHtmlGenerator(db, opts, NullLogger<BlogHtmlGenerator>.Instance);
        return (sut, db);
    }

    private static Content CreateBlogContent(
        string? title = "Test Blog Post",
        string body = "# Heading\n\nFirst paragraph of the blog post.\n\nSecond paragraph with more content.")
    {
        return Content.Create(ContentType.BlogPost, body, title, [PlatformType.Substack, PlatformType.PersonalBlog]);
    }

    [Fact]
    public async Task GenerateAsync_RendersContentIntoHtmlTemplate()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Contains("<html", result.Value!.Html);
        Assert.Contains("</html>", result.Value.Html);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_InjectsTitleDateAuthorMetaDescription()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var saved = await db.Contents.FirstAsync(c => c.Id == content.Id);
        var expectedDate = saved.CreatedAt.ToString("yyyy-MM-dd");

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        var html = result.Value!.Html;
        Assert.Contains("Test Blog Post", html);
        Assert.Contains(expectedDate, html);
        Assert.Contains("Matthew Kruczek", html);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_InjectsOpenGraphTags()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        var html = result.Value!.Html;
        Assert.Contains("og:title", html);
        Assert.Contains("og:description", html);
        Assert.Contains("Test Blog Post", html);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_SetsCanonicalUrlToSubstackUrl_WhenAvailable()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        content.SubstackPostUrl = "https://matthewkruczek.substack.com/p/test-post";
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://matthewkruczek.substack.com/p/test-post", result.Value!.CanonicalUrl);
        Assert.Contains("https://matthewkruczek.substack.com/p/test-post", result.Value.Html);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_UsesPlaceholderCanonicalUrl_WhenSubstackUrlNotKnown()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CanonicalUrl);
        Assert.Contains("canonical", result.Value.Html.ToLowerInvariant());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_ConvertsMarkdownToHtml_WithRawHtmlDisabled()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent(body: "# Title\n\n<script>alert('xss')</script>\n\nSafe paragraph.");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("<script>", result.Value!.Html);
        Assert.Contains("Safe paragraph", result.Value.Html);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_ProducesCorrectFilePath()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        var expectedHash = content.Id.ToString("N")[^6..];
        var expectedDate = content.CreatedAt.ToString("yyyy-MM-dd");
        Assert.Contains(expectedDate, result.Value!.FilePath);
        Assert.Contains("test-blog-post", result.Value.FilePath);
        Assert.Contains(expectedHash, result.Value.FilePath);
        Assert.EndsWith(".html", result.Value.FilePath);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_AppendsContentIdHashSuffixForUniqueness()
    {
        var (sut, db) = await CreateSutAsync();
        var content1 = CreateBlogContent();
        var content2 = CreateBlogContent();
        db.Contents.Add(content1);
        db.Contents.Add(content2);
        await db.SaveChangesAsync();

        var result1 = await sut.GenerateAsync(content1.Id, default);
        var result2 = await sut.GenerateAsync(content2.Id, default);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotEqual(result1.Value!.FilePath, result2.Value!.FilePath);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_HandlesSpecialCharactersInTitle()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent(title: "What's Next? AI & the Future");
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result = await sut.GenerateAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Contains("whats-next-ai-the-future", result.Value!.FilePath);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_RegeneratesWithUpdatedCanonicalUrl()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var result1 = await sut.GenerateAsync(content.Id, default);
        Assert.Null(result1.Value!.CanonicalUrl);

        content.SubstackPostUrl = "https://matthewkruczek.substack.com/p/updated";
        await db.SaveChangesAsync();

        var result2 = await sut.GenerateAsync(content.Id, default);
        Assert.Equal("https://matthewkruczek.substack.com/p/updated", result2.Value!.CanonicalUrl);
        Assert.Contains("https://matthewkruczek.substack.com/p/updated", result2.Value.Html);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNotFound_ForNonExistentContent()
    {
        var (sut, db) = await CreateSutAsync();
        var result = await sut.GenerateAsync(Guid.NewGuid(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("Agent-First Enterprise: Part 5", "agent-first-enterprise-part-5")]
    [InlineData("What's Next? AI & the Future", "whats-next-ai-the-future")]
    [InlineData("Multiple---Dashes", "multiple-dashes")]
    public void GenerateSlug_ProducesExpectedOutput(string input, string expected)
    {
        var result = BlogHtmlGenerator.GenerateSlug(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateSlug_FallsBackToUntitled_ForEmptyInput(string? input)
    {
        var result = BlogHtmlGenerator.GenerateSlug(input);
        Assert.Equal("untitled", result);
    }
}
