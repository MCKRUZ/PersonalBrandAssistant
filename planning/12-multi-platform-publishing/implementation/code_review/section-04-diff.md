diff --git a/CLAUDE.md b/CLAUDE.md
index 3cf74af..aa0bc93 100644
--- a/CLAUDE.md
+++ b/CLAUDE.md
@@ -41,7 +41,7 @@ Follow global rules in `~/.claude/rules/coding-style.md`:
 ## Nexus Intelligence
 
 *Auto-updated by Nexus — do not edit this section manually.*
-*Last sync: 2026-05-26*
+*Last sync: 2026-05-27*
 
 ### Portfolio
 | Project | Description | Tech |
@@ -55,6 +55,15 @@ Follow global rules in `~/.claude/rules/coding-style.md`:
 | _+34 inactive_ | — | — |
 
 ### Project Context
+#### Deployment: Local Docker on Mac Mini
+## PBA Deployment
+
+- **Host:** Mac Mini (192.168.50.103)
+- **Runtime:** Docker Compose
+- **Branch deployed:** v2-rebuild
+- **Platform:** Apple Silico…
+*Tags: deployment, docker, mac-mini, infrastructure, tailscale*
+
 #### Deployment: Local Docker on Furious
 ## PBA Deployment
 
diff --git a/src/PBA.Application/Common/Interfaces/IBlogConnector.cs b/src/PBA.Application/Common/Interfaces/IBlogConnector.cs
deleted file mode 100644
index 0cf6f08..0000000
--- a/src/PBA.Application/Common/Interfaces/IBlogConnector.cs
+++ /dev/null
@@ -1,8 +0,0 @@
-using PBA.Domain.Entities;
-
-namespace PBA.Application.Common.Interfaces;
-
-public interface IBlogConnector
-{
-    Task<string> PublishAsync(Content content, CancellationToken ct);
-}
diff --git a/src/PBA.Application/Features/Content/Commands/PublishContent.cs b/src/PBA.Application/Features/Content/Commands/PublishContent.cs
index 48b56de..62bb488 100644
--- a/src/PBA.Application/Features/Content/Commands/PublishContent.cs
+++ b/src/PBA.Application/Features/Content/Commands/PublishContent.cs
@@ -1,5 +1,7 @@
 using MediatR;
+using Microsoft.Extensions.DependencyInjection;
 using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
 using PBA.Application.Features.ContentStudio;
 using PBA.Domain.Common;
 using PBA.Domain.Entities;
@@ -13,7 +15,7 @@ public static class PublishContent
 
     internal sealed class Handler(
         IAppDbContext db,
-        IBlogConnector blogConnector) : IRequestHandler<Command, Result>
+        [FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector) : IRequestHandler<Command, Result>
     {
         public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
         {
@@ -31,17 +33,20 @@ public static class PublishContent
                 return Result.Fail("Cannot publish content in its current status");
             }
 
-            string? publishedUrl = null;
+            PlatformPublishResult? result = null;
             if (content.PrimaryPlatform == Platform.Blog)
             {
-                try
-                {
-                    publishedUrl = await blogConnector.PublishAsync(content, cancellationToken);
-                }
-                catch (Exception)
-                {
-                    return Result.Fail("Failed to publish to blog platform");
-                }
+                var publishRequest = new PlatformPublishRequest(
+                    Content: content,
+                    TransformedContent: content.Body,
+                    Tags: content.Tags.AsReadOnly(),
+                    CanonicalUrl: null,
+                    Mode: PublishMode.Publish,
+                    ScheduledAt: null);
+
+                result = await blogConnector.PublishAsync(publishRequest, cancellationToken);
+                if (!result.Success)
+                    return Result.Fail(result.ErrorMessage ?? "Failed to publish to blog platform");
             }
 
             db.ContentPlatformPublishes.Add(new ContentPlatformPublish
@@ -49,7 +54,7 @@ public static class PublishContent
                 ContentId = request.ContentId,
                 Platform = content.PrimaryPlatform,
                 Status = PublishStatus.Published,
-                PublishedUrl = publishedUrl,
+                PublishedUrl = result?.PublishedUrl,
                 PublishedAt = DateTimeOffset.UtcNow
             });
 
