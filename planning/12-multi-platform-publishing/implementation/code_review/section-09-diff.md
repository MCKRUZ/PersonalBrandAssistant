diff --git a/src/PBA.Infrastructure/Connectors/TwitterConnector.cs b/src/PBA.Infrastructure/Connectors/TwitterConnector.cs
new file mode 100644
index 0000000..4f5c425
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/TwitterConnector.cs
@@ -0,0 +1,201 @@
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
+public sealed class TwitterConnector(
+    HttpClient httpClient,
+    IAppDbContext db,
+    ITokenEncryptor encryptor,
+    IOAuthService oauthService,
+    IOptionsMonitor<TwitterOptions> options,
+    ILogger<TwitterConnector> logger) : IPlatformConnector
+{
+    private readonly IOptionsMonitor<TwitterOptions> _options = options;
+    private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(10);
+
+    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
+        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
+    };
+
+    public Platform Platform => Platform.Twitter;
+
+    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
+    {
+        try
+        {
+            if (request.Mode is PublishMode.Draft or PublishMode.Schedule)
+                return new PlatformPublishResult(false, null, null,
+                    "Twitter does not support draft or scheduled posts via API.");
+
+            var credential = await GetActiveCredentialAsync(ct);
+            var token = await GetValidTokenAsync(credential, ct);
+            if (token is null)
+                return new PlatformPublishResult(false, null, null,
+                    "Twitter token refresh failed. Please reconnect in Settings.");
+
+            var segments = ParseSegments(request.TransformedContent);
+
+            string? firstTweetId = null;
+            string? previousTweetId = null;
+
+            foreach (var segment in segments)
+            {
+                var tweetPayload = BuildTweetPayload(segment, previousTweetId);
+                var json = JsonSerializer.Serialize(tweetPayload, JsonOptions);
+
+                using var tweetRequest = new HttpRequestMessage(HttpMethod.Post, "/2/tweets")
+                {
+                    Content = new StringContent(json, Encoding.UTF8, "application/json")
+                };
+                tweetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
+
+                var response = await httpClient.SendAsync(tweetRequest, ct);
+
+                if (response.StatusCode == HttpStatusCode.TooManyRequests)
+                    return new PlatformPublishResult(false, null, null,
+                        "Twitter rate limit exceeded. Retry scheduled.");
+
+                if (response.StatusCode == HttpStatusCode.Unauthorized)
+                    return new PlatformPublishResult(false, null, null,
+                        "Twitter authentication failed. Please reconnect in Settings.");
+
+                if (response.StatusCode == HttpStatusCode.Forbidden)
+                {
+                    logger.LogError("Twitter 403 on tweet creation - permissions issue");
+                    return new PlatformPublishResult(false, null, null,
+                        "Twitter API access denied. Check app permissions.");
+                }
+
+                if (!response.IsSuccessStatusCode)
+                {
+                    var errorBody = await response.Content.ReadAsStringAsync(ct);
+                    logger.LogError("Twitter publish failed: {Status} {Body}", response.StatusCode, errorBody);
+                    return new PlatformPublishResult(false, null, null,
+                        $"Twitter publish failed ({response.StatusCode})");
+                }
+
+                var responseJson = await response.Content.ReadAsStringAsync(ct);
+                var tweetResponse = JsonSerializer.Deserialize<TwitterTweetResponse>(responseJson, JsonOptions);
+
+                if (tweetResponse?.Data?.Id is null)
+                    return new PlatformPublishResult(false, null, null,
+                        "Twitter returned an unexpected response format.");
+
+                previousTweetId = tweetResponse.Data.Id;
+                firstTweetId ??= previousTweetId;
+            }
+
+            var publishedUrl = $"https://x.com/i/status/{firstTweetId}";
+            return new PlatformPublishResult(true, publishedUrl, firstTweetId, null);
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "Failed to publish to Twitter");
+            return new PlatformPublishResult(false, null, null,
+                "An unexpected error occurred while publishing to Twitter. Check logs for details.");
+        }
+    }
+
+    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
+    {
+        try
+        {
+            var credential = await GetActiveCredentialAsync(ct);
+            var token = await GetValidTokenAsync(credential, ct);
+            if (token is null) return false;
+
+            using var request = new HttpRequestMessage(HttpMethod.Get, "/2/users/me");
+            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
+
+            var response = await httpClient.SendAsync(request, ct);
+            return response.StatusCode == HttpStatusCode.OK;
+        }
+        catch (Exception ex)
+        {
+            logger.LogWarning(ex, "Twitter credential validation failed");
+            return false;
+        }
+    }
+
+    public PlatformCapabilities GetCapabilities() => new(
+        MaxCharacters: 280,
+        SupportsMarkdown: false,
+        SupportsHtml: false,
+        SupportsImages: true,
+        SupportsScheduling: false,
+        SupportsThreads: true,
+        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "video/mp4"]
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
+                logger.LogWarning("Twitter token refresh failed: {Errors}", string.Join(", ", refreshResult.Errors));
+                return null;
+            }
+            token = refreshResult.Value!;
+        }
+
+        return token;
+    }
+
+    private async Task<PlatformCredential> GetActiveCredentialAsync(CancellationToken ct)
+    {
+        return await db.PlatformCredentials
+            .FirstOrDefaultAsync(c => c.Platform == Platform.Twitter && c.IsActive, ct)
+            ?? throw new InvalidOperationException("No active Twitter credential found");
+    }
+
+    private static List<string> ParseSegments(string transformedContent)
+    {
+        if (transformedContent.StartsWith('['))
+        {
+            try
+            {
+                var segments = JsonSerializer.Deserialize<string[]>(transformedContent);
+                if (segments is { Length: > 1 })
+                    return [.. segments];
+            }
+            catch (JsonException)
+            {
+            }
+        }
+
+        return [transformedContent];
+    }
+
+    private static object BuildTweetPayload(string text, string? inReplyToId)
+    {
+        if (inReplyToId is not null)
+            return new { text, reply = new { in_reply_to_tweet_id = inReplyToId } };
+
+        return new { text };
+    }
+
+    internal record TwitterTweetResponse(TwitterTweetData? Data);
+    internal record TwitterTweetData(string Id, string? Text);
+    internal record TwitterUserResponse(TwitterUserData? Data);
+    internal record TwitterUserData(string Id, string Name, string Username);
+    internal record TwitterErrorResponse(string? Title, int? Status, string? Detail);
+}
diff --git a/src/PBA.Infrastructure/Connectors/TwitterFormatter.cs b/src/PBA.Infrastructure/Connectors/TwitterFormatter.cs
new file mode 100644
index 0000000..f193102
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/TwitterFormatter.cs
@@ -0,0 +1,156 @@
+using System.Text.Json;
+using System.Text.RegularExpressions;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed partial class TwitterFormatter : IPlatformFormatter
+{
+    private const int MaxCharacters = 280;
+    private const int TcoUrlLength = 23;
+
+    public Platform Platform => Platform.Twitter;
+
+    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
+    {
+        var body = StripMarkdown(content.Body);
+
+        var effectiveUrlLength = !string.IsNullOrEmpty(content.CanonicalUrl) ? TcoUrlLength + 1 : 0;
+        var totalLength = body.Length + effectiveUrlLength;
+
+        if (totalLength <= MaxCharacters)
+        {
+            if (!string.IsNullOrEmpty(content.CanonicalUrl))
+                body = $"{body}\n{content.CanonicalUrl}";
+            return Task.FromResult(body);
+        }
+
+        var segments = SplitIntoThread(body, content.CanonicalUrl);
+        var json = JsonSerializer.Serialize(segments);
+        return Task.FromResult(json);
+    }
+
+    private static string StripMarkdown(string text)
+    {
+        text = FencedCodeBlock().Replace(text, "$1");
+        text = ImagePattern().Replace(text, "");
+        text = LinkPattern().Replace(text, "$1");
+        text = BoldPattern().Replace(text, "$1");
+        text = ItalicPattern().Replace(text, "$1");
+        text = InlineCodePattern().Replace(text, "$1");
+        text = HeadingPattern().Replace(text, "$1");
+        text = BlockquotePattern().Replace(text, "$1");
+        text = HorizontalRulePattern().Replace(text, "");
+        text = CollapseBlankLines().Replace(text, "\n\n");
+        return text.Trim();
+    }
+
+    private static List<string> SplitIntoThread(string text, string? canonicalUrl)
+    {
+        var segments = new List<string>();
+        var estimatedCount = (int)Math.Ceiling((double)text.Length / (MaxCharacters - 10));
+        var remaining = text;
+
+        while (remaining.Length > 0)
+        {
+            var numberingSuffix = $" {segments.Count + 1}/{estimatedCount}";
+            var budget = MaxCharacters - numberingSuffix.Length;
+
+            if (remaining.Length <= budget)
+            {
+                segments.Add(remaining);
+                break;
+            }
+
+            var splitIndex = FindSentenceBoundary(remaining, budget);
+            var segment = remaining[..splitIndex].TrimEnd();
+            segments.Add(segment);
+            remaining = remaining[splitIndex..].TrimStart();
+        }
+
+        if (segments.Count > 1)
+        {
+            for (var i = 0; i < segments.Count; i++)
+            {
+                var suffix = $" {i + 1}/{segments.Count}";
+                if (segments[i].Length + suffix.Length <= MaxCharacters)
+                    segments[i] += suffix;
+            }
+        }
+
+        if (!string.IsNullOrEmpty(canonicalUrl))
+        {
+            var lastIdx = segments.Count - 1;
+            var urlAppend = $"\n{canonicalUrl}";
+            var urlBudget = TcoUrlLength + 1;
+
+            if (segments[lastIdx].Length + urlBudget <= MaxCharacters)
+            {
+                segments[lastIdx] += urlAppend;
+            }
+            else
+            {
+                segments.Add(canonicalUrl);
+            }
+        }
+
+        return segments;
+    }
+
+    private static int FindSentenceBoundary(string text, int maxLength)
+    {
+        var searchRegion = text[..Math.Min(maxLength, text.Length)];
+
+        var bestBreak = -1;
+        for (var i = searchRegion.Length - 1; i >= maxLength / 2; i--)
+        {
+            if (i < searchRegion.Length - 1 &&
+                IsSentenceEnd(searchRegion[i]) &&
+                searchRegion[i + 1] == ' ')
+            {
+                bestBreak = i + 1;
+                break;
+            }
+        }
+
+        if (bestBreak > 0)
+            return bestBreak;
+
+        var lastSpace = searchRegion.LastIndexOf(' ');
+        return lastSpace > maxLength / 2 ? lastSpace : maxLength;
+    }
+
+    private static bool IsSentenceEnd(char c) => c is '.' or '!' or '?';
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
+    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
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
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/TwitterConnectorTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/TwitterConnectorTests.cs
new file mode 100644
index 0000000..a463cf4
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/TwitterConnectorTests.cs
@@ -0,0 +1,362 @@
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
+public class TwitterConnectorTests : IDisposable
+{
+    private readonly ApplicationDbContext _dbContext;
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<IOAuthService> _oauthService = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly Mock<ILogger<TwitterConnector>> _logger = new();
+
+    private readonly TwitterOptions _twitterOptions = new()
+    {
+        Enabled = true,
+        ClientId = "test-client-id",
+        ClientSecret = "test-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/twitter/callback",
+    };
+
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
+    };
+
+    public TwitterConnectorTests()
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
+            BaseAddress = new Uri("https://api.x.com")
+        };
+        _httpClient.DefaultRequestHeaders.Accept.Add(
+            new MediaTypeWithQualityHeaderValue("application/json"));
+
+        SeedCredential(DateTimeOffset.UtcNow.AddDays(30));
+    }
+
+    private void SeedCredential(DateTimeOffset accessTokenExpiresAt)
+    {
+        foreach (var existing in _dbContext.PlatformCredentials.Where(c => c.Platform == Platform.Twitter))
+            _dbContext.PlatformCredentials.Remove(existing);
+        _dbContext.SaveChanges();
+
+        _dbContext.PlatformCredentials.Add(new PlatformCredential
+        {
+            Platform = Platform.Twitter,
+            EncryptedAccessToken = "encrypted:test-twitter-token",
+            EncryptedRefreshToken = "encrypted:test-refresh-token",
+            AccessTokenExpiresAt = accessTokenExpiresAt,
+            IsActive = true
+        });
+        _dbContext.SaveChanges();
+    }
+
+    private TwitterConnector CreateConnector()
+    {
+        var optionsMonitor = new Mock<IOptionsMonitor<TwitterOptions>>();
+        optionsMonitor.Setup(o => o.CurrentValue).Returns(_twitterOptions);
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
+        PrimaryPlatform = Platform.Twitter
+    };
+
+    private void SetupTweetPost(string tweetId = "12345")
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
+            {
+                var response = new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = tweetId, text = "Hello" } }, JsonOptions),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+                return Task.FromResult(response);
+            });
+    }
+
+    [Fact]
+    public async Task PublishAsync_SingleTweet_PostsOnce()
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
+                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = "12345", text = "Hello" } }, JsonOptions),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Hello world", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.True(result.Success);
+        Assert.Equal("12345", result.PlatformPostId);
+        Assert.Contains("12345", result.PublishedUrl!);
+        Assert.Equal(1, callCount);
+        Assert.NotNull(capturedBody);
+        Assert.Contains("Hello world", capturedBody);
+    }
+
+    [Fact]
+    public async Task PublishAsync_Thread_ChainsRepliesWithCorrectIds()
+    {
+        var callIndex = 0;
+        var capturedBodies = new List<string>();
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                var body = req.Content is not null ? await req.Content.ReadAsStringAsync() : "";
+                capturedBodies.Add(body);
+                var id = (100 + callIndex).ToString();
+                callIndex++;
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id, text = "segment" } }, JsonOptions),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        var threadContent = JsonSerializer.Serialize(new[] { "First segment", "Second segment", "Third segment" });
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), threadContent, [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.True(result.Success);
+        Assert.Equal(3, capturedBodies.Count);
+        Assert.Equal("100", result.PlatformPostId);
+
+        Assert.DoesNotContain("in_reply_to_tweet_id", capturedBodies[0]);
+        Assert.Contains("100", capturedBodies[1]);
+        Assert.Contains("101", capturedBodies[2]);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ReturnsFirstTweetIdForThreads()
+    {
+        var callIndex = 0;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
+            {
+                var id = callIndex == 0 ? "first-tweet" : "second-tweet";
+                callIndex++;
+                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id, text = "segment" } }, JsonOptions),
+                        System.Text.Encoding.UTF8, "application/json")
+                });
+            });
+
+        var threadContent = JsonSerializer.Serialize(new[] { "Segment one", "Segment two" });
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), threadContent, [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.Equal("first-tweet", result.PlatformPostId);
+        Assert.Contains("first-tweet", result.PublishedUrl!);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ExpiredToken_RefreshesBeforePublishing()
+    {
+        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
+        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Success("new-access-token"));
+
+        SetupTweetPost();
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
+    public async Task PublishAsync_TokenRefreshFails_ReturnsAuthFailure()
+    {
+        SeedCredential(DateTimeOffset.UtcNow.AddMinutes(3));
+        _oauthService.Setup(o => o.RefreshTokenAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Fail("Token refresh failed"));
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Content.", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.Contains("reconnect", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
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
+                    JsonSerializer.Serialize(new { data = new { id = "123", name = "Matt", username = "maboroshi_matt" } }, JsonOptions),
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
+    public async Task ValidateCredentialsAsync_InvalidToken_ReturnsFalse()
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
+        Assert.Equal(280, caps.MaxCharacters);
+        Assert.False(caps.SupportsMarkdown);
+        Assert.False(caps.SupportsHtml);
+        Assert.True(caps.SupportsImages);
+        Assert.False(caps.SupportsScheduling);
+        Assert.True(caps.SupportsThreads);
+        Assert.Contains("image/png", caps.SupportedMediaTypes);
+        Assert.Contains("image/jpeg", caps.SupportedMediaTypes);
+        Assert.Contains("image/gif", caps.SupportedMediaTypes);
+        Assert.Contains("video/mp4", caps.SupportedMediaTypes);
+    }
+
+    [Theory]
+    [InlineData(PublishMode.Draft)]
+    [InlineData(PublishMode.Schedule)]
+    public async Task PublishAsync_UnsupportedMode_ReturnsFailure(PublishMode mode)
+    {
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Content.", [], null, mode, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.Contains("does not support", result.ErrorMessage!);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RateLimited_ReturnsFailureWithMessage()
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(() =>
+            {
+                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { title = "Too Many Requests", status = 429, detail = "Rate limit exceeded" }, JsonOptions),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+                response.Headers.Add("x-rate-limit-reset", "1700000000");
+                return response;
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Content.", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.Contains("rate limit", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+    }
+
+    public void Dispose()
+    {
+        _httpClient.Dispose();
+        _dbContext.Dispose();
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/TwitterFormatterTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/TwitterFormatterTests.cs
new file mode 100644
index 0000000..e32bbb5
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/TwitterFormatterTests.cs
@@ -0,0 +1,145 @@
+using System.Text.Json;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class TwitterFormatterTests
+{
+    private readonly TwitterFormatter _formatter = new();
+
+    private static PreprocessedContent CreateContent(
+        string body,
+        string? canonicalUrl = null) => new(
+        Title: "Test Post",
+        Body: body,
+        CanonicalUrl: canonicalUrl,
+        Tags: [],
+        Images: []);
+
+    [Fact]
+    public async Task Format_Under280Chars_ReturnsSingleSegment()
+    {
+        var content = CreateContent("Short tweet about .NET 10");
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Equal("Short tweet about .NET 10", result);
+        Assert.DoesNotContain("[", result);
+    }
+
+    [Fact]
+    public async Task Format_Over280Chars_SplitsIntoThreadSegments()
+    {
+        var body = string.Join(". ", Enumerable.Range(1, 30)
+            .Select(i => $"This is sentence number {i} which adds length")) + ".";
+
+        var content = CreateContent(body);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        var segments = JsonSerializer.Deserialize<string[]>(result);
+        Assert.NotNull(segments);
+        Assert.True(segments.Length > 1);
+        foreach (var segment in segments)
+            Assert.True(segment.Length <= 280, $"Segment too long ({segment.Length}): {segment[..50]}...");
+    }
+
+    [Fact]
+    public async Task Format_SplitsAtSentenceBoundaries()
+    {
+        var sentences = Enumerable.Range(1, 20)
+            .Select(i => $"Sentence {i} has some words in it here")
+            .ToList();
+        var body = string.Join(". ", sentences) + ".";
+
+        var content = CreateContent(body);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        var segments = JsonSerializer.Deserialize<string[]>(result);
+        Assert.NotNull(segments);
+
+        foreach (var segment in segments)
+        {
+            var trimmed = segment.TrimEnd();
+            if (trimmed.Contains('.') && !trimmed.EndsWith('.'))
+            {
+                var afterLastPeriod = trimmed[(trimmed.LastIndexOf('.') + 1)..].Trim();
+                Assert.True(afterLastPeriod.Length < 50,
+                    "Segment should split near sentence boundaries");
+            }
+        }
+    }
+
+    [Fact]
+    public async Task Format_IncludesArticleLinkInLastSegment()
+    {
+        var body = string.Join(". ", Enumerable.Range(1, 30)
+            .Select(i => $"This is sentence number {i} which adds length")) + ".";
+        var url = "https://matthewkruczek.ai/posts/my-post";
+
+        var content = CreateContent(body, url);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        var segments = JsonSerializer.Deserialize<string[]>(result);
+        Assert.NotNull(segments);
+        Assert.Contains(url, segments[^1]);
+    }
+
+    [Fact]
+    public async Task Format_StripMarkdown_ToPlainText()
+    {
+        var body = "**Bold text** and *italic text* with [a link](https://example.com) and `code`\n## Heading";
+
+        var content = CreateContent(body);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.DoesNotContain("**", result);
+        Assert.DoesNotContain("*italic text*", result);
+        Assert.DoesNotContain("[a link]", result);
+        Assert.DoesNotContain("`code`", result);
+        Assert.DoesNotContain("## ", result);
+        Assert.Contains("Bold text", result);
+        Assert.Contains("italic text", result);
+        Assert.Contains("code", result);
+        Assert.Contains("Heading", result);
+    }
+
+    [Fact]
+    public async Task Format_PreservesHashtags()
+    {
+        var body = "Exploring #dotnet and #AI for enterprise solutions";
+
+        var content = CreateContent(body);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("#dotnet", result);
+        Assert.Contains("#AI", result);
+    }
+
+    [Fact]
+    public void Platform_ReturnsTwitter()
+    {
+        Assert.Equal(Platform.Twitter, _formatter.Platform);
+    }
+
+    [Fact]
+    public async Task Format_SingleTweetWithUrl_BudgetsTcoLength()
+    {
+        var textPart = new string('A', 250);
+        var url = "https://matthewkruczek.ai/posts/very-long-slug-name-here";
+
+        var content = CreateContent(textPart, url);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains(url, result);
+        Assert.DoesNotContain("[", result);
+    }
+}
