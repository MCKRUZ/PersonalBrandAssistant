namespace PBA.Infrastructure.Tests.Transformers;

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Transformers;

public class BlogFormatterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BlogFormatter _formatter;

    public BlogFormatterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"blog-fmt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var templatePath = Path.Combine(_tempDir, "template.html");
        File.WriteAllText(templatePath, """
            <!DOCTYPE html>
            <html>
            <head><title>{{title}}</title></head>
            <body>
            <h1>{{title}}</h1>
            <p>Author: {{author}} | Date: {{date}} | Category: {{category}}</p>
            <p>Tags: {{tags}}</p>
            <div>{{content}}</div>
            </body>
            </html>
            """);

        var options = new BlogConnectorOptions
        {
            RepoPath = _tempDir,
            TemplatePath = templatePath,
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
        string? contentType = "Blog",
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
    public async Task FormatAsync_AppliesHtmlTemplate_WithTokenReplacement()
    {
        var content = CreatePreprocessed(title: "Test Post", contentType: "Blog");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("<title>Test Post</title>", result);
        Assert.Contains("AI, Dev", result);
        Assert.Contains("Matt Kruczek", result);
        Assert.Contains("Category: Blog", result);
        Assert.Contains("2026-05-27", result);
    }

    [Fact]
    public async Task FormatAsync_HandlesEmptyBody_ProducesValidHtml()
    {
        var content = CreatePreprocessed(body: "");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("<html>", result);
        Assert.Contains("</html>", result);
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

        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public async Task FormatAsync_UsesCreatedAtDate_NotCurrentDate()
    {
        var pastDate = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var content = CreatePreprocessed(createdAt: pastDate);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("2025-01-15", result);
    }

    [Fact]
    public void Platform_ReturnsBlog()
    {
        Assert.Equal(Platform.Blog, _formatter.Platform);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }
}