diff --git a/src/PBA.Infrastructure/Connectors/BlogConnector.cs b/src/PBA.Infrastructure/Connectors/BlogConnector.cs
index 4e85768..c568fad 100644
--- a/src/PBA.Infrastructure/Connectors/BlogConnector.cs
+++ b/src/PBA.Infrastructure/Connectors/BlogConnector.cs
@@ -1,18 +1,17 @@
 using System.Text.RegularExpressions;
-using Markdig;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using PBA.Application.Common.Interfaces;
-using PBA.Domain.Entities;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
 
 namespace PBA.Infrastructure.Connectors;
 
-public sealed partial class BlogConnector : IBlogConnector
+public sealed partial class BlogConnector : IPlatformConnector
 {
     private readonly IProcessRunner _processRunner;
     private readonly IOptionsMonitor<BlogConnectorOptions> _options;
     private readonly ILogger<BlogConnector> _logger;
-    private readonly MarkdownPipeline _pipeline;
 
     public BlogConnector(
         IProcessRunner processRunner,
@@ -22,48 +21,63 @@ public sealed partial class BlogConnector : IBlogConnector
         _processRunner = processRunner;
         _options = options;
         _logger = logger;
-        _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
     }
 
-    public async Task<string> PublishAsync(Content content, CancellationToken ct)
-    {
-        ArgumentException.ThrowIfNullOrWhiteSpace(content.Title, nameof(content.Title));
-        ArgumentException.ThrowIfNullOrWhiteSpace(content.Body, nameof(content.Body));
-
-        var opts = _options.CurrentValue;
-
-        if (!File.Exists(opts.TemplatePath))
-            throw new InvalidOperationException($"Blog template not found: {opts.TemplatePath}");
+    public Platform Platform => Platform.Blog;
 
-        var slug = GenerateSlug(content.Title);
-        if (string.IsNullOrEmpty(slug))
-            throw new InvalidOperationException($"Cannot generate a valid URL slug from title: {content.Title}");
+    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
+    {
+        try
+        {
+            ArgumentException.ThrowIfNullOrWhiteSpace(request.Content.Title, nameof(request.Content.Title));
+            ArgumentException.ThrowIfNullOrWhiteSpace(request.TransformedContent, nameof(request.TransformedContent));
 
-        var template = await File.ReadAllTextAsync(opts.TemplatePath, ct);
-        var html = Markdown.ToHtml(content.Body, _pipeline);
+            var opts = _options.CurrentValue;
+            var slug = GenerateSlug(request.Content.Title);
+            if (string.IsNullOrEmpty(slug))
+                return new PlatformPublishResult(false, null, null,
+                    $"Cannot generate a valid URL slug from title: {request.Content.Title}");
 
-        var rendered = template
-            .Replace("{{title}}", content.Title)
-            .Replace("{{content}}", html)
-            .Replace("{{date}}", content.CreatedAt.ToString("yyyy-MM-dd"))
-            .Replace("{{author}}", opts.Author)
-            .Replace("{{tags}}", string.Join(", ", content.Tags))
-            .Replace("{{category}}", content.ContentType.ToString());
+            var filePath = Path.Combine(opts.RepoPath, "posts", $"{slug}.html");
+            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
+            await File.WriteAllTextAsync(filePath, request.TransformedContent, ct);
 
-        var filePath = Path.Combine(opts.RepoPath, "posts", $"{slug}.html");
-        var directory = Path.GetDirectoryName(filePath)!;
-        Directory.CreateDirectory(directory);
-        await File.WriteAllTextAsync(filePath, rendered, ct);
+            await RunGitAsync(opts, $"add posts/{slug}.html", ct);
+            await RunGitCommitAsync(opts, request.Content.Title, ct);
+            await RunGitAsync(opts, $"push {opts.RemoteName} {opts.Branch}", ct);
 
-        await RunGitAsync(opts, $"add posts/{slug}.html", ct: ct);
-        await RunGitCommitAsync(opts, content.Title, ct);
-        await RunGitAsync(opts, $"push {opts.RemoteName} {opts.Branch}", ct: ct);
+            var url = $"{opts.BaseUrl.TrimEnd('/')}/posts/{slug}";
+            _logger.LogInformation("Published blog post {Slug} to {BaseUrl}", slug, opts.BaseUrl);
 
-        _logger.LogInformation("Published blog post {Slug} to {BaseUrl}", slug, opts.BaseUrl);
+            return new PlatformPublishResult(true, url, slug, null);
+        }
+        catch (ArgumentException)
+        {
+            throw;
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Failed to publish blog post '{Title}'", request.Content.Title);
+            return new PlatformPublishResult(false, null, null, ex.Message);
+        }
+    }
 
-        return $"{opts.BaseUrl.TrimEnd('/')}/posts/{slug}";
+    public Task<bool> ValidateCredentialsAsync(CancellationToken ct)
+    {
+        var opts = _options.CurrentValue;
+        return Task.FromResult(Directory.Exists(opts.RepoPath));
     }
 
+    public PlatformCapabilities GetCapabilities() => new(
+        MaxCharacters: int.MaxValue,
+        SupportsMarkdown: false,
+        SupportsHtml: true,
+        SupportsImages: true,
+        SupportsScheduling: false,
+        SupportsThreads: false,
+        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "image/webp"]
+    );
+
     private async Task RunGitAsync(BlogConnectorOptions opts, string arguments, CancellationToken ct)
     {
         var result = await _processRunner.RunAsync("git", $"-C \"{opts.RepoPath}\" {arguments}", ct: ct);
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 1afff1c..5c303a7 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -42,7 +42,7 @@ public static class DependencyInjection
         services.AddHostedService<AiConnectionsService>();
 
         services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
-        services.AddScoped<IBlogConnector, BlogConnector>();
+        services.AddKeyedScoped<IPlatformConnector, BlogConnector>(PBA.Domain.Enums.Platform.Blog);
 
         services.AddScoped<IContentPublisher, ContentPublisher>();
         services.AddScoped<IContentScheduler, HangfireContentScheduler>();
diff --git a/src/PBA.Infrastructure/Publishing/ContentPublisher.cs b/src/PBA.Infrastructure/Publishing/ContentPublisher.cs
index 407b281..2dbdb4b 100644
--- a/src/PBA.Infrastructure/Publishing/ContentPublisher.cs
+++ b/src/PBA.Infrastructure/Publishing/ContentPublisher.cs
@@ -1,6 +1,7 @@
-using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
 using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
 using PBA.Application.Features.ContentStudio;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
@@ -9,7 +10,7 @@ namespace PBA.Infrastructure.Publishing;
 
 public sealed class ContentPublisher(
     IAppDbContext db,
-    IBlogConnector blogConnector,
+    [FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector,
     ILogger<ContentPublisher> logger) : IContentPublisher
 {
     public async Task PublishAsync(Guid contentId)
@@ -27,9 +28,18 @@ public sealed class ContentPublisher(
             return;
         }
 
-        string? publishedUrl = null;
+        PlatformPublishResult? result = null;
         if (content.PrimaryPlatform == Platform.Blog)
-            publishedUrl = await blogConnector.PublishAsync(content, CancellationToken.None);
+        {
+            var request = new PlatformPublishRequest(
+                Content: content,
+                TransformedContent: content.Body,
+                Tags: content.Tags.AsReadOnly(),
+                CanonicalUrl: null,
+                Mode: PublishMode.Publish,
+                ScheduledAt: content.ScheduledAt);
+            result = await blogConnector.PublishAsync(request, CancellationToken.None);
+        }
 
         var machine = ContentStateMachine.Create(content);
         await machine.FireAsync(ContentTrigger.Publish);
@@ -39,7 +49,7 @@ public sealed class ContentPublisher(
             ContentId = contentId,
             Platform = content.PrimaryPlatform,
             Status = PublishStatus.Published,
-            PublishedUrl = publishedUrl,
+            PublishedUrl = result?.PublishedUrl,
             PublishedAt = DateTimeOffset.UtcNow
         });
 
diff --git a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
index 1eae846..3e0cb4f 100644
--- a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
+++ b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
@@ -49,10 +49,11 @@ public class TestWebApplicationFactory : WebApplicationFactory<Program>
                 .ReturnsAsync("""{"score": 85, "feedback": "Good brand voice alignment"}""");
             services.AddSingleton(sidecarMock.Object);
 
-            var blogConnectorMock = new Mock<IBlogConnector>();
-            blogConnectorMock.Setup(x => x.PublishAsync(It.IsAny<PBA.Domain.Entities.Content>(), It.IsAny<CancellationToken>()))
-                .ReturnsAsync("https://blog.test/published-post");
-            services.AddSingleton(blogConnectorMock.Object);
+            services.RemoveAll<IPlatformConnector>();
+            var blogConnectorMock = new Mock<IPlatformConnector>();
+            blogConnectorMock.Setup(x => x.PublishAsync(It.IsAny<PBA.Application.Common.Models.PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
+                .ReturnsAsync(new PBA.Application.Common.Models.PlatformPublishResult(true, "https://blog.test/published-post", "published-post", null));
+            services.AddKeyedSingleton<IPlatformConnector>(PBA.Domain.Enums.Platform.Blog, blogConnectorMock.Object);
 
             services.AddSingleton(new Mock<IContentPublisher>().Object);
         });
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs
index 92d7dbd..2c8d482 100644
--- a/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs
@@ -2,6 +2,7 @@ using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using Moq;
 using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
 using PBA.Infrastructure.Connectors;
@@ -21,13 +22,10 @@ public class BlogConnectorTests : IDisposable
         _tempDir = Path.Combine(Path.GetTempPath(), $"blog-connector-test-{Guid.NewGuid():N}");
         Directory.CreateDirectory(_tempDir);
 
-        var templatePath = Path.Combine(_tempDir, "template.html");
-        File.WriteAllText(templatePath, "<html><head><title>{{title}}</title></head><body>{{content}}<p>{{date}}</p><p>{{author}}</p><p>{{tags}}</p><p>{{category}}</p></body></html>");
-
         _options = new BlogConnectorOptions
         {
             RepoPath = _tempDir,
-            TemplatePath = templatePath,
+            TemplatePath = Path.Combine(_tempDir, "template.html"),
             Author = "Matt Kruczek",
             RemoteName = "origin",
             Branch = "main",
@@ -49,68 +47,90 @@ public class BlogConnectorTests : IDisposable
         return monitor.Object;
     }
 
-    private static Content CreateTestContent(string title = "My Test Post", string body = "## Hello\n\nThis is **bold** text.") =>
+    private static Content CreateTestContent(string title = "My Test Post") =>
         new()
         {
             Title = title,
-            Body = body,
+            Body = "Some content",
             ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Tags = ["AI", "Engineering"],
             CreatedAt = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero)
         };
 
+    private static PlatformPublishRequest CreatePublishRequest(
+        string title = "My Test Post",
+        string transformedContent = "<h2>Hello</h2><p>This is <strong>bold</strong> text.</p>") =>
+        new(
+            Content: CreateTestContent(title),
+            TransformedContent: transformedContent,
+            Tags: ["AI", "Engineering"],
+            CanonicalUrl: null,
+            Mode: PublishMode.Publish,
+            ScheduledAt: null
+        );
+
     [Fact]
-    public async Task PublishAsync_ConvertsMarkdownToHtml()
+    public void ImplementsIPlatformConnector()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent();
-
-        await connector.PublishAsync(content, CancellationToken.None);
+        Assert.IsAssignableFrom<IPlatformConnector>(connector);
+    }
 
-        var outputFile = Path.Combine(_tempDir, "posts", "my-test-post.html");
-        var result = await File.ReadAllTextAsync(outputFile);
-        Assert.Contains("<h2", result);
-        Assert.Contains("Hello</h2>", result);
-        Assert.Contains("<strong>bold</strong>", result);
+    [Fact]
+    public void Platform_ReturnsBlog()
+    {
+        var connector = CreateConnector();
+        Assert.Equal(Platform.Blog, connector.Platform);
     }
 
     [Fact]
-    public async Task PublishAsync_InjectsHtmlIntoTemplateWithMetadata()
+    public async Task PublishAsync_UsesTransformedContentDirectly()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent();
+        var html = "<html><body><h1>Pre-rendered</h1></body></html>";
+        var request = CreatePublishRequest(transformedContent: html);
 
-        await connector.PublishAsync(content, CancellationToken.None);
+        await connector.PublishAsync(request, CancellationToken.None);
 
         var outputFile = Path.Combine(_tempDir, "posts", "my-test-post.html");
         var result = await File.ReadAllTextAsync(outputFile);
-        Assert.Contains("<title>My Test Post</title>", result);
-        Assert.Contains("2026-05-11", result);
-        Assert.Contains("Matt Kruczek", result);
-        Assert.Contains("AI, Engineering", result);
-        Assert.Contains("Blog", result);
+        Assert.Equal(html, result);
     }
 
     [Fact]
     public async Task PublishAsync_WritesFileToCorrectPath()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent();
