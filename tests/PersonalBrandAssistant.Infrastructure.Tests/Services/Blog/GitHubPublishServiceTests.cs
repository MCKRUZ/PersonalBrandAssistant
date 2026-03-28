using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.BlogServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Blog;

public class GitHubPublishServiceTests
{
    private static BlogPublishOptions CreateOptions(
        int initialDelay = 1, int maxRetries = 3) => new()
    {
        RepoOwner = "MCKRUZ",
        RepoName = "matthewkruczek-ai",
        Branch = "main",
        ContentPath = "content/blog",
        AuthorName = "Matthew Kruczek",
        AuthorEmail = "matt@matthewkruczek.ai",
        DeployVerificationInitialDelaySeconds = initialDelay,
        DeployVerificationMaxRetries = maxRetries
    };

    private static BlogPublishRequest CreateRequest(string? html = null, string? path = null) => new()
    {
        ContentId = Guid.NewGuid(),
        Html = html ?? "<html><body>Test</body></html>",
        TargetPath = path ?? "content/blog/2026-03-28-test-post-abc123.html",
        Status = BlogPublishStatus.Publishing
    };

    private static GitHubPublishService CreateSut(
        HttpClient githubClient,
        HttpClient? verifyClient = null,
        BlogPublishOptions? options = null,
        IConfiguration? config = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("GitHubApi")).Returns(githubClient);
        factory.Setup(f => f.CreateClient("BlogVerification"))
            .Returns(verifyClient ?? new HttpClient(new StubHandler(HttpStatusCode.OK)));

