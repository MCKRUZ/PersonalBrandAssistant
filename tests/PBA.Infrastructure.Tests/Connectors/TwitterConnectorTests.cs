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

public class TwitterConnectorTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<IOAuthService> _oauthService = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<TwitterConnector>> _logger = new();

    private readonly TwitterOptions _twitterOptions = new()
    {
        Enabled = true,
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/twitter/callback",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public TwitterConnectorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns((string s) => s.Replace("encrypted:", ""));

        _httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://api.x.com")
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        SeedCredential(DateTimeOffset.UtcNow.AddDays(30));
    }

    private void SeedCredential(DateTimeOffset accessTokenExpiresAt)
    {
        foreach (var existing in _dbContext.PlatformCredentials.Where(c => c.Platform == Platform.Twitter))
            _dbContext.PlatformCredentials.Remove(existing);
        _dbContext.SaveChanges();

        _dbContext.PlatformCredentials.Add(new PlatformCredential
        {
            Platform = Platform.Twitter,
            EncryptedAccessToken = "encrypted:test-twitter-token",
            EncryptedRefreshToken = "encrypted:test-refresh-token",
            AccessTokenExpiresAt = accessTokenExpiresAt,
            IsActive = true
        });
        _dbContext.SaveChanges();
    }

    private TwitterConnector CreateConnector()
    {
        var optionsMonitor = new Mock<IOptionsMonitor<TwitterOptions>>();
        optionsMonitor.Setup(o => o.CurrentValue).Returns(_twitterOptions);
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
        PrimaryPlatform = Platform.Twitter
    };

    private void SetupTweetPost(string tweetId = "12345")
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = tweetId, text = "Hello" } }, JsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            });
    }

    [Fact]
    public async Task PublishAsync_SingleTweet_PostsOnce()
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
                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = "12345", text = "Hello" } }, JsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Hello world", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("12345", result.PlatformPostId);
        Assert.Contains("12345", result.PublishedUrl!);
        Assert.Equal(1, callCount);
        Assert.NotNull(capturedBody);
        Assert.Contains("Hello world", capturedBody);
    }

    [Fact]
    public async Task PublishAsync_Thread_ChainsRepliesWithCorrectIds()
    {
        var callIndex = 0;
        var capturedBodies = new List<string>();
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                var body = req.Content is not null ? await req.Content.ReadAsStringAsync() : "";
                capturedBodies.Add(body);
                var id = (100 + callIndex).ToString();
                callIndex++;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id, text = "segment" } }, JsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var threadContent = JsonSerializer.Serialize(new[] { "First segment", "Second segment", "Third segment" });
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), threadContent, [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, capturedBodies.Count);
        Assert.Equal("100", result.PlatformPostId);

        Assert.DoesNotContain("in_reply_to_tweet_id", capturedBodies[0]);
        Assert.Contains("100", capturedBodies[1]);
        Assert.Contains("101", capturedBodies[2]);
    }

    [Fact]
    public async Task PublishAsync_ReturnsFirstTweetIdForThreads()
    {
        var callIndex = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                var id = callIndex == 0 ? "first-tweet" : "second-tweet";
                callIndex++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id, text = "segment" } }, JsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                });
            });

        var threadContent = JsonSerializer.Serialize(new[] { "Segment one", "Segment two" });
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), threadContent, [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.Equal("first-tweet", result.PlatformPostId);
        Assert.Contains("first-tweet", result.PublishedUrl!);
    }

    [Fact]
    public async Task PublishAsync_ExpiredToken_RefreshesBeforePublishing()
    {
        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("new-access-token"));

        SetupTweetPost();

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        _oauthService.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_TokenRefreshFails_ReturnsAuthFailure()
    {
        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Fail("Token refresh failed"));

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Content.", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("reconnect", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
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
                    JsonSerializer.Serialize(new { data = new { id = "123", name = "Matt", username = "maboroshi_matt" } }, JsonOptions),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var connector = CreateConnector();
        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_InvalidToken_ReturnsFalse()
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

        Assert.Equal(280, caps.MaxCharacters);
        Assert.False(caps.SupportsMarkdown);
        Assert.False(caps.SupportsHtml);
        Assert.True(caps.SupportsImages);
        Assert.False(caps.SupportsScheduling);
        Assert.True(caps.SupportsThreads);
        Assert.Contains("image/png", caps.SupportedMediaTypes);
        Assert.Contains("image/jpeg", caps.SupportedMediaTypes);
        Assert.Contains("image/gif", caps.SupportedMediaTypes);
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
    public async Task PublishAsync_PartialThreadFailure_ReturnsPartialInfo()
    {
        var callIndex = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                callIndex++;
                if (callIndex <= 2)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { data = new { id = $"tweet-{callIndex}", text = "ok" } }, JsonOptions),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                });
            });

        var threadContent = JsonSerializer.Serialize(new[] { "Seg 1", "Seg 2", "Seg 3" });
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), threadContent, [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("tweet-1", result.PlatformPostId);
        Assert.Contains("tweet-1", result.PublishedUrl!);
        Assert.Contains("partially published", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadMediaAsync_ChunkedFlow_ReturnsMediaId()
    {
        var callIndex = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                callIndex++;
                if (req.RequestUri?.AbsolutePath.Contains("initialize") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { media_id = "media-999" }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                if (req.RequestUri?.AbsolutePath.Contains("append") == true)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

                if (req.RequestUri?.AbsolutePath.Contains("finalize") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { media_id = "media-999" }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        var connector = CreateConnector();
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var mediaId = await connector.UploadMediaAsync("image/png", imageData.Length, imageData,
            "test-token", CancellationToken.None);

        Assert.Equal("media-999", mediaId);
        Assert.True(callIndex >= 3);
    }

    [Fact]
    public async Task UploadMediaAsync_FinalizeWithProcessing_PollsUntilComplete()
    {
        var callIndex = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                callIndex++;
                if (req.RequestUri?.AbsolutePath.Contains("initialize") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { media_id = "media-video" }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                if (req.RequestUri?.AbsolutePath.Contains("append") == true)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

                if (req.RequestUri?.AbsolutePath.Contains("finalize") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { media_id = "media-video", processing_info = new { state = "pending", check_after_secs = 0 } }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath.Contains("media-video") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { media_id = "media-video", processing_info = new { state = "succeeded" } }),
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        var connector = CreateConnector();
        var videoData = new byte[] { 0x00, 0x00, 0x00 };

        var mediaId = await connector.UploadMediaAsync("video/mp4", videoData.Length, videoData,
            "test-token", CancellationToken.None);

        Assert.Equal("media-video", mediaId);
        Assert.True(callIndex >= 4);
    }

    [Fact]
    public async Task PublishAsync_RateLimited_ReturnsFailureWithMessage()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { title = "Too Many Requests", status = 429, detail = "Rate limit exceeded" }, JsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
                response.Headers.Add("x-rate-limit-reset", "1700000000");
                return response;
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Content.", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rate limit", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _dbContext.Dispose();
    }
}