+        var request = CreatePublishRequest();
 
-        await connector.PublishAsync(content, CancellationToken.None);
+        await connector.PublishAsync(request, CancellationToken.None);
 
         var expectedPath = Path.Combine(_tempDir, "posts", "my-test-post.html");
         Assert.True(File.Exists(expectedPath));
     }
 
+    [Fact]
+    public async Task PublishAsync_ReturnsSuccessWithUrl()
+    {
+        var connector = CreateConnector();
+        var request = CreatePublishRequest();
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.True(result.Success);
+        Assert.Equal("https://matthewkruczek.ai/posts/my-test-post", result.PublishedUrl);
+        Assert.Equal("my-test-post", result.PlatformPostId);
+        Assert.Null(result.ErrorMessage);
+    }
+
     [Fact]
     public async Task PublishAsync_RunsGitAddCommitPush()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent();
+        var request = CreatePublishRequest();
 
-        await connector.PublishAsync(content, CancellationToken.None);
+        await connector.PublishAsync(request, CancellationToken.None);
 
         var calls = _processRunner.Invocations
             .Where(i => i.Method.Name == nameof(IProcessRunner.RunAsync))
@@ -127,9 +147,9 @@ public class BlogConnectorTests : IDisposable
     public async Task PublishAsync_CommitUsesStdinToAvoidInjection()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent(title: "Title with \"quotes\" and $(cmd)");
