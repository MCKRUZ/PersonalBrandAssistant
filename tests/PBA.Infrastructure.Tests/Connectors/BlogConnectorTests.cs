using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class BlogConnectorTests : IDisposable
{
    private readonly Mock<IProcessRunner> _processRunner = new();
    private readonly Mock<IHeroImageGenerator> _heroImageGenerator = new();
    private readonly IBlogIndexUpdater _indexUpdater = new BlogIndexUpdater();
    private readonly Mock<ILogger<BlogConnector>> _logger = new();
    private readonly string _tempDir;
    private readonly BlogConnectorOptions _options;

    public BlogConnectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"blog-connector-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _options = new BlogConnectorOptions
        {
            RepoPath = _tempDir,
            TemplatePath = Path.Combine(_tempDir, "template.html"),
            Author = "Matt Kruczek",
            RemoteName = "origin",
            Branch = "main",
            BaseUrl = "https://matthewkruczek.ai"
        };

        _processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        // Hero generation succeeds by default; individual tests override for failure cases.
        _heroImageGenerator
            .Setup(h => h.GenerateAsync(It.IsAny<BlogPostMeta>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlogPostMeta m, CancellationToken _) =>
            {
                var imagePath = Path.Combine(_tempDir, "assets", "blog-images", $"{m.Slug}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47]);
                return Result<string>.Success(imagePath);
            });
    }

    private BlogConnector CreateConnector() =>
        new(_processRunner.Object, _indexUpdater, _heroImageGenerator.Object, CreateOptionsMonitor(), _logger.Object);

    private IOptionsMonitor<BlogConnectorOptions> CreateOptionsMonitor()
    {
        var monitor = new Mock<IOptionsMonitor<BlogConnectorOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(_options);
        return monitor.Object;
    }

    private void SeedIndexFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "blog.html"), BlogHtmlSeed);
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), IndexHtmlSeed);
        File.WriteAllText(Path.Combine(_tempDir, "sitemap.xml"), SitemapSeed);
    }

    private const string BlogHtmlSeed = """
        <html><body>
        <section class="featured-post">
            <div class="featured-post-content">
                <span class="blog-card-category">Enterprise AI</span>
                <h2><a href="blog/existing-featured.html">Existing Featured</a></h2>
                <p>An older featured post.</p>
                <div class="blog-card-meta">
                    <span class="blog-card-date">January 1, 2026</span>
                </div>
            </div>
            <div class="featured-post-image">
                <img src="assets/blog-images/existing-featured.png" alt="Existing Featured">
            </div>
        </section>
        <div class="blog-list-grid" id="blogGrid">
            <!-- Article: Original Card -->
            <article class="blog-card" data-category="enterprise-ai">
                <h3><a href="blog/original-card.html">Original Card</a></h3>
            </article>
        </div>
        </body></html>
        """;

    private const string IndexHtmlSeed = """
        <html><body>
        <div class="blog-grid">
            <article class="blog-card">
                <h3><a href="blog/homepage-existing.html">Homepage Existing</a></h3>
            </article>
        </div>
        <a href="blog.html">View All 54 Articles</a>
        </body></html>
        """;

    private const string SitemapSeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <url>
            <loc>https://matthewkruczek.ai/</loc>
            <priority>1.0</priority>
          </url>
        </urlset>
        """;

    private static Content CreateTestContent(string title = "My Test Post") =>
        new()
        {
            Title = title,
            Body = "Some content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Tags = ["AI", "Engineering"],
            CreatedAt = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero)
        };

    private static PlatformPublishRequest CreatePublishRequest(
        string title = "My Test Post",
        string transformedContent = "<h2>Hello</h2><p>This is <strong>bold</strong> text.</p>") =>
        new(
            Content: CreateTestContent(title),
            TransformedContent: transformedContent,
            Tags: ["AI", "Engineering"],
            CanonicalUrl: null,
            Mode: PublishMode.Publish,
            ScheduledAt: null
        );

    private List<string> GitCalls() =>
        _processRunner.Invocations
            .Where(i => i.Method.Name == nameof(IProcessRunner.RunAsync))
            .Select(i => (string)i.Arguments[1])
            .ToList();

    [Fact]
    public void ImplementsIPlatformConnector()
    {
        var connector = CreateConnector();
        Assert.IsAssignableFrom<IPlatformConnector>(connector);
    }

    [Fact]
    public void Platform_ReturnsBlog()
    {
        var connector = CreateConnector();
        Assert.Equal(Platform.Blog, connector.Platform);
    }

    [Fact]
    public async Task PublishAsync_UsesTransformedContentDirectly()
    {
        var connector = CreateConnector();
        var html = "<html><body><h1>Pre-rendered</h1></body></html>";
        var request = CreatePublishRequest(transformedContent: html);

        await connector.PublishAsync(request, CancellationToken.None);

        var outputFile = Path.Combine(_tempDir, "blog", "my-test-post.html");
        var result = await File.ReadAllTextAsync(outputFile);
        Assert.Equal(html, result);
    }

    [Fact]
    public async Task PublishAsync_WritesPostToBlogDirectory_NotPosts()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        await connector.PublishAsync(request, CancellationToken.None);

        var expectedPath = Path.Combine(_tempDir, "blog", "my-test-post.html");
        Assert.True(File.Exists(expectedPath));

        var oldPath = Path.Combine(_tempDir, "posts", "my-test-post.html");
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public async Task PublishAsync_ReturnsSuccessWithBlogUrl()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://matthewkruczek.ai/blog/my-test-post", result.PublishedUrl);
        Assert.EndsWith("/blog/my-test-post", result.PublishedUrl);
        Assert.Equal("my-test-post", result.PlatformPostId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_WeavesNewPostIntoBlogListing_PreservingOriginalCard()
    {
        SeedIndexFiles();
        var connector = CreateConnector();
        var request = CreatePublishRequest(title: "Brand New Article");

        await connector.PublishAsync(request, CancellationToken.None);

        var blogHtml = await File.ReadAllTextAsync(Path.Combine(_tempDir, "blog.html"));

        // New post is now the featured article.
        Assert.Contains("blog/brand-new-article.html", blogHtml);
        Assert.Contains("Brand New Article", blogHtml);
        // Original grid card is preserved.
        Assert.Contains("blog/original-card.html", blogHtml);
        // Previously featured post is demoted, not lost.
        Assert.Contains("blog/existing-featured.html", blogHtml);
    }

    [Fact]
    public async Task PublishAsync_AddsNewPostLocToSitemap()
    {
        SeedIndexFiles();
        var connector = CreateConnector();
        var request = CreatePublishRequest(title: "Brand New Article");

        await connector.PublishAsync(request, CancellationToken.None);

        var sitemap = await File.ReadAllTextAsync(Path.Combine(_tempDir, "sitemap.xml"));
        Assert.Contains("https://matthewkruczek.ai/blog/brand-new-article.html", sitemap);
    }

    [Fact]
    public async Task PublishAsync_IncrementsHomepageArticleCount()
    {
        SeedIndexFiles();
        var connector = CreateConnector();
        var request = CreatePublishRequest(title: "Brand New Article");

        await connector.PublishAsync(request, CancellationToken.None);

        var indexHtml = await File.ReadAllTextAsync(Path.Combine(_tempDir, "index.html"));
        Assert.Contains("View All 55 Articles", indexHtml);
        Assert.Contains("blog/brand-new-article.html", indexHtml);
    }

    [Fact]
    public async Task PublishAsync_MissingIndexFiles_DoesNotFail()
    {
        // No SeedIndexFiles() call: blog.html/index.html/sitemap.xml are absent.
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_tempDir, "blog", "my-test-post.html")));
    }

    [Fact]
    public async Task PublishAsync_RunsGitAddCommitPushInOrder()
    {
        SeedIndexFiles();
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        await connector.PublishAsync(request, CancellationToken.None);

        var calls = GitCalls();

        var commitIndex = calls.FindIndex(c => c.Contains("commit --file=-"));
        var pushIndex = calls.FindIndex(c => c.Contains("push origin main"));

        // At least one add precedes the commit, which precedes the push.
        Assert.Contains(calls, c => c.Contains("add "));
        Assert.True(commitIndex >= 0);
        Assert.True(pushIndex >= 0);
        Assert.True(calls.FindIndex(c => c.Contains("add ")) < commitIndex);
        Assert.True(commitIndex < pushIndex);

        // The post file is staged.
        Assert.Contains(calls, c => c.Contains("add \"blog/my-test-post.html\""));
    }

    [Fact]
    public async Task PublishAsync_StagesIndexAndHeroFilesThatExist()
    {
        SeedIndexFiles();
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        await connector.PublishAsync(request, CancellationToken.None);

        var calls = GitCalls();
        Assert.Contains(calls, c => c.Contains("add \"blog/my-test-post.html\""));
        Assert.Contains(calls, c => c.Contains("add \"blog.html\""));
        Assert.Contains(calls, c => c.Contains("add \"index.html\""));
        Assert.Contains(calls, c => c.Contains("add \"sitemap.xml\""));
        Assert.Contains(calls, c => c.Contains("add \"assets/blog-images/my-test-post.png\""));
    }

    [Fact]
    public async Task PublishAsync_HeroGenerationFailure_DoesNotFailPublish()
    {
        _heroImageGenerator
            .Setup(h => h.GenerateAsync(It.IsAny<BlogPostMeta>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Fail("ComfyUI unreachable"));

        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_tempDir, "blog", "my-test-post.html")));
        // No hero image on disk, so it must not be staged.
        Assert.DoesNotContain(GitCalls(), c => c.Contains("blog-images"));
    }

    [Fact]
    public async Task PublishAsync_HeroGenerationThrows_DoesNotFailPublish()
    {
        _heroImageGenerator
            .Setup(h => h.GenerateAsync(It.IsAny<BlogPostMeta>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task PublishAsync_CommitUsesStdinToAvoidInjection()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest(title: "Title with \"quotes\" and $(cmd)");

        await connector.PublishAsync(request, CancellationToken.None);

        _processRunner.Verify(
            p => p.RunAsync("git",
                It.Is<string>(s => s.Contains("commit --file=-")),
                It.Is<string?>(s => s != null && s.Contains("Title with \"quotes\" and $(cmd)")),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishAsync_SetsWorkingDirectory()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        await connector.PublishAsync(request, CancellationToken.None);

        _processRunner.Verify(
            p => p.RunAsync("git", It.Is<string>(s => s.Contains($"-C \"{_tempDir}\"")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        Assert.All(GitCalls(), c => Assert.Contains($"-C \"{_tempDir}\"", c));
    }

    [Fact]
    public async Task PublishAsync_ReturnsFailureOnGitPushError()
    {
        _processRunner
            .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("push")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "fatal: remote rejected"));

        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fatal: remote rejected", result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_ReturnsFailureOnGitAddError()
    {
        _processRunner
            .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("add")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "fatal: not a git repository"));

        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fatal: not a git repository", result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_EmptyTitle_ThrowsArgumentException()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest(title: "   ");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.PublishAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_EmptyTransformedContent_ThrowsArgumentException()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest(transformedContent: "   ");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.PublishAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ReturnsTrueWhenRepoExists()
    {
        var connector = CreateConnector();
        Assert.True(await connector.ValidateCredentialsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ReturnsFalseWhenRepoMissing()
    {
        var badOptions = new BlogConnectorOptions
        {
            RepoPath = Path.Combine(_tempDir, "nonexistent"),
            TemplatePath = "unused"
        };
        var monitor = new Mock<IOptionsMonitor<BlogConnectorOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(badOptions);

        var connector = new BlogConnector(_processRunner.Object, _indexUpdater, _heroImageGenerator.Object, monitor.Object, _logger.Object);
        Assert.False(await connector.ValidateCredentialsAsync(CancellationToken.None));
    }

    [Fact]
    public void GetCapabilities_ReturnsCorrectValues()
    {
        var connector = CreateConnector();
        var caps = connector.GetCapabilities();

        Assert.Equal(int.MaxValue, caps.MaxCharacters);
        Assert.False(caps.SupportsMarkdown);
        Assert.True(caps.SupportsHtml);
        Assert.True(caps.SupportsImages);
        Assert.False(caps.SupportsScheduling);
        Assert.False(caps.SupportsThreads);
    }

    [Theory]
    [InlineData("My Blog Post! (Part 2)", "my-blog-post-part-2")]
    [InlineData("Hello World", "hello-world")]
    [InlineData("C# & .NET Tips", "c-net-tips")]
    [InlineData("  Spaced  Out  ", "spaced-out")]
    [InlineData("---dashes---", "dashes")]
    public void GenerateSlug_ProducesExpectedOutput(string title, string expected)
    {
        var slug = BlogConnector.GenerateSlug(title);
        Assert.Equal(expected, slug);
    }

    [Fact]
    public void GenerateSlug_AllSpecialChars_ReturnsEmpty()
    {
        var slug = BlogConnector.GenerateSlug("!!!");
        Assert.Equal(string.Empty, slug);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
