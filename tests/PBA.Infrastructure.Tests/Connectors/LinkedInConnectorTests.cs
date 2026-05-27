using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class LinkedInConnectorTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<IOAuthService> _oauthService = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<LinkedInConnector>> _logger = new();

    private readonly LinkedInOptions _linkedInOptions = new()
    {
        Enabled = true,
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/linkedin/callback"
    };

    public LinkedInConnectorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns((string s) => s.Replace("encrypted:", ""));

        _httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://api.linkedin.com")
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        SeedCredential(DateTimeOffset.UtcNow.AddDays(30));
    }

    private void SeedCredential(DateTimeOffset accessTokenExpiresAt)
    {
        foreach (var existing in _dbContext.PlatformCredentials.Where(c => c.Platform == Platform.LinkedIn))
            _dbContext.PlatformCredentials.Remove(existing);
        _dbContext.SaveChanges();

        _dbContext.PlatformCredentials.Add(new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedAccessToken = "encrypted:test-linkedin-token",
            EncryptedRefreshToken = "encrypted:test-refresh-token",
            AccessTokenExpiresAt = accessTokenExpiresAt,
            IsActive = true
        });
        _dbContext.SaveChanges();
    }

    private LinkedInConnector CreateConnector()
    {
        var optionsMonitor = new Mock<IOptionsMonitor<LinkedInOptions>>();
        optionsMonitor.Setup(o => o.CurrentValue).Returns(_linkedInOptions);
        return new(
            _httpClient,
            _dbContext,
            _encryptor.Object,
            _oauthService.Object,
            optionsMonitor.Object,
            _logger.Object);
    }

    private static Content CreateContent(string title = "Test Post") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Body = "Test body content",
        Status = ContentStatus.Approved,
        PrimaryPlatform = Platform.LinkedIn
    };

    private void SetupUserInfoAndPost()
    {
        var callCount = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                callCount++;
                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }

                var response = new HttpResponseMessage(HttpStatusCode.Created);
                response.Headers.Add("x-restli-id", "urn:li:share:12345");
                return response;
            });
    }

    [Fact]
    public async Task PublishAsync_ExpiredToken_RefreshesBeforePublishing()
    {
        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("new-access-token"));

        SetupUserInfoAndPost();

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        _oauthService.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_RefreshFails_ReturnsAuthFailure()
    {
        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Fail("Token refresh failed"));

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("token", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_ValidToken_DoesNotRefresh()
    {
        SetupUserInfoAndPost();

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        _oauthService.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_TextPost_CreatesCorrectPayload()
    {
        string? capturedBody = null;
        var callCount = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                callCount++;
                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }

                capturedBody = await req.Content!.ReadAsStringAsync();
                var response = new HttpResponseMessage(HttpStatusCode.Created);
                response.Headers.Add("x-restli-id", "urn:li:share:12345");
                return response;
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Hello LinkedIn!", [], null, PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"author\":\"urn:li:person:person123\"", capturedBody);
        Assert.Contains("\"commentary\":\"Hello LinkedIn!\"", capturedBody);
        Assert.Contains("\"visibility\":\"PUBLIC\"", capturedBody);
        Assert.Contains("\"lifecycleState\":\"PUBLISHED\"", capturedBody);
    }

    [Fact]
    public async Task PublishAsync_WithArticleLink_IncludesContentObject()
    {
        string? capturedBody = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }

                capturedBody = await req.Content!.ReadAsStringAsync();
                var response = new HttpResponseMessage(HttpStatusCode.Created);
                response.Headers.Add("x-restli-id", "urn:li:share:12345");
                return response;
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent("My Article"), "Check out my article.", [],
            "https://matthewkruczek.ai/posts/test", PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("https://matthewkruczek.ai/posts/test", capturedBody);
        Assert.Contains("My Article", capturedBody);
    }

    [Fact]
    public async Task PublishAsync_ReturnsPostUrnFromResponseHeader()
    {
        SetupUserInfoAndPost();

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Content.", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("urn:li:share:12345", result.PlatformPostId);
        Assert.Contains("urn:li:share:12345", result.PublishedUrl!);
    }

    [Fact]
    public async Task PublishAsync_HttpError_ReturnsFailure()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { status = 403, message = "Insufficient permissions" }),
                        System.Text.Encoding.UTF8, "application/json")
                });
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Content.", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_IncludesVersionHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                capturedRequest = req;
                var response = new HttpResponseMessage(HttpStatusCode.Created);
                response.Headers.Add("x-restli-id", "urn:li:share:12345");
                return Task.FromResult(response);
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Content.", [], null, PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("2.0.0", capturedRequest.Headers.GetValues("X-Restli-Protocol-Version").First());
        Assert.Equal("202604", capturedRequest.Headers.GetValues("LinkedIn-Version").First());
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ValidToken_ReturnsTrue()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { sub = "person123", name = "Test" }),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var connector = CreateConnector();
        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ExpiredToken_ReturnsFalse()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var connector = CreateConnector();
        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public void GetCapabilities_ReturnsCorrectValues()
    {
        var connector = CreateConnector();
        var caps = connector.GetCapabilities();

        Assert.Equal(3000, caps.MaxCharacters);
        Assert.False(caps.SupportsMarkdown);
        Assert.False(caps.SupportsHtml);
        Assert.False(caps.SupportsImages);
        Assert.False(caps.SupportsScheduling);
        Assert.False(caps.SupportsThreads);
    }

    [Theory]
    [InlineData(PublishMode.Draft)]
    [InlineData(PublishMode.Schedule)]
    public async Task PublishAsync_UnsupportedMode_ReturnsFailure(PublishMode mode)
    {
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Content.", [], null, mode, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does not support", result.ErrorMessage!);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _dbContext.Dispose();
    }
}