+        var request = CreatePublishRequest(title: "Title with \"quotes\" and $(cmd)");
 
-        await connector.PublishAsync(content, CancellationToken.None);
+        await connector.PublishAsync(request, CancellationToken.None);
 
         _processRunner.Verify(
             p => p.RunAsync("git",
@@ -141,112 +161,116 @@ public class BlogConnectorTests : IDisposable
     }
 
     [Fact]
-    public async Task PublishAsync_ReturnsConstructedUrl()
+    public async Task PublishAsync_SetsWorkingDirectory()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent();
-
-        var url = await connector.PublishAsync(content, CancellationToken.None);
+        var request = CreatePublishRequest();
 
-        Assert.Equal("https://matthewkruczek.ai/posts/my-test-post", url);
-    }
+        await connector.PublishAsync(request, CancellationToken.None);
 
-    [Theory]
-    [InlineData("My Blog Post! (Part 2)", "my-blog-post-part-2")]
-    [InlineData("Hello World", "hello-world")]
-    [InlineData("C# & .NET Tips", "c-net-tips")]
-    [InlineData("  Spaced  Out  ", "spaced-out")]
-    [InlineData("---dashes---", "dashes")]
-    public void GenerateSlug_ProducesExpectedOutput(string title, string expected)
-    {
-        var slug = BlogConnector.GenerateSlug(title);
-        Assert.Equal(expected, slug);
+        _processRunner.Verify(
+            p => p.RunAsync("git", It.Is<string>(s => s.Contains($"-C \"{_tempDir}\"")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
+            Times.Exactly(3));
     }
 
     [Fact]
-    public async Task PublishAsync_HandlesGitPushFailure()
+    public async Task PublishAsync_ReturnsFailureOnGitPushError()
     {
         _processRunner
             .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("push")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ProcessRunResult(1, "", "fatal: remote rejected"));
 
         var connector = CreateConnector();
-        var content = CreateTestContent();
+        var request = CreatePublishRequest();
 
-        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
-            () => connector.PublishAsync(content, CancellationToken.None));
+        var result = await connector.PublishAsync(request, CancellationToken.None);
 
-        Assert.Contains("fatal: remote rejected", ex.Message);
+        Assert.False(result.Success);
+        Assert.Contains("fatal: remote rejected", result.ErrorMessage);
     }
 
     [Fact]
