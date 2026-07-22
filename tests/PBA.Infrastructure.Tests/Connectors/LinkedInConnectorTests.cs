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
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
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

        // The connector asks the factory for a bare client to PUT the pre-signed video parts;
        // route it through the same mock handler so the upload PUTs are intercepted too.
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(_httpHandler.Object));

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
            _httpClientFactory.Object,
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
        Assert.True(caps.SupportsImages);
        Assert.False(caps.SupportsScheduling);
        Assert.False(caps.SupportsThreads);
        Assert.Contains("image/png", caps.SupportedMediaTypes);
        Assert.Contains("video/mp4", caps.SupportedMediaTypes);
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

    [Fact]
    public async Task PublishAsync_WithVideo_UploadsAndAttachesVideoUrn()
    {
        const string videoUrn = "urn:li:video:C5505AQH-testvideo";
        const string partEtag = "/ambry-video/signedId/AQJxWLYH-part0.bin";
        string? finalizeBody = null;
        string? postBody = null;

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                var path = req.RequestUri!.PathAndQuery;

                // 1. person lookup
                if (path == "/v2/userinfo")
                    return Json(new { sub = "person123", name = "Test User" });

                // 2. initializeUpload -> one pre-signed part covering the whole file
                if (path == "/rest/videos?action=initializeUpload")
                    return Json(new
                    {
                        value = new
                        {
                            uploadUrlsExpireAt = 1633234498985L,
                            video = videoUrn,
                            uploadInstructions = new[]
                            {
                                new { uploadUrl = "https://api.linkedin.com/dms-uploads/part0", firstByte = 0, lastByte = 9 }
                            },
                            uploadToken = ""
                        }
                    });

                // 3. the pre-signed PUT for the bytes -> 200 with an ETag part id (no auth header)
                if (req.Method == HttpMethod.Put)
                {
                    Assert.Null(req.Headers.Authorization);
                    var putResponse = new HttpResponseMessage(HttpStatusCode.OK);
                    // LinkedIn's part id is a path-form ETag that fails strict validation on Add,
                    // but arrives fine as a received header — mimic that with TryAddWithoutValidation.
                    putResponse.Headers.TryAddWithoutValidation("ETag", partEtag);
                    return putResponse;
                }

                // 4. finalizeUpload
                if (path == "/rest/videos?action=finalizeUpload")
                {
                    finalizeBody = await req.Content!.ReadAsStringAsync();
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                // 5. status poll -> AVAILABLE
                if (path.StartsWith("/rest/videos/"))
                    return Json(new { id = videoUrn, status = "AVAILABLE" });

                // 6. the actual post
                postBody = await req.Content!.ReadAsStringAsync();
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Add("x-restli-id", "urn:li:share:99999");
                return created;
            });

        var connector = CreateConnector();
        var media = new MediaAttachment(new byte[10], "harness_li_teaser.mp4", "video/mp4", "Harness teaser");
        var request = new PlatformPublishRequest(
            CreateContent(), "Post with a video.", [], null, PublishMode.Publish, null, media);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("urn:li:share:99999", result.PlatformPostId);

        // Finalize carried the ETag part id back (quotes stripped).
        Assert.NotNull(finalizeBody);
        Assert.Contains(partEtag, finalizeBody);

        // The post references the video URN and its title via content.media.
        Assert.NotNull(postBody);
        Assert.Contains($"\"id\":\"{videoUrn}\"", postBody);
        Assert.Contains("Harness teaser", postBody);
    }

    [Fact]
    public async Task PublishAsync_WithImage_UploadsAndAttachesImageUrn()
    {
        const string imageUrn = "urn:li:image:C4E10AQFtestimage";
        var putHadAuth = false;
        string? postBody = null;

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                var path = req.RequestUri!.PathAndQuery;

                if (path == "/v2/userinfo")
                    return Json(new { sub = "person123", name = "Test User" });

                // initializeUpload -> single upload URL (no byte ranges, no token)
                if (path == "/rest/images?action=initializeUpload")
                    return Json(new
                    {
                        value = new
                        {
                            uploadUrlExpiresAt = 1650567510704L,
                            uploadUrl = "https://api.linkedin.com/dms-uploads/image0",
                            image = imageUrn
                        }
                    });

                // the single image PUT — LinkedIn requires the bearer token here (unlike video)
                if (req.Method == HttpMethod.Put)
                {
                    putHadAuth = req.Headers.Authorization is not null;
                    return new HttpResponseMessage(HttpStatusCode.Created);
                }

                // status poll -> AVAILABLE (status under `value`)
                if (path.StartsWith("/rest/images/"))
                    return Json(new { value = new { status = "AVAILABLE" } });

                postBody = await req.Content!.ReadAsStringAsync();
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Add("x-restli-id", "urn:li:share:77777");
                return created;
            });

        var connector = CreateConnector();
        var media = new MediaAttachment(new byte[64], "thumb.png", "image/png", "Harness thumbnail");
        var request = new PlatformPublishRequest(
            CreateContent(), "Post with an image.", [], null, PublishMode.Publish, null, media);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(putHadAuth); // image upload URL is token-gated
        Assert.NotNull(postBody);
        Assert.Contains($"\"id\":\"{imageUrn}\"", postBody);
        Assert.Contains("altText", postBody);
        Assert.Contains("Harness thumbnail", postBody);
    }

    [Fact]
    public async Task PublishAsync_UnsupportedMediaType_ReturnsFailureWithoutPosting()
    {
        var posted = false;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                if (req.RequestUri!.PathAndQuery == "/v2/userinfo")
                    return Task.FromResult(Json(new { sub = "person123" }));
                if (req.RequestUri.PathAndQuery == "/rest/posts")
                    posted = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            });

        var connector = CreateConnector();
        var media = new MediaAttachment(new byte[16], "notes.pdf", "application/pdf", null);
        var request = new PlatformPublishRequest(
            CreateContent(), "Post with a PDF.", [], null, PublishMode.Publish, null, media);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does not support this media type", result.ErrorMessage!);
        Assert.False(posted); // must not silently post text and drop the attachment
    }

    [Fact]
    public async Task PublishAsync_VideoProcessingFails_ReturnsFailure()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                var path = req.RequestUri!.PathAndQuery;
                if (path == "/v2/userinfo")
                    return Task.FromResult(Json(new { sub = "person123" }));
                if (path == "/rest/videos?action=initializeUpload")
                    return Task.FromResult(Json(new
                    {
                        value = new
                        {
                            video = "urn:li:video:fail",
                            uploadInstructions = new[]
                            {
                                new { uploadUrl = "https://api.linkedin.com/dms-uploads/part0", firstByte = 0, lastByte = 9 }
                            },
                            uploadToken = ""
                        }
                    }));
                if (req.Method == HttpMethod.Put)
                {
                    var put = new HttpResponseMessage(HttpStatusCode.OK);
                    put.Headers.TryAddWithoutValidation("ETag", "etag0");
                    return Task.FromResult(put);
                }
                if (path == "/rest/videos?action=finalizeUpload")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                // status poll -> processing failed (must not proceed to a post)
                return Task.FromResult(Json(new { status = "PROCESSING_FAILED" }));
            });

        var connector = CreateConnector();
        var media = new MediaAttachment(new byte[10], "clip.mp4", "video/mp4", null);
        var request = new PlatformPublishRequest(
            CreateContent(), "Post with a video.", [], null, PublishMode.Publish, null, media);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("video upload failed", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
    };

    public void Dispose()
    {
        _httpClient.Dispose();
        _dbContext.Dispose();
    }
}
