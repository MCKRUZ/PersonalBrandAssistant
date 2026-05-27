using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class SubstackConnectorTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly MockSubstackHandler _handler = new();

    private const string CookieJson =
        "{\"substack.sid\":\"sid123\",\"sid\":\"s123\",\"substack.lli\":\"lli123\"}";

    public SubstackConnectorTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);

        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns<string>(s => s.Replace("encrypted:", ""));
    }

    private SubstackConnector CreateConnector(bool enabled = true)
    {
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://matthewkruczek.substack.com")
        };

        var opts = Mock.Of<IOptionsMonitor<SubstackOptions>>(o =>
            o.CurrentValue == new SubstackOptions
            {
                Enabled = enabled,
                PublicationSlug = "matthewkruczek",
                DefaultAudience = "everyone"
            });

        return new SubstackConnector(
            httpClient, _dbContext, _encryptor.Object, opts,
            NullLogger<SubstackConnector>.Instance);
    }

    private void SeedCredential(string? encryptedCookies = "encrypted:" + CookieJson)
    {
        _dbContext.PlatformCredentials.Add(new PlatformCredential
        {
            Id = Guid.NewGuid(),
            Platform = Platform.Substack,
            EncryptedAccessToken = "",
            EncryptedCookies = encryptedCookies,
            IsActive = true
        });
        _dbContext.SaveChanges();
    }

    private static PlatformPublishRequest MakeRequest(
        PublishMode mode = PublishMode.Draft,
        IReadOnlyList<string>? tags = null) =>
        new(
            new Content { Title = "Test Post" },
            "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}]}]}",
            tags ?? [],
            null,
            mode,
            null);

    private void SetupDraftFlow()
    {
        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.OK,
            "{\"id\":1,\"name\":\"Test\",\"byline_id\":789}");
        _handler.Setup(HttpMethod.Post, "/api/v1/drafts", HttpStatusCode.OK,
            "{\"id\":12345,\"slug\":\"test-post\",\"title\":\"Test Post\"}");
    }

    [Fact]
    public async Task PublishAsync_Draft_CreatesDraftOnly()
    {
        SeedCredential();
        SetupDraftFlow();
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(PublishMode.Draft), default);

        Assert.True(result.Success);
        Assert.Equal("12345", result.PlatformPostId);
        Assert.DoesNotContain(_handler.Requests, r => r.Path.EndsWith("/prepublish"));
        Assert.DoesNotContain(_handler.Requests, r => r.Path.EndsWith("/publish"));
    }

    [Fact]
    public async Task PublishAsync_Publish_ExecutesFullFlow()
    {
        SeedCredential();
        SetupDraftFlow();
        _handler.Setup(HttpMethod.Post, "/api/v1/drafts/12345/prepublish", HttpStatusCode.OK, "{}");
        _handler.Setup(HttpMethod.Post, "/api/v1/drafts/12345/publish", HttpStatusCode.OK,
            "{\"id\":12345,\"slug\":\"test-post\",\"canonical_url\":\"https://matthewkruczek.substack.com/p/test-post\"}");
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(PublishMode.Publish), default);

        Assert.True(result.Success);
        Assert.Equal("https://matthewkruczek.substack.com/p/test-post", result.PublishedUrl);
        Assert.Equal("12345", result.PlatformPostId);
    }

    [Fact]
    public async Task PublishAsync_WithTags_CallsTagsEndpoint()
    {
        SeedCredential();
        SetupDraftFlow();
        _handler.Setup(HttpMethod.Put, "/api/v1/post/12345/tags", HttpStatusCode.OK, "{}");
        var connector = CreateConnector();

        await connector.PublishAsync(MakeRequest(tags: ["AI", "Engineering"]), default);

        Assert.Contains(_handler.Requests, r =>
            r.Method == HttpMethod.Put && r.Path == "/api/v1/post/12345/tags");
    }

    [Fact]
    public async Task PublishAsync_AttachesCookiesToAllRequests()
    {
        SeedCredential();
        SetupDraftFlow();
        var connector = CreateConnector();

        await connector.PublishAsync(MakeRequest(), default);

        Assert.All(_handler.Requests, r =>
        {
            Assert.NotNull(r.CookieHeader);
            Assert.Contains("substack.sid=sid123", r.CookieHeader);
        });
    }

    [Fact]
    public async Task PublishAsync_AuthExpired_ReturnsFailure()
    {
        SeedCredential();
        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.Unauthorized,
            "{\"error\":true,\"message\":\"Not authorized\"}");
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(), default);

        Assert.False(result.Success);
        Assert.Contains("session", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_Disabled_ReturnsFailure()
    {
        var connector = CreateConnector(enabled: false);

        var result = await connector.PublishAsync(MakeRequest(), default);

        Assert.False(result.Success);
        Assert.Contains("disabled", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task PublishAsync_NoCredential_ReturnsFailure()
    {
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(), default);

        Assert.False(result.Success);
        Assert.Contains("credential", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_Schedule_TreatedAsDraft()
    {
        SeedCredential();
        SetupDraftFlow();
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(PublishMode.Schedule), default);

        Assert.True(result.Success);
        Assert.DoesNotContain(_handler.Requests, r => r.Path.EndsWith("/publish"));
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ValidCookies_ReturnsTrue()
    {
        SeedCredential();
        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.OK,
            "{\"id\":1,\"name\":\"Test\",\"byline_id\":789}");
        var connector = CreateConnector();

        var result = await connector.ValidateCredentialsAsync(default);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ExpiredCookies_ReturnsFalse()
    {
        SeedCredential();
        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.Unauthorized,
            "{\"error\":true,\"message\":\"Not authorized\"}");
        var connector = CreateConnector();

        var result = await connector.ValidateCredentialsAsync(default);

        Assert.False(result);
    }

    [Fact]
    public async Task GetCapabilities_ReturnsCorrectValues()
    {
        var connector = CreateConnector();
        var caps = connector.GetCapabilities();

        Assert.Equal(int.MaxValue, caps.MaxCharacters);
        Assert.False(caps.SupportsMarkdown);
        Assert.False(caps.SupportsHtml);
        Assert.True(caps.SupportsImages);
        Assert.False(caps.SupportsScheduling);
        Assert.False(caps.SupportsThreads);
    }

    [Fact]
    public async Task PublishAsync_DraftCreationFails_ReturnsFailure()
    {
        SeedCredential();
        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.OK,
            "{\"id\":1,\"name\":\"Test\",\"byline_id\":789}");
        _handler.Setup(HttpMethod.Post, "/api/v1/drafts", HttpStatusCode.InternalServerError,
            "{\"error\":true,\"message\":\"Internal server error\"}");
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(), default);

        Assert.False(result.Success);
        Assert.Contains("draft", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_CookieDecryptionFails_ReturnsFailure()
    {
        SeedCredential("bad-encrypted-data");
        _encryptor.Setup(e => e.Decrypt("bad-encrypted-data"))
            .Throws(new FormatException("Invalid encrypted data"));
        var connector = CreateConnector();

        var result = await connector.PublishAsync(MakeRequest(), default);

        Assert.False(result.Success);
        Assert.Empty(_handler.Requests);
    }

    public void Dispose() => _dbContext.Dispose();

    private sealed class MockSubstackHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new();
        public List<(HttpMethod Method, string Path, string? Body, string? CookieHeader)> Requests { get; } = [];

        public void Setup(HttpMethod method, string path, HttpStatusCode status, string body)
        {
            _responses[$"{method}:{path}"] = (status, body);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(ct)
                : null;
            var cookies = request.Headers.Contains("Cookie")
                ? request.Headers.GetValues("Cookie").FirstOrDefault()
                : null;
            Requests.Add((request.Method, path, body, cookies));

            var key = $"{request.Method}:{path}";
            if (_responses.TryGetValue(key, out var response))
            {
                return new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