-    public async Task PublishAsync_SetsWorkingDirectory()
+    public async Task PublishAsync_ReturnsFailureOnGitAddError()
     {
+        _processRunner
+            .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("add")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new ProcessRunResult(1, "", "fatal: not a git repository"));
+
         var connector = CreateConnector();
-        var content = CreateTestContent();
+        var request = CreatePublishRequest();
 
-        await connector.PublishAsync(content, CancellationToken.None);
+        var result = await connector.PublishAsync(request, CancellationToken.None);
 
-        _processRunner.Verify(
-            p => p.RunAsync("git", It.Is<string>(s => s.Contains($"-C \"{_tempDir}\"")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
-            Times.Exactly(3));
+        Assert.False(result.Success);
+        Assert.Contains("fatal: not a git repository", result.ErrorMessage);
     }
 
     [Fact]
     public async Task PublishAsync_EmptyTitle_ThrowsArgumentException()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent(title: "   ", body: "Some body");
+        var request = CreatePublishRequest(title: "   ");
 
         await Assert.ThrowsAsync<ArgumentException>(
-            () => connector.PublishAsync(content, CancellationToken.None));
+            () => connector.PublishAsync(request, CancellationToken.None));
     }
 
     [Fact]
-    public async Task PublishAsync_EmptyBody_ThrowsArgumentException()
+    public async Task PublishAsync_EmptyTransformedContent_ThrowsArgumentException()
     {
         var connector = CreateConnector();
-        var content = CreateTestContent(body: "  ");
+        var request = CreatePublishRequest(transformedContent: "   ");
 
         await Assert.ThrowsAsync<ArgumentException>(
-            () => connector.PublishAsync(content, CancellationToken.None));
+            () => connector.PublishAsync(request, CancellationToken.None));
     }
 
     [Fact]
