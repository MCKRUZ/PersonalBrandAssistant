using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
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
    }

    private BlogConnector CreateConnector() =>
        new(_processRunner.Object, CreateOptionsMonitor(), _logger.Object);

    private IOptionsMonitor<BlogConnectorOptions> CreateOptionsMonitor()
    {
        var monitor = new Mock<IOptionsMonitor<BlogConnectorOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(_options);
        return monitor.Object;
    }

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

        var outputFile = Path.Combine(_tempDir, "posts", "my-test-post.html");
        var result = await File.ReadAllTextAsync(outputFile);
        Assert.Equal(html, result);
    }

    [Fact]
    public async Task PublishAsync_WritesFileToCorrectPath()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        await connector.PublishAsync(request, CancellationToken.None);

        var expectedPath = Path.Combine(_tempDir, "posts", "my-test-post.html");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task PublishAsync_ReturnsSuccessWithUrl()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://matthewkruczek.ai/posts/my-test-post", result.PublishedUrl);
        Assert.Equal("my-test-post", result.PlatformPostId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_RunsGitAddCommitPush()
    {
        var connector = CreateConnector();
        var request = CreatePublishRequest();

        await connector.PublishAsync(request, CancellationToken.None);

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
            Times.Exactly(3));
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

        var connector = new BlogConnector(_processRunner.Object, monitor.Object, _logger.Object);
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
