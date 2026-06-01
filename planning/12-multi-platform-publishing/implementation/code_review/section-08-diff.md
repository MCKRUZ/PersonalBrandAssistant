diff --git a/src/PBA.Infrastructure/Connectors/LinkedInConnector.cs b/src/PBA.Infrastructure/Connectors/LinkedInConnector.cs
new file mode 100644
index 0000000..764c412
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/LinkedInConnector.cs
@@ -0,0 +1,232 @@
+using System.Net;
+using System.Net.Http.Headers;
+using System.Text;
+using System.Text.Json;
+using System.Text.Json.Serialization;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed class LinkedInConnector(
+    HttpClient httpClient,
+    IAppDbContext db,
+    ITokenEncryptor encryptor,
+    IOAuthService oauthService,
+    IOptionsMonitor<LinkedInOptions> options,
+    ILogger<LinkedInConnector> logger) : IPlatformConnector
+{
+    private readonly IOptionsMonitor<LinkedInOptions> _options = options;
+    private const string LinkedInVersion = "202604";
+    private const string RestliProtocolVersion = "2.0.0";
+    private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(5);
+
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
+    };
+
+    public Platform Platform => Platform.LinkedIn;
+
+    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
+    {
+        try
+        {
+            if (request.Mode is PublishMode.Draft or PublishMode.Schedule)
+                return new PlatformPublishResult(false, null, null,
+                    "LinkedIn API does not support draft or scheduled posts. Content can only be published immediately.");
+
+            var credential = await GetActiveCredentialAsync(ct);
+            var token = await GetValidTokenAsync(credential, ct);
+            if (token is null)
+                return new PlatformPublishResult(false, null, null,
+                    "LinkedIn token refresh failed. Please reconnect in Settings.");
+
+            var personUrn = await GetPersonUrnAsync(token, ct);
+            if (personUrn is null)
+                return new PlatformPublishResult(false, null, null,
+                    "LinkedIn access token is invalid or expired. Please reconnect in Settings.");
+
+            var payload = BuildPostPayload(personUrn, request);
+
+            var json = JsonSerializer.Serialize(payload, JsonOptions);
+            using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/rest/posts")
+            {
+                Content = new StringContent(json, Encoding.UTF8, "application/json")
+            };
+            SetLinkedInHeaders(postRequest, token);
+
+            var response = await httpClient.SendAsync(postRequest, ct);
+
+            if (response.StatusCode == HttpStatusCode.TooManyRequests)
+                return new PlatformPublishResult(false, null, null, "LinkedIn rate limit exceeded. Retry scheduled.");
+
+            if (response.StatusCode == HttpStatusCode.Unauthorized)
+                return new PlatformPublishResult(false, null, null,
+                    "LinkedIn access token is invalid or expired. Please reconnect in Settings.");
+
+            if (response.StatusCode == HttpStatusCode.Forbidden)
+                return new PlatformPublishResult(false, null, null,
+                    "LinkedIn API access denied. Verify your app has w_member_social scope.");
+
+            if (!response.IsSuccessStatusCode)
+            {
+                var errorBody = await response.Content.ReadAsStringAsync(ct);
+                logger.LogError("LinkedIn publish failed: {Status} {Body}", response.StatusCode, errorBody);
+                return new PlatformPublishResult(false, null, null, $"LinkedIn publish failed ({response.StatusCode})");
+            }
+
+            var postUrn = response.Headers.TryGetValues("x-restli-id", out var values)
+                ? values.FirstOrDefault()
+                : null;
+
+            var publishedUrl = postUrn is not null
+                ? $"https://www.linkedin.com/feed/update/{postUrn}"
+                : null;
+
+            return new PlatformPublishResult(true, publishedUrl, postUrn, null);
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "Failed to publish to LinkedIn");
+            return new PlatformPublishResult(false, null, null,
+                "An unexpected error occurred while publishing to LinkedIn. Check logs for details.");
+        }
+    }
+
+    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
+    {
+        try
+        {
+            var credential = await GetActiveCredentialAsync(ct);
+            var token = encryptor.Decrypt(credential.EncryptedAccessToken);
+            return await GetPersonUrnAsync(token, ct) is not null;
+        }
+        catch (Exception ex)
+        {
+            logger.LogWarning(ex, "LinkedIn credential validation failed");
+            return false;
+        }
+    }
+
+    public PlatformCapabilities GetCapabilities() => new(
+        MaxCharacters: 3000,
+        SupportsMarkdown: false,
+        SupportsHtml: false,
+        SupportsImages: true,
+        SupportsScheduling: false,
+        SupportsThreads: false,
+        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif"]
+    );
+
+    private async Task<string?> GetValidTokenAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        var token = encryptor.Decrypt(credential.EncryptedAccessToken);
+
+        if (credential.AccessTokenExpiresAt.HasValue &&
+            credential.AccessTokenExpiresAt.Value - DateTimeOffset.UtcNow < TokenRefreshWindow)
+        {
+            var refreshResult = await oauthService.RefreshTokenAsync(credential, ct);
+            if (!refreshResult.IsSuccess)
+            {
+                logger.LogWarning("LinkedIn token refresh failed: {Errors}", string.Join(", ", refreshResult.Errors));
+                return null;
+            }
+            token = refreshResult.Value!;
+        }
+
+        return token;
+    }
+
+    private async Task<string?> GetPersonUrnAsync(string token, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get, "/v2/userinfo");
+        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
+
+        var response = await httpClient.SendAsync(request, ct);
+
+        if (response.StatusCode == HttpStatusCode.Unauthorized)
+            return null;
+
+        if (!response.IsSuccessStatusCode)
+        {
+            var body = await response.Content.ReadAsStringAsync(ct);
+            logger.LogError("LinkedIn /v2/userinfo failed: {Status} {Body}", response.StatusCode, body);
+            throw new HttpRequestException($"LinkedIn user lookup failed ({response.StatusCode})");
+        }
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var userInfo = JsonSerializer.Deserialize<LinkedInUserInfo>(json, JsonOptions);
+        return userInfo?.Sub is not null ? $"urn:li:person:{userInfo.Sub}" : null;
+    }
+
+    private static object BuildPostPayload(string personUrn, PlatformPublishRequest request)
+    {
+        var distribution = new
+        {
+            feedDistribution = "MAIN_FEED",
+            targetEntities = Array.Empty<object>(),
+            thirdPartyDistributionChannels = Array.Empty<object>()
+        };
+
+        if (!string.IsNullOrEmpty(request.CanonicalUrl))
+        {
+            var description = request.TransformedContent.Length > 200
+                ? request.TransformedContent[..200]
+                : request.TransformedContent;
+
+            return new
+            {
+                author = personUrn,
+                commentary = request.TransformedContent,
+                visibility = "PUBLIC",
+                distribution,
+                lifecycleState = "PUBLISHED",
+                content = new
+                {
+                    article = new
+                    {
+                        source = request.CanonicalUrl,
+                        title = request.Content.Title,
+                        description
+                    }
+                }
+            };
+        }
+
+        return new
+        {
+            author = personUrn,
+            commentary = request.TransformedContent,
+            visibility = "PUBLIC",
+            distribution,
+            lifecycleState = "PUBLISHED"
+        };
+    }
+
+    private static void SetLinkedInHeaders(HttpRequestMessage request, string token)
+    {
+        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
+        request.Headers.Add("X-Restli-Protocol-Version", RestliProtocolVersion);
+        request.Headers.Add("LinkedIn-Version", LinkedInVersion);
+    }
+
+    private async Task<PlatformCredential> GetActiveCredentialAsync(CancellationToken ct)
+    {
+        return await db.PlatformCredentials
+            .FirstOrDefaultAsync(c => c.Platform == Platform.LinkedIn && c.IsActive, ct)
+            ?? throw new InvalidOperationException("No active LinkedIn credential found");
+    }
+
+    internal record LinkedInUserInfo(string? Sub, string? Name, string? Email);
+    internal record LinkedInImageUploadResponse(LinkedInImageUploadValue Value);
+    internal record LinkedInImageUploadValue(string UploadUrl, string Image, long UploadUrlExpiresAt);
+    internal record LinkedInErrorResponse(int Status, int ServiceErrorCode, string Code, string Message);
+}
diff --git a/src/PBA.Infrastructure/Connectors/LinkedInFormatter.cs b/src/PBA.Infrastructure/Connectors/LinkedInFormatter.cs
new file mode 100644
index 0000000..4636af1
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/LinkedInFormatter.cs
@@ -0,0 +1,86 @@
+using System.Text.RegularExpressions;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed partial class LinkedInFormatter : IPlatformFormatter
+{
+    private const int MaxCharacters = 3000;
+
+    public Platform Platform => Platform.LinkedIn;
+
+    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
+    {
+        var body = content.Body;
+
+        body = FencedCodeBlock().Replace(body, "$1");
+        body = ImagePattern().Replace(body, "");
+        body = LinkPattern().Replace(body, "$1");
+        body = BoldPattern().Replace(body, "$1");
+        body = ItalicPattern().Replace(body, "$1");
+        body = InlineCodePattern().Replace(body, "$1");
+        body = HeadingPattern().Replace(body, "$1");
+        body = BlockquotePattern().Replace(body, "$1");
+        body = HorizontalRulePattern().Replace(body, "");
+
+        body = CollapseBlankLines().Replace(body, "\n\n");
+        body = body.Trim();
+
+        if (body.Length > MaxCharacters)
+            body = Truncate(body, content.CanonicalUrl);
+
+        return Task.FromResult(body);
+    }
+
+    private static string Truncate(string text, string? canonicalUrl)
+    {
+        string suffix;
+        if (!string.IsNullOrEmpty(canonicalUrl))
+            suffix = $"...\n\nRead more: {canonicalUrl}";
+        else
+            suffix = "...";
+
+        var budget = MaxCharacters - suffix.Length;
+        if (budget <= 0)
+            return suffix[..MaxCharacters];
+
+        var truncated = text[..budget];
+        var lastSpace = truncated.LastIndexOf(' ');
+        if (lastSpace > budget / 2)
+            truncated = truncated[..lastSpace];
+
+        return truncated + suffix;
+    }
+
+    [GeneratedRegex(@"```[\w]*\n([\s\S]*?)```", RegexOptions.Multiline)]
+    private static partial Regex FencedCodeBlock();
+
+    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]+\)\s*")]
+    private static partial Regex ImagePattern();
+
+    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
+    private static partial Regex LinkPattern();
+
+    [GeneratedRegex(@"\*\*(.+?)\*\*")]
+    private static partial Regex BoldPattern();
+
+    [GeneratedRegex(@"\*(.+?)\*")]
+    private static partial Regex ItalicPattern();
+
+    [GeneratedRegex(@"`([^`]+)`")]
+    private static partial Regex InlineCodePattern();
+
+    [GeneratedRegex(@"^#{1,6}\s+(.+)$", RegexOptions.Multiline)]
+    private static partial Regex HeadingPattern();
+
+    [GeneratedRegex(@"^>\s?(.*)$", RegexOptions.Multiline)]
+    private static partial Regex BlockquotePattern();
+
+    [GeneratedRegex(@"^---+\s*$", RegexOptions.Multiline)]
+    private static partial Regex HorizontalRulePattern();
+
+    [GeneratedRegex(@"\n{3,}")]
+    private static partial Regex CollapseBlankLines();
+}
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/LinkedInConnectorTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/LinkedInConnectorTests.cs
new file mode 100644
index 0000000..0924789
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/LinkedInConnectorTests.cs
@@ -0,0 +1,412 @@
+using System.Net;
+using System.Net.Http.Headers;
+using System.Text.Json;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class LinkedInConnectorTests : IDisposable
+{
+    private readonly ApplicationDbContext _dbContext;
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<IOAuthService> _oauthService = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly Mock<ILogger<LinkedInConnector>> _logger = new();
+
+    private readonly LinkedInOptions _linkedInOptions = new()
+    {
+        Enabled = true,
+        ClientId = "test-client-id",
+        ClientSecret = "test-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/linkedin/callback"
+    };
+
+    public LinkedInConnectorTests()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options;
+        _dbContext = new ApplicationDbContext(options);
+
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
+            .Returns((string s) => s.Replace("encrypted:", ""));
+
+        _httpClient = new HttpClient(_httpHandler.Object)
+        {
+            BaseAddress = new Uri("https://api.linkedin.com")
+        };
+        _httpClient.DefaultRequestHeaders.Accept.Add(
+            new MediaTypeWithQualityHeaderValue("application/json"));
+
+        SeedCredential(DateTimeOffset.UtcNow.AddDays(30));
+    }
+
+    private void SeedCredential(DateTimeOffset accessTokenExpiresAt)
+    {
+        foreach (var existing in _dbContext.PlatformCredentials.Where(c => c.Platform == Platform.LinkedIn))
+            _dbContext.PlatformCredentials.Remove(existing);
+        _dbContext.SaveChanges();
+
+        _dbContext.PlatformCredentials.Add(new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedAccessToken = "encrypted:test-linkedin-token",
+            EncryptedRefreshToken = "encrypted:test-refresh-token",
+            AccessTokenExpiresAt = accessTokenExpiresAt,
+            IsActive = true
+        });
+        _dbContext.SaveChanges();
+    }
+
+    private LinkedInConnector CreateConnector()
+    {
+        var optionsMonitor = new Mock<IOptionsMonitor<LinkedInOptions>>();
+        optionsMonitor.Setup(o => o.CurrentValue).Returns(_linkedInOptions);
+        return new(
+            _httpClient,
+            _dbContext,
+            _encryptor.Object,
+            _oauthService.Object,
+            optionsMonitor.Object,
+            _logger.Object);
+    }
+
+    private static Content CreateContent(string title = "Test Post") => new()
+    {
+        Id = Guid.NewGuid(),
+        Title = title,
+        Body = "Test body content",
+        Status = ContentStatus.Approved,
+        PrimaryPlatform = Platform.LinkedIn
+    };
+
+    private void SetupUserInfoAndPost(string? capturedBodyHolder = null)
+    {
+        var callCount = 0;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                callCount++;
+                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
+                {
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+                }
+
+                var response = new HttpResponseMessage(HttpStatusCode.Created);
+                response.Headers.Add("x-restli-id", "urn:li:share:12345");
+                return response;
+            });
+    }
+
+    private void SetupHttpResponses(params (Func<HttpRequestMessage, bool> predicate, HttpResponseMessage response)[] handlers)
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
+            {
+                foreach (var (predicate, response) in handlers)
+                {
+                    if (predicate(req))
+                        return Task.FromResult(response);
+                }
+                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
+            });
+    }
+
+    [Fact]
+    public async Task PublishAsync_ExpiredToken_RefreshesBeforePublishing()
+    {
+        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
+        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Success("new-access-token"));
+
+        SetupUserInfoAndPost();
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        _oauthService.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RefreshFails_ReturnsAuthFailure()
+    {
+        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
+        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Fail("Token refresh failed"));
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.Contains("token", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ValidToken_DoesNotRefresh()
+    {
+        SetupUserInfoAndPost();
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Test content.", [], null, PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        _oauthService.Verify(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task PublishAsync_TextPost_CreatesCorrectPayload()
+    {
+        string? capturedBody = null;
+        var callCount = 0;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                callCount++;
+                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
+                {
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+                }
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                var response = new HttpResponseMessage(HttpStatusCode.Created);
+                response.Headers.Add("x-restli-id", "urn:li:share:12345");
+                return response;
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Hello LinkedIn!", [], null, PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        Assert.Contains("\"author\":\"urn:li:person:person123\"", capturedBody);
+        Assert.Contains("\"commentary\":\"Hello LinkedIn!\"", capturedBody);
+        Assert.Contains("\"visibility\":\"PUBLIC\"", capturedBody);
+        Assert.Contains("\"lifecycleState\":\"PUBLISHED\"", capturedBody);
+    }
+
+    [Fact]
+    public async Task PublishAsync_WithArticleLink_IncludesContentObject()
+    {
+        string? capturedBody = null;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
+                {
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+                }
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                var response = new HttpResponseMessage(HttpStatusCode.Created);
+                response.Headers.Add("x-restli-id", "urn:li:share:12345");
+                return response;
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent("My Article"), "Check out my article.", [],
+            "https://matthewkruczek.ai/posts/test", PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        Assert.Contains("https://matthewkruczek.ai/posts/test", capturedBody);
+        Assert.Contains("My Article", capturedBody);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ReturnsPostUrnFromResponseHeader()
+    {
+        SetupUserInfoAndPost();
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Content.", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.True(result.Success);
+        Assert.Equal("urn:li:share:12345", result.PlatformPostId);
+        Assert.Contains("urn:li:share:12345", result.PublishedUrl!);
+    }
+
+    [Fact]
+    public async Task PublishAsync_HttpError_ReturnsFailure()
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
+            {
+                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
+                {
+                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    });
+                }
+
+                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { status = 403, message = "Insufficient permissions" }),
+                        System.Text.Encoding.UTF8, "application/json")
+                });
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Content.", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.NotNull(result.ErrorMessage);
+    }
+
+    [Fact]
+    public async Task PublishAsync_IncludesVersionHeader()
+    {
+        HttpRequestMessage? capturedRequest = null;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
+            {
+                if (req.RequestUri?.PathAndQuery == "/v2/userinfo")
+                {
+                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { sub = "person123", name = "Test User" }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    });
+                }
+
+                capturedRequest = req;
+                var response = new HttpResponseMessage(HttpStatusCode.Created);
+                response.Headers.Add("x-restli-id", "urn:li:share:12345");
+                return Task.FromResult(response);
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Content.", [], null, PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedRequest);
+        Assert.Equal("2.0.0", capturedRequest.Headers.GetValues("X-Restli-Protocol-Version").First());
+        Assert.Equal("202604", capturedRequest.Headers.GetValues("LinkedIn-Version").First());
+    }
+
+    [Fact]
+    public async Task ValidateCredentialsAsync_ValidToken_ReturnsTrue()
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
+            {
+                Content = new StringContent(
+                    JsonSerializer.Serialize(new { sub = "person123", name = "Test" }),
+                    System.Text.Encoding.UTF8, "application/json")
+            });
+
+        var connector = CreateConnector();
+        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);
+
+        Assert.True(result);
+    }
+
+    [Fact]
+    public async Task ValidateCredentialsAsync_ExpiredToken_ReturnsFalse()
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));
+
+        var connector = CreateConnector();
+        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);
+
+        Assert.False(result);
+    }
+
+    [Fact]
+    public void GetCapabilities_ReturnsCorrectValues()
+    {
+        var connector = CreateConnector();
+        var caps = connector.GetCapabilities();
+
+        Assert.Equal(3000, caps.MaxCharacters);
+        Assert.False(caps.SupportsMarkdown);
+        Assert.False(caps.SupportsHtml);
+        Assert.True(caps.SupportsImages);
+        Assert.False(caps.SupportsScheduling);
+        Assert.False(caps.SupportsThreads);
+    }
+
+    public void Dispose()
+    {
+        _httpClient.Dispose();
+        _dbContext.Dispose();
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/LinkedInFormatterTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/LinkedInFormatterTests.cs
new file mode 100644
index 0000000..4f10d63
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/LinkedInFormatterTests.cs
@@ -0,0 +1,129 @@
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class LinkedInFormatterTests
+{
+    private readonly LinkedInFormatter _formatter = new();
+
+    private static PreprocessedContent CreateContent(string body, string? canonicalUrl = null) => new(
+        Title: "Test Post",
+        Body: body,
+        CanonicalUrl: canonicalUrl,
+        Tags: [],
+        Images: []);
+
+    [Fact]
+    public async Task Format_StripMarkdown_ToPlainText()
+    {
+        var content = CreateContent(
+            "## Heading\n\n**Bold text** and *italic text*.\n\n[A link](https://example.com)\n\n- Item 1\n- Item 2");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("Heading", result);
+        Assert.DoesNotContain("##", result);
+        Assert.Contains("Bold text", result);
+        Assert.DoesNotContain("**", result);
+        Assert.Contains("italic text", result);
+        Assert.Contains("A link", result);
+        Assert.DoesNotContain("](", result);
+        Assert.Contains("- Item 1", result);
+        Assert.Contains("- Item 2", result);
+    }
+
+    [Fact]
+    public async Task Format_PreservesLineBreaksAndBullets()
+    {
+        var content = CreateContent("First paragraph.\n\nSecond paragraph.\n\n- Bullet one\n- Bullet two");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("First paragraph.\n\nSecond paragraph.", result);
+        Assert.Contains("- Bullet one\n- Bullet two", result);
+    }
+
+    [Fact]
+    public async Task Format_TruncatesTo3000Chars_WithEllipsis()
+    {
+        var longBody = new string('A', 3500);
+        var content = CreateContent(longBody, "https://matthewkruczek.ai/posts/long-post");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.True(result.Length <= 3000);
+        Assert.Contains("...", result);
+        Assert.Contains("Read more: https://matthewkruczek.ai/posts/long-post", result);
+    }
+
+    [Fact]
+    public async Task Format_AddsReadMoreLink_WhenTruncated()
+    {
+        var longBody = new string('W', 100) + " " + new string('X', 3400);
+        var content = CreateContent(longBody, "https://matthewkruczek.ai/posts/test");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.True(result.Length <= 3000);
+        Assert.Contains("Read more: https://matthewkruczek.ai/posts/test", result);
+    }
+
+    [Fact]
+    public async Task Format_Under3000Chars_NoTruncation()
+    {
+        var content = CreateContent("Short content here.");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Equal("Short content here.", result);
+        Assert.DoesNotContain("...", result);
+        Assert.DoesNotContain("Read more", result);
+    }
+
+    [Fact]
+    public async Task Format_NullCanonicalUrl_TruncatesWithoutReadMore()
+    {
+        var longBody = new string('A', 3500);
+        var content = CreateContent(longBody, null);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.True(result.Length <= 3000);
+        Assert.EndsWith("...", result);
+        Assert.DoesNotContain("Read more", result);
+    }
+
+    [Fact]
+    public async Task Format_CodeBlocks_ConvertToPlainText()
+    {
+        var content = CreateContent("Before code.\n\n```csharp\nvar x = 1;\n```\n\nAfter code.");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("var x = 1;", result);
+        Assert.DoesNotContain("```", result);
+        Assert.DoesNotContain("csharp", result);
+    }
+
+    [Fact]
+    public async Task Format_Images_Stripped()
+    {
+        var content = CreateContent("Before image.\n\n![alt text](https://example.com/img.png)\n\nAfter image.");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.DoesNotContain("![", result);
+        Assert.DoesNotContain("img.png", result);
+        Assert.Contains("Before image.", result);
+        Assert.Contains("After image.", result);
+    }
+
+    [Fact]
+    public void Format_Platform_ReturnsLinkedIn()
+    {
+        Assert.Equal(Platform.LinkedIn, _formatter.Platform);
+    }
+}