        config ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:PersonalAccessToken"] = "ghp_test_token_12345"
            })
            .Build();

        return new GitHubPublishService(
            factory.Object,
            Options.Create(options ?? CreateOptions()),
            config,
            NullLogger<GitHubPublishService>.Instance);
    }

    [Fact]
    public async Task CommitBlogPostAsync_CreatesFile_ViaGitHubContentsApi()
    {
        var handler = new SequentialHandler(
            // GET — file doesn't exist
            new StubResponse(HttpStatusCode.NotFound, "{}"),
            // PUT — create file
            new StubResponse(HttpStatusCode.Created, JsonSerializer.Serialize(new
            {
                commit = new { sha = "abc123def456", html_url = "https://github.com/MCKRUZ/matthewkruczek-ai/commit/abc123def456" }
            })));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);
        var request = CreateRequest();

        var result = await sut.CommitBlogPostAsync(request, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("abc123def456", result.Value!.CommitSha);
        Assert.Contains("abc123def456", result.Value.CommitUrl);
    }

    [Fact]
    public async Task CommitBlogPostAsync_UsesCorrectCommitMessage()
    {
        var handler = new CaptureHandler(
            new StubResponse(HttpStatusCode.NotFound, "{}"),
            new StubResponse(HttpStatusCode.Created, JsonSerializer.Serialize(new
            {
                commit = new { sha = "abc123", html_url = "https://github.com/test" }
            })));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);
        var request = CreateRequest(path: "content/blog/2026-03-28-my-post-abc123.html");

        await sut.CommitBlogPostAsync(request, default);

        var putBody = handler.CapturedBodies.Last();
        Assert.Contains("blog: publish 2026-03-28-my-post-abc123", putBody);
    }

    [Fact]
    public async Task CommitBlogPostAsync_UsesConfiguredAuthorNameAndEmail()
    {
        var handler = new CaptureHandler(
            new StubResponse(HttpStatusCode.NotFound, "{}"),
            new StubResponse(HttpStatusCode.Created, JsonSerializer.Serialize(new
            {
                commit = new { sha = "abc", html_url = "https://github.com/test" }
            })));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);

        await sut.CommitBlogPostAsync(CreateRequest(), default);

        var putBody = handler.CapturedBodies.Last();
        Assert.Contains("Matthew Kruczek", putBody);
        Assert.Contains("matt@matthewkruczek.ai", putBody);
    }

    [Fact]
    public async Task CommitBlogPostAsync_CommitsToConfiguredBranch()
    {
        var handler = new CaptureHandler(
            new StubResponse(HttpStatusCode.NotFound, "{}"),
            new StubResponse(HttpStatusCode.Created, JsonSerializer.Serialize(new
            {
                commit = new { sha = "abc", html_url = "https://github.com/test" }
            })));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);

        await sut.CommitBlogPostAsync(CreateRequest(), default);

        var putBody = handler.CapturedBodies.Last();
        Assert.Contains("main", putBody);
    }

    [Fact]
    public async Task CommitBlogPostAsync_StoresCommitShaInResult()
    {
        var handler = new SequentialHandler(
            new StubResponse(HttpStatusCode.NotFound, "{}"),
            new StubResponse(HttpStatusCode.Created, JsonSerializer.Serialize(new
            {
                commit = new { sha = "deadbeef12345678", html_url = "https://github.com/test" }
            })));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);

        var result = await sut.CommitBlogPostAsync(CreateRequest(), default);

        Assert.Equal("deadbeef12345678", result.Value!.CommitSha);
    }

    [Fact]
    public async Task CommitBlogPostAsync_HandlesFileAlreadyExists_SendsUpdate()
    {
        var handler = new CaptureHandler(
            // GET — file exists with sha
            new StubResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new { sha = "existing_sha_value" })),
            // PUT — update
            new StubResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                commit = new { sha = "updated_sha", html_url = "https://github.com/test" }
            })));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);

        var result = await sut.CommitBlogPostAsync(CreateRequest(), default);

        Assert.True(result.IsSuccess);
        var putBody = handler.CapturedBodies.Last();
        Assert.Contains("existing_sha_value", putBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication failed")]
    [InlineData(HttpStatusCode.Forbidden, "authorization denied")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "rejected")]
    public async Task CommitBlogPostAsync_ReturnsError_OnGitHubApiFailure(
        HttpStatusCode statusCode, string expectedFragment)
    {
        var handler = new SequentialHandler(
            new StubResponse(HttpStatusCode.NotFound, "{}"),
            new StubResponse(statusCode, "{\"message\":\"error\"}"));

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var sut = CreateSut(client);

        var result = await sut.CommitBlogPostAsync(CreateRequest(), default);

        Assert.False(result.IsSuccess);
        Assert.Contains(expectedFragment, result.Errors.First(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitBlogPostAsync_ReturnsError_WhenPatNotConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var client = new HttpClient(new StubHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://api.github.com")
        };
        var sut = CreateSut(client, config: config);

        var result = await sut.CommitBlogPostAsync(CreateRequest(), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("Token", result.Errors.First());
    }

    [Fact]
    public async Task VerifyDeploymentAsync_ReturnsTrue_OnHttp200()
    {
        var verifyClient = new HttpClient(new StubHandler(HttpStatusCode.OK));
        var githubClient = new HttpClient(new StubHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        var sut = CreateSut(githubClient, verifyClient, CreateOptions(initialDelay: 0));

        var result = await sut.VerifyDeploymentAsync("https://matthewkruczek.ai/blog/test", default);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyDeploymentAsync_ReturnsFalse_AfterAllRetriesExhausted()
    {
        var verifyClient = new HttpClient(new StubHandler(HttpStatusCode.NotFound));
        var githubClient = new HttpClient(new StubHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        var sut = CreateSut(githubClient, verifyClient, CreateOptions(initialDelay: 0, maxRetries: 2));

        var result = await sut.VerifyDeploymentAsync("https://matthewkruczek.ai/blog/test", default);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyDeploymentAsync_HandlesNetworkErrors()
    {
        var verifyClient = new HttpClient(new ThrowingHandler());
        var githubClient = new HttpClient(new StubHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://api.github.com")
        };

        var sut = CreateSut(githubClient, verifyClient, CreateOptions(initialDelay: 0, maxRetries: 2));

        var result = await sut.VerifyDeploymentAsync("https://matthewkruczek.ai/blog/test", default);

        Assert.False(result);
    }

    // Test helpers

    private record StubResponse(HttpStatusCode StatusCode, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public StubHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{}")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Network error");
    }

    private sealed class SequentialHandler : HttpMessageHandler
    {
        private readonly Queue<StubResponse> _responses;
        public SequentialHandler(params StubResponse[] responses) =>
            _responses = new Queue<StubResponse>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var stub = _responses.Count > 0
                ? _responses.Dequeue()
                : new StubResponse(HttpStatusCode.InternalServerError, "{}");
            return Task.FromResult(new HttpResponseMessage(stub.StatusCode)
            {
                Content = new StringContent(stub.Body)
            });
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<StubResponse> _responses;
        public List<string> CapturedBodies { get; } = [];

        public CaptureHandler(params StubResponse[] responses) =>
            _responses = new Queue<StubResponse>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(ct)
                : string.Empty;
            CapturedBodies.Add(body);

            var stub = _responses.Count > 0
                ? _responses.Dequeue()
                : new StubResponse(HttpStatusCode.InternalServerError, "{}");
            return new HttpResponseMessage(stub.StatusCode)
            {
                Content = new StringContent(stub.Body)
            };
        }
    }
}