-    public async Task PublishAsync_MissingTemplate_ThrowsInvalidOperation()
+    public async Task ValidateCredentialsAsync_ReturnsTrueWhenRepoExists()
+    {
+        var connector = CreateConnector();
+        Assert.True(await connector.ValidateCredentialsAsync(CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task ValidateCredentialsAsync_ReturnsFalseWhenRepoMissing()
     {
         var badOptions = new BlogConnectorOptions
         {
-            RepoPath = _tempDir,
-            TemplatePath = Path.Combine(_tempDir, "nonexistent.html"),
-            Author = "Matt Kruczek"
+            RepoPath = Path.Combine(_tempDir, "nonexistent"),
+            TemplatePath = "unused"
         };
         var monitor = new Mock<IOptionsMonitor<BlogConnectorOptions>>();
         monitor.Setup(m => m.CurrentValue).Returns(badOptions);
 
         var connector = new BlogConnector(_processRunner.Object, monitor.Object, _logger.Object);
-        var content = CreateTestContent();
-
-        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
-            () => connector.PublishAsync(content, CancellationToken.None));
-
-        Assert.Contains("template not found", ex.Message, StringComparison.OrdinalIgnoreCase);
+        Assert.False(await connector.ValidateCredentialsAsync(CancellationToken.None));
     }
 
     [Fact]
-    public async Task PublishAsync_HandlesGitAddFailure()
+    public void GetCapabilities_ReturnsCorrectValues()
     {
-        _processRunner
-            .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("add")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new ProcessRunResult(1, "", "fatal: not a git repository"));
-
         var connector = CreateConnector();
-        var content = CreateTestContent();
-
-        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
-            () => connector.PublishAsync(content, CancellationToken.None));
+        var caps = connector.GetCapabilities();
+
+        Assert.Equal(int.MaxValue, caps.MaxCharacters);
+        Assert.False(caps.SupportsMarkdown);
+        Assert.True(caps.SupportsHtml);
+        Assert.True(caps.SupportsImages);
+        Assert.False(caps.SupportsScheduling);
+        Assert.False(caps.SupportsThreads);
+    }
 
