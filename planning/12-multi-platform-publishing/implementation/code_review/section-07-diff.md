diff --git a/src/PBA.Infrastructure/Connectors/MediumConnector.cs b/src/PBA.Infrastructure/Connectors/MediumConnector.cs
new file mode 100644
index 0000000..508eba5
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/MediumConnector.cs
@@ -0,0 +1,152 @@
+using System.Net.Http.Headers;
+using System.Text;
+using System.Text.Json;
+using System.Text.Json.Serialization;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed class MediumConnector(
+    HttpClient httpClient,
+    IAppDbContext db,
+    ITokenEncryptor encryptor,
+    IOptions<MediumOptions> options,
+    ILogger<MediumConnector> logger) : IPlatformConnector
+{
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
+    };
+
+    private readonly MediumOptions _options = options.Value;
+    private string? _cachedUserId;
+
+    public Platform Platform => Platform.Medium;
+
+    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
+    {
+        try
+        {
+            var token = await GetDecryptedTokenAsync(ct);
+            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
+
+            var userId = await GetUserIdAsync(ct);
+            if (userId is null)
+                return new PlatformPublishResult(false, null, null,
+                    "Medium integration token is invalid or expired. Please reconfigure in Settings.");
+
+            var publishStatus = request.Mode switch
+            {
+                PublishMode.Publish => "public",
+                PublishMode.Schedule => "draft",
+                _ => "draft"
+            };
+
+            var tags = request.Tags
+                .Take(3)
+                .Select(t => t.Length > 25 ? t[..25] : t)
+                .ToList();
+
+            var payload = new
+            {
+                title = request.Content.Title,
+                contentFormat = "markdown",
+                content = request.TransformedContent,
+                tags,
+                canonicalUrl = request.CanonicalUrl,
+                publishStatus
+            };
+
+            var json = JsonSerializer.Serialize(payload, JsonOptions);
+            using var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/users/{userId}/posts")
+            {
+                Content = new StringContent(json, Encoding.UTF8, "application/json")
+            };
+
+            var response = await httpClient.SendAsync(postRequest, ct);
+
+            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
+                return new PlatformPublishResult(false, null, null, "Medium rate limit exceeded. Retry scheduled.");
+
+            if (!response.IsSuccessStatusCode)
+            {
+                var errorBody = await response.Content.ReadAsStringAsync(ct);
+                logger.LogError("Medium publish failed: {Status} {Body}", response.StatusCode, errorBody);
+                return new PlatformPublishResult(false, null, null, $"Medium publish failed ({response.StatusCode})");
+            }
+
+            var responseJson = await response.Content.ReadAsStringAsync(ct);
+            var postData = JsonSerializer.Deserialize<MediumResponse<MediumPost>>(responseJson, JsonOptions);
+
+            return new PlatformPublishResult(
+                true,
+                postData?.Data?.Url,
+                postData?.Data?.Id,
+                null);
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "Failed to publish to Medium");
+            return new PlatformPublishResult(false, null, null, ex.Message);
+        }
+    }
+
+    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
+    {
+        try
+        {
+            var token = await GetDecryptedTokenAsync(ct);
+            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
+            return await GetUserIdAsync(ct) is not null;
+        }
+        catch
+        {
+            return false;
+        }
+    }
+
+    public PlatformCapabilities GetCapabilities() => new(
+        MaxCharacters: int.MaxValue,
+        SupportsMarkdown: true,
+        SupportsHtml: true,
+        SupportsImages: true,
+        SupportsScheduling: false,
+        SupportsThreads: false,
+        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif"]
+    );
+
+    private async Task<string?> GetUserIdAsync(CancellationToken ct)
+    {
+        if (_cachedUserId is not null)
+            return _cachedUserId;
+
+        var response = await httpClient.GetAsync("/v1/me", ct);
+        if (!response.IsSuccessStatusCode)
+            return null;
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var userData = JsonSerializer.Deserialize<MediumResponse<MediumUser>>(json, JsonOptions);
+        _cachedUserId = userData?.Data?.Id;
+        return _cachedUserId;
+    }
+
+    private async Task<string> GetDecryptedTokenAsync(CancellationToken ct)
+    {
+        var credential = await db.PlatformCredentials
+            .FirstOrDefaultAsync(c => c.Platform == Platform.Medium && c.IsActive, ct)
+            ?? throw new InvalidOperationException("No active Medium credential found");
+
+        return encryptor.Decrypt(credential.EncryptedIntegrationToken
+            ?? throw new InvalidOperationException("Medium credential has no integration token"));
+    }
+
+    internal record MediumResponse<T>(T? Data);
+    internal record MediumUser(string Id, string? Username, string? Name, string? Url);
+    internal record MediumPost(string Id, string? Title, string? Url, string? CanonicalUrl, string? PublishStatus);
+}
diff --git a/src/PBA.Infrastructure/Connectors/MediumFormatter.cs b/src/PBA.Infrastructure/Connectors/MediumFormatter.cs
new file mode 100644
index 0000000..0b78a63
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/MediumFormatter.cs
@@ -0,0 +1,44 @@
+using System.Text.RegularExpressions;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed partial class MediumFormatter : IPlatformFormatter
+{
+    public Platform Platform => Platform.Medium;
+
+    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
+    {
+        var body = content.Body;
+
+        body = ResolveRelativeImages(body, content.Images);
+        body = SvgToPng().Replace(body, ".png)");
+
+        if (!string.IsNullOrEmpty(content.CanonicalUrl))
+        {
+            var host = new Uri(content.CanonicalUrl).Host;
+            body += $"\n\n---\n*Originally published at [{host}]({content.CanonicalUrl})*";
+        }
+
+        return Task.FromResult(body);
+    }
+
+    private static string ResolveRelativeImages(string body, IReadOnlyList<ImageReference> images)
+    {
+        foreach (var image in images)
+        {
+            if (!image.OriginalPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
+            {
+                body = body.Replace(
+                    $"({image.OriginalPath})",
+                    $"({image.AbsoluteUrl})");
+            }
+        }
+        return body;
+    }
+
+    [GeneratedRegex(@"\.svg\)")]
+    private static partial Regex SvgToPng();
+}
diff --git a/src/PBA.Infrastructure/Connectors/MediumOptions.cs b/src/PBA.Infrastructure/Connectors/MediumOptions.cs
new file mode 100644
index 0000000..6df4470
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/MediumOptions.cs
@@ -0,0 +1,9 @@
+namespace PBA.Infrastructure.Connectors;
+
+public sealed class MediumOptions
+{
+    public const string SectionName = "Publishing:Medium";
+
+    public bool Enabled { get; init; }
+    public string DefaultPublishStatus { get; init; } = "draft";
+}
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/MediumConnectorTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/MediumConnectorTests.cs
new file mode 100644
index 0000000..9f52dfe
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/MediumConnectorTests.cs
@@ -0,0 +1,389 @@
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
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class MediumConnectorTests : IDisposable
+{
+    private readonly ApplicationDbContext _dbContext;
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly Mock<ILogger<MediumConnector>> _logger = new();
+
+    private readonly MediumOptions _mediumOptions = new()
+    {
+        Enabled = true,
+        DefaultPublishStatus = "draft"
+    };
+
+    public MediumConnectorTests()
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
+            BaseAddress = new Uri("https://api.medium.com")
+        };
+        _httpClient.DefaultRequestHeaders.Accept.Add(
+            new MediaTypeWithQualityHeaderValue("application/json"));
+
+        SeedCredential();
+    }
+
+    private void SeedCredential()
+    {
+        _dbContext.PlatformCredentials.Add(new PlatformCredential
+        {
+            Platform = Platform.Medium,
+            EncryptedIntegrationToken = "encrypted:test-medium-token",
+            IsActive = true
+        });
+        _dbContext.SaveChanges();
+    }
+
+    private MediumConnector CreateConnector() => new(
+        _httpClient,
+        _dbContext,
+        _encryptor.Object,
+        Options.Create(_mediumOptions),
+        _logger.Object);
+
+    private static Content CreateContent(string title = "Test Post") => new()
+    {
+        Id = Guid.NewGuid(),
+        Title = title,
+        Body = "Test body content",
+        Status = ContentStatus.Approved,
+        PrimaryPlatform = Platform.Medium
+    };
+
+    private void SetupHttpSequence(params (string responseJson, HttpStatusCode status)[] responses)
+    {
+        var sequence = _httpHandler.Protected()
+            .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>());
+
+        foreach (var (responseJson, status) in responses)
+        {
+            sequence.ReturnsAsync(new HttpResponseMessage(status)
+            {
+                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
+            });
+        }
+    }
+
+    private string? SetupMeAndPostWithCapture()
+    {
+        string? capturedBody = null;
+        var callCount = 0;
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                callCount++;
+                if (callCount == 1)
+                {
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+                }
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        return capturedBody;
+    }
+
+    [Fact]
+    public async Task PublishAsync_Draft_SendsCorrectPayload()
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
+                if (callCount == 1)
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "# My Post\n\nBody here.", ["AI", "Tech"], null, PublishMode.Draft, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        Assert.Contains("\"publishStatus\":\"draft\"", capturedBody);
+        Assert.Contains("\"contentFormat\":\"markdown\"", capturedBody);
+    }
+
+    [Fact]
+    public async Task PublishAsync_Public_SendsCorrectStatus()
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
+                if (callCount == 1)
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Body.", [], null, PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        Assert.Contains("\"publishStatus\":\"public\"", capturedBody);
+    }
+
+    [Fact]
+    public async Task PublishAsync_TruncatesTags_ToMax3()
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
+                if (callCount == 1)
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Body.", ["AI", "Engineering", "CSharp", "Web", "DevOps"], null, PublishMode.Draft, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        var doc = JsonDocument.Parse(capturedBody);
+        var tags = doc.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList();
+        Assert.Equal(3, tags.Count);
+        Assert.Equal(["AI", "Engineering", "CSharp"], tags);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ReturnsPublishedUrlFromResponse()
+    {
+        SetupHttpSequence(
+            (JsonSerializer.Serialize(new { data = new { id = "user123" } }), HttpStatusCode.OK),
+            (JsonSerializer.Serialize(new { data = new { id = "abc123", title = "Test Post", url = "https://medium.com/@user/title-abc123", publishStatus = "public" } }), HttpStatusCode.Created)
+        );
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Body.", [], null, PublishMode.Publish, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.True(result.Success);
+        Assert.Equal("https://medium.com/@user/title-abc123", result.PublishedUrl);
+        Assert.Equal("abc123", result.PlatformPostId);
+    }
+
+    [Fact]
+    public async Task PublishAsync_IncludesCanonicalUrl()
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
+                if (callCount == 1)
+                    return new HttpResponseMessage(HttpStatusCode.OK)
+                    {
+                        Content = new StringContent(
+                            JsonSerializer.Serialize(new { data = new { id = "user123" } }),
+                            System.Text.Encoding.UTF8, "application/json")
+                    };
+
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                return new HttpResponseMessage(HttpStatusCode.Created)
+                {
+                    Content = new StringContent(
+                        JsonSerializer.Serialize(new { data = new { id = "post1", title = "Test", url = "https://medium.com/@user/test" } }),
+                        System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Body.", [], "https://matthewkruczek.ai/posts/test", PublishMode.Publish, null);
+
+        await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        Assert.Contains("https://matthewkruczek.ai/posts/test", capturedBody);
+    }
+
+    [Fact]
+    public async Task PublishAsync_InvalidToken_ReturnsFailureResult()
+    {
+        SetupHttpSequence(
+            (JsonSerializer.Serialize(new { errors = new[] { new { message = "Token was invalid.", code = 6003 } } }), HttpStatusCode.Unauthorized)
+        );
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Body.", [], null, PublishMode.Draft, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.Contains("invalid", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RateLimited_ReturnsFailureResult()
+    {
+        SetupHttpSequence(
+            (JsonSerializer.Serialize(new { data = new { id = "user123" } }), HttpStatusCode.OK),
+            (JsonSerializer.Serialize(new { errors = new[] { new { message = "Rate limit exceeded", code = 429 } } }), HttpStatusCode.TooManyRequests)
+        );
+
+        var connector = CreateConnector();
+        var request = new PlatformPublishRequest(
+            CreateContent(), "Body.", [], null, PublishMode.Draft, null);
+
+        var result = await connector.PublishAsync(request, CancellationToken.None);
+
+        Assert.False(result.Success);
+        Assert.Contains("rate limit", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task ValidateCredentialsAsync_ValidToken_ReturnsTrue()
+    {
+        SetupHttpSequence(
+            (JsonSerializer.Serialize(new { data = new { id = "user123", username = "test" } }), HttpStatusCode.OK)
+        );
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
+        SetupHttpSequence(
+            ("{\"errors\":[{\"message\":\"Token was invalid.\"}]}", HttpStatusCode.Unauthorized)
+        );
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
+        Assert.Equal(int.MaxValue, caps.MaxCharacters);
+        Assert.True(caps.SupportsMarkdown);
+        Assert.True(caps.SupportsHtml);
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
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/MediumFormatterTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/MediumFormatterTests.cs
new file mode 100644
index 0000000..8368955
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/MediumFormatterTests.cs
@@ -0,0 +1,81 @@
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class MediumFormatterTests
+{
+    private readonly MediumFormatter _formatter = new();
+
+    [Fact]
+    public async Task FormatAsync_InjectsCanonicalUrlFooter()
+    {
+        var content = new PreprocessedContent(
+            "Test Post", "Some body text.",
+            "https://matthewkruczek.ai/posts/my-post",
+            [], []);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.EndsWith(
+            "\n\n---\n*Originally published at [matthewkruczek.ai](https://matthewkruczek.ai/posts/my-post)*",
+            result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_NullCanonicalUrl_OmitsFooter()
+    {
+        var content = new PreprocessedContent(
+            "Test Post", "Some body text.", null, [], []);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.DoesNotContain("Originally published", result);
+        Assert.Equal("Some body text.", result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_ConvertsSvgReferences_ToPng()
+    {
+        var content = new PreprocessedContent(
+            "Test", "![diagram](https://example.com/chart.svg)",
+            null, [], []);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("https://example.com/chart.png", result);
+        Assert.DoesNotContain(".svg", result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_ResolvesRelativeImageUrls_ToAbsolute()
+    {
+        var content = new PreprocessedContent(
+            "Test", "![alt](images/photo.png)",
+            "https://matthewkruczek.ai/posts/my-post",
+            [], [new ImageReference("images/photo.png", "https://matthewkruczek.ai/images/photo.png", "alt")]);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Contains("![alt](https://matthewkruczek.ai/images/photo.png)", result);
+    }
+
+    [Fact]
+    public async Task FormatAsync_PreservesMarkdownFormat()
+    {
+        var body = "# Heading\n\n**bold** and *italic*\n\n```csharp\nvar x = 1;\n```\n\n[link](https://example.com)";
+        var content = new PreprocessedContent("Test", body, null, [], []);
+
+        var result = await _formatter.FormatAsync(content, CancellationToken.None);
+
+        Assert.Equal(body, result);
+    }
+
+    [Fact]
+    public void Platform_ReturnsMedium()
+    {
+        Assert.Equal(Platform.Medium, _formatter.Platform);
+    }
+}
