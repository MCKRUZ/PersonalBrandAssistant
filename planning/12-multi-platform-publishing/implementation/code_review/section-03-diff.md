diff --git a/src/PBA.Application/Common/Models/PreprocessedContent.cs b/src/PBA.Application/Common/Models/PreprocessedContent.cs
index aba841e..4bb6b8f 100644
--- a/src/PBA.Application/Common/Models/PreprocessedContent.cs
+++ b/src/PBA.Application/Common/Models/PreprocessedContent.cs
@@ -5,5 +5,6 @@ public record PreprocessedContent(
     string Body,
     string? CanonicalUrl,
     IReadOnlyList<string> Tags,
-    IReadOnlyList<ImageReference> Images
+    IReadOnlyList<ImageReference> Images,
+    string? ContentType = null
 );
diff --git a/src/PBA.Infrastructure/Transformers/BlogFormatter.cs b/src/PBA.Infrastructure/Transformers/BlogFormatter.cs
new file mode 100644
index 0000000..9339ac3
--- /dev/null
+++ b/src/PBA.Infrastructure/Transformers/BlogFormatter.cs
@@ -0,0 +1,43 @@
+using Markdig;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+
+namespace PBA.Infrastructure.Transformers;
+
+public sealed class BlogFormatter : IPlatformFormatter
+{
+    private readonly IOptionsMonitor<BlogConnectorOptions> _options;
+    private readonly ILogger<BlogFormatter> _logger;
+    private readonly MarkdownPipeline _pipeline;
+
+    public BlogFormatter(
+        IOptionsMonitor<BlogConnectorOptions> options,
+        ILogger<BlogFormatter> logger)
+    {
+        _options = options;
+        _logger = logger;
+        _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
+    }
+
+    public Platform Platform => Platform.Blog;
+
+    public async Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
+    {
+        var opts = _options.CurrentValue;
+
+        var template = await File.ReadAllTextAsync(opts.TemplatePath, ct);
+        var html = Markdown.ToHtml(content.Body, _pipeline);
+
+        return template
+            .Replace("{{title}}", content.Title)
+            .Replace("{{content}}", html)
+            .Replace("{{date}}", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"))
+            .Replace("{{author}}", opts.Author)
+            .Replace("{{tags}}", string.Join(", ", content.Tags))
+            .Replace("{{category}}", content.ContentType ?? string.Empty);
+    }
+}
diff --git a/src/PBA.Infrastructure/Transformers/ContentTransformer.cs b/src/PBA.Infrastructure/Transformers/ContentTransformer.cs
new file mode 100644
index 0000000..ac995eb
--- /dev/null
+++ b/src/PBA.Infrastructure/Transformers/ContentTransformer.cs
@@ -0,0 +1,109 @@
+using System.Text.RegularExpressions;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+
+namespace PBA.Infrastructure.Transformers;
+
+public sealed partial class ContentTransformer : IContentTransformer
+{
+    private readonly IServiceProvider _serviceProvider;
+    private readonly IOptionsMonitor<BlogConnectorOptions> _options;
+    private readonly ILogger<ContentTransformer> _logger;
+
+    public ContentTransformer(
+        IServiceProvider serviceProvider,
+        IOptionsMonitor<BlogConnectorOptions> options,
+        ILogger<ContentTransformer> logger)
+    {
+        _serviceProvider = serviceProvider;
+        _options = options;
+        _logger = logger;
+    }
+
+    public async Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct)
+    {
+        var preprocessed = Preprocess(content);
+
+        var formatter = _serviceProvider.GetKeyedService<IPlatformFormatter>(platform)
+            ?? throw new NotSupportedException($"No formatter registered for platform: {platform}");
+
+        _logger.LogDebug("Transforming content '{Title}' for {Platform}", content.Title, platform);
+
+        return await formatter.FormatAsync(preprocessed, ct);
+    }
+
+    private PreprocessedContent Preprocess(Content content)
+    {
+        var body = StripFrontmatter(content.Body);
+        var baseUrl = _options.CurrentValue.BaseUrl;
+        var (resolvedBody, images) = ResolveImagePaths(body, baseUrl);
+
+        return new PreprocessedContent(
+            Title: content.Title,
+            Body: resolvedBody,
+            CanonicalUrl: null,
+            Tags: content.Tags.AsReadOnly(),
+            Images: images,
+            ContentType: content.ContentType.ToString()
+        );
+    }
+
+    internal static string StripFrontmatter(string body)
+    {
+        if (string.IsNullOrEmpty(body))
+            return body;
+
+        var trimmed = body.TrimStart();
+        if (!trimmed.StartsWith("---"))
+            return body;
+
+        var endIndex = trimmed.IndexOf("---", 3);
+        if (endIndex < 0)
+            return body;
+
+        return trimmed[(endIndex + 3)..].TrimStart('\r', '\n');
+    }
+
+    internal static (string body, IReadOnlyList<ImageReference> images) ResolveImagePaths(
+        string body, string baseUrl)
+    {
+        var images = new List<ImageReference>();
+
+        if (string.IsNullOrEmpty(body))
+            return (body, images);
+
+        var resolvedBody = ImagePattern().Replace(body, match =>
+        {
+            var altText = match.Groups[1].Value;
+            var originalPath = match.Groups[2].Value;
+
+            string absoluteUrl;
+            if (originalPath.StartsWith("http://") || originalPath.StartsWith("https://"))
+            {
+                absoluteUrl = originalPath;
+            }
+            else
+            {
+                absoluteUrl = $"{baseUrl.TrimEnd('/')}/{originalPath.TrimStart('/')}";
+            }
+
+            images.Add(new ImageReference(
+                originalPath,
+                absoluteUrl,
+                string.IsNullOrEmpty(altText) ? null : altText));
+
+            return $"![{altText}]({absoluteUrl})";
+        });
+
+        return (resolvedBody, images);
+    }
+
+    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
+    private static partial Regex ImagePattern();
+}
diff --git a/tests/PBA.Infrastructure.Tests/Transformers/BlogFormatterTests.cs b/tests/PBA.Infrastructure.Tests/Transformers/BlogFormatterTests.cs
new file mode 100644
index 0000000..e9b4bfc
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Transformers/BlogFormatterTests.cs
@@ -0,0 +1,122 @@
+namespace PBA.Infrastructure.Tests.Transformers;
+
+using Microsoft.Extensions.Logging.Abstractions;
+using Xunit;
+using Microsoft.Extensions.Options;
+using Moq;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Transformers;
+
+public class BlogFormatterTests : IDisposable
+{
+    private readonly string _tempDir;
+    private readonly BlogFormatter _formatter;
+
+    public BlogFormatterTests()
+    {
+        _tempDir = Path.Combine(Path.GetTempPath(), $"blog-fmt-{Guid.NewGuid():N}");
+        Directory.CreateDirectory(_tempDir);
+
+        var templatePath = Path.Combine(_tempDir, "template.html");
+        File.WriteAllText(templatePath, """
+            <!DOCTYPE html>
+            <html>
+            <head><title>{{title}}</title></head>
+            <body>
+            <h1>{{title}}</h1>
+            <p>Author: {{author}} | Date: {{date}} | Category: {{category}}</p>
+            <p>Tags: {{tags}}</p>
+            <div>{{content}}</div>
+            </body>
+            </html>
+            """);
+
+        var options = new BlogConnectorOptions
+        {
+            RepoPath = _tempDir,
+            TemplatePath = templatePath,
+            Author = "Matt Kruczek",
+            BaseUrl = "https://matthewkruczek.ai"
+        };
+
+        var optionsMonitor = Mock.Of<IOptionsMonitor<BlogConnectorOptions>>(
+            o => o.CurrentValue == options);
+
+        _formatter = new BlogFormatter(optionsMonitor, NullLogger<BlogFormatter>.Instance);
+    }
+
+    private static PreprocessedContent CreatePreprocessed(
+        string body = "Test content",
+        string title = "Test Title",
+        string? contentType = "Blog") =>
+        new(
+            Title: title,
+            Body: body,
+            CanonicalUrl: null,
+            Tags: ["AI", "Dev"],
+            Images: [],
+            ContentType: contentType
+        );
+
+    [Fact]
+    public async Task FormatAsync_ConvertsMarkdownToHtml_ViaMarkdig()
+    {
+        var content = CreatePreprocessed("## Hello\n\nThis is **bold** text.");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("<h2", result);
+        Assert.Contains("Hello</h2>", result);
+        Assert.Contains("<strong>bold</strong>", result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_AppliesHtmlTemplate_WithTokenReplacement()
+    {
+        var content = CreatePreprocessed(title: "Test Post", contentType: "Blog");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("<title>Test Post</title>", result);
+        Assert.Contains("AI, Dev", result);
+        Assert.Contains("Matt Kruczek", result);
+        Assert.Contains("Category: Blog", result);
+        Assert.Contains(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_HandlesEmptyBody_ProducesValidHtml()
+    {
+        var content = CreatePreprocessed(body: "");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("<html>", result);
+        Assert.Contains("</html>", result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_HandlesCodeBlocks_InMarkdown()
+    {
+        var content = CreatePreprocessed("```csharp\nvar x = 1;\n```");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("<pre>", result);
+        Assert.Contains("<code", result);
+    }
+
+    [Fact]
+    public void Platform_ReturnsBlog()
+    {
+        Assert.Equal(Platform.Blog, _formatter.Platform);
+    }
+
+    public void Dispose()
+    {
+        try { Directory.Delete(_tempDir, recursive: true); }
+        catch { /* cleanup best-effort */ }
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Transformers/ContentTransformerTests.cs b/tests/PBA.Infrastructure.Tests/Transformers/ContentTransformerTests.cs
new file mode 100644
index 0000000..5bcec78
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Transformers/ContentTransformerTests.cs
@@ -0,0 +1,156 @@
+namespace PBA.Infrastructure.Tests.Transformers;
+
+using Microsoft.Extensions.DependencyInjection;
+using Xunit;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Transformers;
+
+public class ContentTransformerTests
+{
+    private readonly Mock<IPlatformFormatter> _mockFormatter = new();
+    private readonly BlogConnectorOptions _options = new()
+    {
+        RepoPath = "/tmp/repo",
+        TemplatePath = "/tmp/template.html",
+        BaseUrl = "https://matthewkruczek.ai"
+    };
+
+    private ContentTransformer CreateTransformer(Platform? registerForPlatform = null)
+    {
+        var services = new ServiceCollection();
+
+        if (registerForPlatform.HasValue)
+        {
+            services.AddKeyedSingleton<IPlatformFormatter>(
+                registerForPlatform.Value, (_, _) => _mockFormatter.Object);
+        }
+
+        var provider = services.BuildServiceProvider();
+        var optionsMonitor = Mock.Of<IOptionsMonitor<BlogConnectorOptions>>(
+            o => o.CurrentValue == _options);
+
+        return new ContentTransformer(provider, optionsMonitor, NullLogger<ContentTransformer>.Instance);
+    }
+
+    private static Content CreateContent(string body = "Test content", string title = "Test Title") =>
+        new()
+        {
+            Title = title,
+            Body = body,
+            ContentType = ContentType.Blog,
+            Tags = ["AI", "Tech"]
+        };
+
+    [Fact]
+    public async Task TransformAsync_WithBlogPlatform_DelegatesToBlogFormatter()
+    {
+        _mockFormatter
+            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync("formatted output");
+
+        var transformer = CreateTransformer(Platform.Blog);
+        var result = await transformer.TransformAsync(CreateContent(), Platform.Blog, CancellationToken.None);
+
+        Assert.Equal("formatted output", result);
+        _mockFormatter.Verify(
+            f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task TransformAsync_WithUnregisteredPlatform_ThrowsNotSupportedException()
+    {
+        var transformer = CreateTransformer();
+
+        await Assert.ThrowsAsync<NotSupportedException>(
+            () => transformer.TransformAsync(CreateContent(), Platform.Reddit, CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task TransformAsync_StripsFrontmatter_BeforeDelegating()
+    {
+        PreprocessedContent? captured = null;
+        _mockFormatter
+            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
+            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
+            .ReturnsAsync("ok");
+
+        var content = CreateContent("---\ntitle: Hello\ntags: [a, b]\n---\n\nActual content here.");
+        var transformer = CreateTransformer(Platform.Blog);
+
+        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.DoesNotContain("---", captured.Body);
+        Assert.DoesNotContain("title: Hello", captured.Body);
+        Assert.StartsWith("Actual content here.", captured.Body);
+    }
+
+    [Fact]
+    public async Task TransformAsync_ResolvesRelativeImagePaths_ToAbsoluteUrls()
+    {
+        PreprocessedContent? captured = null;
+        _mockFormatter
+            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
+            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
+            .ReturnsAsync("ok");
+
+        var content = CreateContent("![alt](images/photo.png)\n![alt2](/images/other.jpg)");
+        var transformer = CreateTransformer(Platform.Blog);
+
+        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal(2, captured.Images.Count);
+        Assert.All(captured.Images, img => Assert.StartsWith("https://", img.AbsoluteUrl));
+        Assert.Contains("https://matthewkruczek.ai/images/photo.png", captured.Body);
+        Assert.Contains("https://matthewkruczek.ai/images/other.jpg", captured.Body);
+    }
+
+    [Fact]
+    public async Task TransformAsync_PreservesContentMetadata_InPreprocessedContent()
+    {
+        PreprocessedContent? captured = null;
+        _mockFormatter
+            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
+            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
+            .ReturnsAsync("ok");
+
+        var content = CreateContent(title: "My Title");
+        content.Tags = ["AI", "Tech"];
+        var transformer = CreateTransformer(Platform.Blog);
+
+        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal("My Title", captured.Title);
+        Assert.Contains("AI", captured.Tags);
+        Assert.Contains("Tech", captured.Tags);
+        Assert.Equal("Blog", captured.ContentType);
+    }
+
+    [Fact]
+    public async Task TransformAsync_EmptyBody_PassesEmptyToFormatter()
+    {
+        PreprocessedContent? captured = null;
+        _mockFormatter
+            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
+            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
+            .ReturnsAsync("ok");
+
+        var content = CreateContent(body: "");
+        var transformer = CreateTransformer(Platform.Blog);
+
+        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal("", captured.Body);
+    }
+}