-        Assert.Contains("fatal: not a git repository", ex.Message);
+    [Theory]
+    [InlineData("My Blog Post! (Part 2)", "my-blog-post-part-2")]
+    [InlineData("Hello World", "hello-world")]
+    [InlineData("C# & .NET Tips", "c-net-tips")]
+    [InlineData("  Spaced  Out  ", "spaced-out")]
+    [InlineData("---dashes---", "dashes")]
+    public void GenerateSlug_ProducesExpectedOutput(string title, string expected)
+    {
+        var slug = BlogConnector.GenerateSlug(title);
+        Assert.Equal(expected, slug);
     }
 
     [Fact]
diff --git a/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs b/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs
index 049bdd6..4a249d2 100644
--- a/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs
@@ -2,6 +2,7 @@ using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
 using PBA.Infrastructure.Data;
@@ -13,7 +14,7 @@ namespace PBA.Infrastructure.Tests.Publishing;
 public class ContentPublisherTests : IDisposable
 {
     private readonly ApplicationDbContext _dbContext;
-    private readonly Mock<IBlogConnector> _blogConnector = new();
+    private readonly Mock<IPlatformConnector> _blogConnector = new();
     private readonly Mock<ILogger<ContentPublisher>> _logger = new();
 
     public ContentPublisherTests()
@@ -43,8 +44,8 @@ public class ContentPublisherTests : IDisposable
         var content = CreateScheduledContent();
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync("https://matthewkruczek.ai/posts/test-post");
+        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
@@ -67,7 +68,7 @@ public class ContentPublisherTests : IDisposable
 
         var updated = await _dbContext.Contents.FindAsync(content.Id);
         Assert.Equal(ContentStatus.Approved, updated!.Status);
-        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()), Times.Never);
+        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
     }
 
     [Fact]
@@ -76,13 +77,13 @@ public class ContentPublisherTests : IDisposable
         var content = CreateScheduledContent(Platform.Blog);
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync("https://matthewkruczek.ai/posts/test-post");
+        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
-        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()), Times.Once);
+        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
     }
 
     [Fact]
@@ -95,7 +96,7 @@ public class ContentPublisherTests : IDisposable
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
-        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()), Times.Never);
+        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
     }
 
     [Fact]
@@ -104,8 +105,8 @@ public class ContentPublisherTests : IDisposable
         var content = CreateScheduledContent();
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<Content>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync("https://matthewkruczek.ai/posts/test-post");
+        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
