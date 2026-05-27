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
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class MediumConnectorTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<MediumConnector>> _logger = new();

    private readonly MediumOptions _mediumOptions = new()
    {
        Enabled = true,
        DefaultPublishStatus = "draft"
    };

    public MediumConnectorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns((string s) => s.Replace("encrypted:", ""));

        _httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://api.medium.com")
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        SeedCredential();
    }

    private void SeedCredential()
    {
        _dbContext.PlatformCredentials.Add(new PlatformCredential
        {
            Platform = Platform.Medium,
            EncryptedIntegrationToken = "encrypted:test-medium-token",
            IsActive = true
        });
        _dbContext.SaveChanges();
    }

    private MediumConnector CreateConnector()
    {
        var optionsMonitor = new Mock<IOptionsMonitor<MediumOptions>>();
        optionsMonitor.Setup(o => o.CurrentValue).Returns(_mediumOptions);
        return new(
            _httpClient,
            _dbContext,
            _encryptor.Object,
            optionsMonitor.Object,
            _logger.Object);
    }

    private static Content CreateContent(string title = "Test Post") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Body = "Test body content",
        Status = ContentStatus.Approved,
        PrimaryPlatform = Platform.Medium
    };

    private void SetupHttpSequence(params (string responseJson, HttpStatusCode status)[] responses)
    {
        var sequence = _httpHandler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        foreach (var (responseJson, status) in responses)
        {
            sequence.ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private string? SetupMeAndPostWithCapture()
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
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }

                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        return capturedBody;
    }

    [Fact]
    public async Task PublishAsync_Draft_SendsCorrectPayload()
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
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
                            System.Text.Encoding.UTF8, "application/json")
                    };

                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "# My Post\n\nBody here.", ["AI", "Tech"], null, PublishMode.Draft, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"publishStatus\":\"draft\"", capturedBody);
        Assert.Contains("\"contentFormat\":\"markdown\"", capturedBody);
    }

    [Fact]
    public async Task PublishAsync_Public_SendsCorrectStatus()
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
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
                            System.Text.Encoding.UTF8, "application/json")
                    };

                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Body.", [], null, PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"publishStatus\":\"public\"", capturedBody);
    }

    [Fact]
    public async Task PublishAsync_TruncatesTags_ToMax3()
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
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
                            System.Text.Encoding.UTF8, "application/json")
                    };

                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Body.", ["AI", "Engineering", "CSharp", "Web", "DevOps"], null, PublishMode.Draft, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody);
        var tags = doc.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.Equal(3, tags.Count);
        Assert.Equal(["AI", "Engineering", "CSharp"], tags);
    }

    [Fact]
    public async Task PublishAsync_ReturnsPublishedUrlFromResponse()
    {
        SetupHttpSequence(
            (JsonSerializer.Serialize(new { data = new { id = "user123" } }), HttpStatusCode.OK),
            (JsonSerializer.Serialize(new { data = new { id = "abc123", title = "Test Post", url = "https://medium.com/@user/title-abc123", publishStatus = "public" } }), HttpStatusCode.Created)
        );

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Body.", [], null, PublishMode.Publish, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://medium.com/@user/title-abc123", result.PublishedUrl);
        Assert.Equal("abc123", result.PlatformPostId);
    }

    [Fact]
    public async Task PublishAsync_IncludesCanonicalUrl()
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
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
                            System.Text.Encoding.UTF8, "application/json")
                    };

                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Body.", [], "https://matthewkruczek.ai/posts/test", PublishMode.Publish, null);

        await connector.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("https://matthewkruczek.ai/posts/test", capturedBody);
    }

    [Fact]
    public async Task PublishAsync_InvalidToken_ReturnsFailureResult()
    {
        SetupHttpSequence(
            (JsonSerializer.Serialize(new { errors = new[] { new { message = "Token was invalid.", code = 6003 } } }), HttpStatusCode.Unauthorized)
        );

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Body.", [], null, PublishMode.Draft, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("invalid", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_RateLimited_ReturnsFailureResult()
    {
        SetupHttpSequence(
            (JsonSerializer.Serialize(new { data = new { id = "user123" } }), HttpStatusCode.OK),
            (JsonSerializer.Serialize(new { errors = new[] { new { message = "Rate limit exceeded", code = 429 } } }), HttpStatusCode.TooManyRequests)
        );

        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Body.", [], null, PublishMode.Draft, null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rate limit", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ValidToken_ReturnsTrue()
    {
        SetupHttpSequence(
            (JsonSerializer.Serialize(new { data = new { id = "user123", username = "test" } }), HttpStatusCode.OK)
        );

        var connector = CreateConnector();
        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_InvalidToken_ReturnsFalse()
    {
        SetupHttpSequence(
            ("{\"errors\":[{\"message\":\"Token was invalid.\"}]}", HttpStatusCode.Unauthorized)
        );

        var connector = CreateConnector();
        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public void GetCapabilities_ReturnsCorrectValues()
    {
        var connector = CreateConnector();
        var caps = connector.GetCapabilities();

        Assert.Equal(int.MaxValue, caps.MaxCharacters);
        Assert.True(caps.SupportsMarkdown);
        Assert.True(caps.SupportsHtml);
        Assert.True(caps.SupportsImages);
        Assert.False(caps.SupportsScheduling);
        Assert.False(caps.SupportsThreads);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _dbContext.Dispose();
    }
}
