using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class BlogConnectorTests : IDisposable
{
    private readonly Mock<IProcessRunner> _processRunner = new();
    private readonly Mock<ILogger<BlogConnector>> _logger = new();
    private readonly string _tempDir;
    private readonly BlogConnectorOptions _options;

    public BlogConnectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"blog-connector-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var templatePath = Path.Combine(_tempDir, "template.html");
        File.WriteAllText(templatePath, "<html><head><title>{{title}}</title></head><body>{{content}}<p>{{date}}</p><p>{{author}}</p><p>{{tags}}</p><p>{{category}}</p></body></html>");

        _options = new BlogConnectorOptions
        {
            RepoPath = _tempDir,
            TemplatePath = templatePath,
            Author = "Matt Kruczek",
            RemoteName = "origin",
            Branch = "main",
            BaseUrl = "https://matthewkruczek.ai"
        };

        _processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
    }

    private BlogConnector CreateConnector() =>
        new(_processRunner.Object, CreateOptionsMonitor(), _logger.Object);

    private IOptionsMonitor<BlogConnectorOptions> CreateOptionsMonitor()
    {
        var monitor = new Mock<IOptionsMonitor<BlogConnectorOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(_options);
        return monitor.Object;
    }

    private static Content CreateTestContent(string title = "My Test Post", string body = "## Hello\n\nThis is **bold** text.") =>
        new()
        {
            Title = title,
            Body = body,
            ContentType = ContentType.BlogPost,
            PrimaryPlatform = Platform.Blog,
            Tags = ["AI", "Engineering"],
            CreatedAt = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero)
        };

    [Fact]
    public async Task PublishAsync_ConvertsMarkdownToHtml()
    {
        var connector = CreateConnector();
        var content = CreateTestContent();

        await connector.PublishAsync(content, CancellationToken.None);

        var outputFile = Path.Combine(_tempDir, "posts", "my-test-post.html");
        var result = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("<h2", result);
        Assert.Contains("Hello</h2>", result);
        Assert.Contains("<strong>bold</strong>", result);
    }

    [Fact]
    public async Task PublishAsync_InjectsHtmlIntoTemplateWithMetadata()
    {
        var connector = CreateConnector();
        var content = CreateTestContent();

        await connector.PublishAsync(content, CancellationToken.None);

        var outputFile = Path.Combine(_tempDir, "posts", "my-test-post.html");
        var result = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("<title>My Test Post</title>", result);
        Assert.Contains("2026-05-11", result);
        Assert.Contains("Matt Kruczek", result);
        Assert.Contains("AI, Engineering", result);
        Assert.Contains("BlogPost", result);
    }

    [Fact]
    public async Task PublishAsync_WritesFileToCorrectPath()
    {
        var connector = CreateConnector();
        var content = CreateTestContent();

        await connector.PublishAsync(content, CancellationToken.None);

        var expectedPath = Path.Combine(_tempDir, "posts", "my-test-post.html");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task PublishAsync_RunsGitAddCommitPush()
    {
        var connector = CreateConnector();
        var content = CreateTestContent();

        await connector.PublishAsync(content, CancellationToken.None);

        var calls = _processRunner.Invocations
            .Where(i => i.Method.Name == nameof(IProcessRunner.RunAsync))
            .Select(i => (string)i.Arguments[1])
            .ToList();

        Assert.Equal(3, calls.Count);
        Assert.Contains("add posts/my-test-post.html", calls[0]);
        Assert.Contains("commit --file=-", calls[1]);
        Assert.Contains("push origin main", calls[2]);
    }

    [Fact]
    public async Task PublishAsync_CommitUsesStdinToAvoidInjection()
    {
        var connector = CreateConnector();
        var content = CreateTestContent(title: "Title with \"quotes\" and $(cmd)");

        await connector.PublishAsync(content, CancellationToken.None);

        _processRunner.Verify(
            p => p.RunAsync("git",
                It.Is<string>(s => s.Contains("commit --file=-")),
                It.Is<string?>(s => s != null && s.Contains("Title with \"quotes\" and $(cmd)")),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ReturnsConstructedUrl()
    {
        var connector = CreateConnector();
        var content = CreateTestContent();

        var url = await connector.PublishAsync(content, CancellationToken.None);

        Assert.Equal("https://matthewkruczek.ai/posts/my-test-post", url);
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
    public async Task PublishAsync_HandlesGitPushFailure()
    {
        _processRunner
            .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("push")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "fatal: remote rejected"));

        var connector = CreateConnector();
        var content = CreateTestContent();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.PublishAsync(content, CancellationToken.None));

        Assert.Contains("fatal: remote rejected", ex.Message);
    }

    [Fact]
    public async Task PublishAsync_SetsWorkingDirectory()
    {
        var connector = CreateConnector();
        var content = CreateTestContent();

        await connector.PublishAsync(content, CancellationToken.None);

        _processRunner.Verify(
            p => p.RunAsync("git", It.Is<string>(s => s.Contains($"-C \"{_tempDir}\"")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task PublishAsync_EmptyTitle_ThrowsArgumentException()
    {
        var connector = CreateConnector();
        var content = CreateTestContent(title: "   ", body: "Some body");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.PublishAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_EmptyBody_ThrowsArgumentException()
    {
        var connector = CreateConnector();
        var content = CreateTestContent(body: "  ");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.PublishAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_MissingTemplate_ThrowsInvalidOperation()
    {
        var badOptions = new BlogConnectorOptions
        {
            RepoPath = _tempDir,
            TemplatePath = Path.Combine(_tempDir, "nonexistent.html"),
            Author = "Matt Kruczek"
        };
        var monitor = new Mock<IOptionsMonitor<BlogConnectorOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(badOptions);

        var connector = new BlogConnector(_processRunner.Object, monitor.Object, _logger.Object);
        var content = CreateTestContent();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.PublishAsync(content, CancellationToken.None));

        Assert.Contains("template not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_HandlesGitAddFailure()
    {
        _processRunner
            .Setup(p => p.RunAsync("git", It.Is<string>(s => s.Contains("add")), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "fatal: not a git repository"));

        var connector = CreateConnector();
        var content = CreateTestContent();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.PublishAsync(content, CancellationToken.None));

        Assert.Contains("fatal: not a git repository", ex.Message);
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
