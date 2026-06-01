namespace PBA.Infrastructure.Tests.Transformers;

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Transformers;

public class BlogFormatterTests
{
    private static readonly string TemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Transformers", "templates", "blog-post.template.html");

    private readonly BlogFormatter _formatter;

    public BlogFormatterTests()
    {
        var options = new BlogConnectorOptions
        {
            RepoPath = AppContext.BaseDirectory,
            TemplatePath = TemplatePath,
            Author = "Matt Kruczek",
            BaseUrl = "https://matthewkruczek.ai"
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<BlogConnectorOptions>>(
            o => o.CurrentValue == options);

        _formatter = new BlogFormatter(optionsMonitor, NullLogger<BlogFormatter>.Instance);
    }

    private static PreprocessedContent CreatePreprocessed(
        string body = "Test content",
        string title = "Test Title",
        string? contentType = "Agentic AI",
        DateTimeOffset? createdAt = null) =>
        new(
            Title: title,
            Body: body,
            CanonicalUrl: null,
            Tags: ["AI", "Dev"],
            Images: [],
            ContentType: contentType,
            CreatedAt: createdAt ?? new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero)
        );

    [Fact]
    public async Task FormatAsync_ConvertsMarkdownToHtml_ViaMarkdig()
    {
        var content = CreatePreprocessed("## Hello\n\nThis is **bold** text.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("<h2", result);
        Assert.Contains("Hello</h2>", result);
        Assert.Contains("<strong>bold</strong>", result);
    }

    [Fact]
    public async Task FormatAsync_IncludesGoogleAnalyticsId()
    {
        var content = CreatePreprocessed();

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("G-30EZ2BP22E", result);
    }

    [Fact]
    public async Task FormatAsync_IncludesOpenGraphAndTwitterMeta()
    {
        var content = CreatePreprocessed(title: "Agent-First Enterprise");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("property=\"og:title\" content=\"Agent-First Enterprise\"", result);
        Assert.Contains("property=\"og:type\" content=\"article\"", result);
        Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", result);
        Assert.Contains("name=\"twitter:title\" content=\"Agent-First Enterprise\"", result);
    }

    [Fact]
    public async Task FormatAsync_IncludesJsonLdArticleSchema_WithTitle()
    {
        var content = CreatePreprocessed(title: "Agent-First Enterprise");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("<script type=\"application/ld+json\">", result);
        Assert.Contains("\"@type\": \"Article\"", result);
        Assert.Contains("\"headline\": \"Agent-First Enterprise\"", result);
        Assert.Contains("\"name\": \"Matthew Kruczek\"", result);
    }

    [Fact]
    public async Task FormatAsync_BuildsCanonicalBlogUrl_FromSlug()
    {
        var content = CreatePreprocessed(title: "Agent-First Enterprise");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("https://matthewkruczek.ai/blog/agent-first-enterprise.html", result);
        Assert.Contains(
            "<link rel=\"canonical\" href=\"https://matthewkruczek.ai/blog/agent-first-enterprise.html\">",
            result);
    }

    [Fact]
    public async Task FormatAsync_BuildsHeroImageUrl_FromSlug()
    {
        var content = CreatePreprocessed(title: "Agent-First Enterprise");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains(
            "https://matthewkruczek.ai/assets/blog-images/agent-first-enterprise.png",
            result);
    }

    [Fact]
    public async Task FormatAsync_RendersBodyContentIntoArticle()
    {
        var content = CreatePreprocessed("This is the article body paragraph.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("This is the article body paragraph.", result);
    }

    [Fact]
    public async Task FormatAsync_LeavesNoUnresolvedPlaceholders()
    {
        var content = CreatePreprocessed("## Title\n\nSome body content with words.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.DoesNotContain("{{", result);
        Assert.DoesNotContain("}}", result);
    }

    [Fact]
    public async Task FormatAsync_ComputesReadingTime_FromWordCount()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 401));
        var content = CreatePreprocessed(body);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("3 min read", result);
    }

    [Fact]
    public async Task FormatAsync_BuildsDescription_FromBodyText()
    {
        var content = CreatePreprocessed("## Heading\n\nThis is the **lead paragraph** that becomes the description.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("name=\"description\"", result);
        Assert.Contains("lead paragraph", result);
        Assert.DoesNotContain("**", result);
    }

    [Fact]
    public async Task FormatAsync_TruncatesLongDescription_To155Chars()
    {
        var body = new string('a', 300);
        var content = CreatePreprocessed(body);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        var marker = "<meta name=\"description\" content=\"";
        var start = result.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = result.IndexOf('"', start);
        var description = result[start..end];

        Assert.EndsWith("...", description);
        Assert.True(description.Length <= 158, $"Description length was {description.Length}");
    }

    [Fact]
    public async Task FormatAsync_RendersDisplayAndIsoDates()
    {
        var content = CreatePreprocessed(createdAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("2026-01-02", result);
        Assert.Contains("January 2, 2026", result);
    }

    [Fact]
    public async Task FormatAsync_HandlesEmptyBody_ProducesValidHtml()
    {
        var content = CreatePreprocessed(body: "");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("<html", result);
        Assert.Contains("</html>", result);
        Assert.DoesNotContain("{{", result);
    }

    [Fact]
    public async Task FormatAsync_HandlesCodeBlocks_InMarkdown()
    {
        var content = CreatePreprocessed("```csharp\nvar x = 1;\n```");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("<pre>", result);
        Assert.Contains("<code", result);
    }

    [Fact]
    public async Task FormatAsync_HtmlEncodesTitle_PreventingXss()
    {
        var content = CreatePreprocessed(title: "<script>alert('xss')</script>");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void Platform_ReturnsBlog()
    {
        Assert.Equal(Platform.Blog, _formatter.Platform);
    }
}
