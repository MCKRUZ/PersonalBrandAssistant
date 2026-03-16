diff --git a/src/PersonalBrandAssistant.Api/Endpoints/PlatformEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/PlatformEndpoints.cs
new file mode 100644
index 0000000..e5d985e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/PlatformEndpoints.cs
@@ -0,0 +1,128 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class PlatformEndpoints
+{
+    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/platforms").WithTags("Platforms");
+
+        group.MapGet("/", ListPlatforms);
+        group.MapGet("/{type}/auth-url", GetAuthUrl);
+        group.MapPost("/{type}/callback", HandleCallback);
+        group.MapDelete("/{type}/disconnect", Disconnect);
+        group.MapGet("/{type}/status", GetStatus);
+        group.MapPost("/{type}/test-post", TestPost);
+        group.MapGet("/{type}/engagement/{postId}", GetEngagement);
+    }
+
+    private static async Task<IResult> ListPlatforms(IApplicationDbContext db)
+    {
+        var platforms = await db.Platforms
+            .Select(p => new
+            {
+                p.Type,
+                p.IsConnected,
+                p.DisplayName,
+            })
+            .ToListAsync();
+
+        return Results.Ok(platforms);
+    }
+
+    private static async Task<IResult> GetAuthUrl(
+        string type, IOAuthManager oauthManager, CancellationToken ct)
+    {
+        if (!TryParsePlatform(type, out var platformType))
+            return InvalidPlatformResult(type);
+
+        var result = await oauthManager.GenerateAuthUrlAsync(platformType, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> HandleCallback(
+        string type, OAuthCallbackRequest body, IOAuthManager oauthManager, CancellationToken ct)
+    {
+        if (!TryParsePlatform(type, out var platformType))
+            return InvalidPlatformResult(type);
+
+        var result = await oauthManager.ExchangeCodeAsync(
+            platformType, body.Code, body.State, body.CodeVerifier, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> Disconnect(
+        string type, IOAuthManager oauthManager, CancellationToken ct)
+    {
+        if (!TryParsePlatform(type, out var platformType))
+            return InvalidPlatformResult(type);
+
+        var result = await oauthManager.RevokeTokenAsync(platformType, ct);
+        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetStatus(
+        string type, IApplicationDbContext db, CancellationToken ct)
+    {
+        if (!TryParsePlatform(type, out var platformType))
+            return InvalidPlatformResult(type);
+
+        var platform = await db.Platforms
+            .FirstOrDefaultAsync(p => p.Type == platformType, ct);
+
+        if (platform is null)
+            return Results.NotFound($"Platform '{type}' not configured");
+
+        return Results.Ok(new
+        {
+            platform.IsConnected,
+            platform.DisplayName,
+            platform.Type,
+        });
+    }
+
+    private static async Task<IResult> TestPost(
+        string type, IEnumerable<ISocialPlatform> adapters, CancellationToken ct)
+    {
+        if (!TryParsePlatform(type, out var platformType))
+            return InvalidPlatformResult(type);
+
+        var adapter = adapters.FirstOrDefault(a => a.Type == platformType);
+        if (adapter is null)
+            return Results.NotFound($"No adapter for platform '{type}'");
+
+        var content = new PlatformContent(
+            $"Test post from Personal Brand Assistant - {DateTime.UtcNow:O}",
+            null, ContentType.SocialPost, [], new Dictionary<string, string>());
+
+        var result = await adapter.PublishAsync(content, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetEngagement(
+        string type, string postId, IEnumerable<ISocialPlatform> adapters, CancellationToken ct)
+    {
+        if (!TryParsePlatform(type, out var platformType))
+            return InvalidPlatformResult(type);
+
+        var adapter = adapters.FirstOrDefault(a => a.Type == platformType);
+        if (adapter is null)
+            return Results.NotFound($"No adapter for platform '{type}'");
+
+        var result = await adapter.GetEngagementAsync(postId, ct);
+        return result.ToHttpResult();
+    }
+
+    private static bool TryParsePlatform(string type, out PlatformType platformType) =>
+        Enum.TryParse(type, ignoreCase: true, out platformType);
+
+    private static IResult InvalidPlatformResult(string type) =>
+        Results.BadRequest($"Invalid platform type: {type}. Valid values: {string.Join(", ", Enum.GetNames<PlatformType>())}");
+}
+
+public record OAuthCallbackRequest(string Code, string? CodeVerifier, string State);
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index 89a7911..2ba451e 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -59,6 +59,7 @@ app.MapSchedulingEndpoints();
 app.MapNotificationEndpoints();
 app.MapAgentEndpoints();
 app.MapMediaEndpoints();
+app.MapPlatformEndpoints();
 
 app.Run();
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/PlatformEndpointsTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/PlatformEndpointsTests.cs
new file mode 100644
index 0000000..6d3f3f8
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/PlatformEndpointsTests.cs
@@ -0,0 +1,148 @@
+using Microsoft.AspNetCore.Builder;
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using Moq;
+using PersonalBrandAssistant.Api.Endpoints;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+using System.Net;
+using System.Net.Http.Json;
+using MediatR;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class PlatformEndpointsTests
+{
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<ISocialPlatform> _twitterAdapter = new();
+
+    public PlatformEndpointsTests()
+    {
+        _twitterAdapter.Setup(a => a.Type).Returns(PlatformType.TwitterX);
+    }
+
+    [Fact]
+    public void OAuthCallbackRequest_HasExpectedProperties()
+    {
+        var request = new OAuthCallbackRequest("code123", "verifier", "state456");
+        Assert.Equal("code123", request.Code);
+        Assert.Equal("verifier", request.CodeVerifier);
+        Assert.Equal("state456", request.State);
+    }
+
+    [Fact]
+    public void OAuthCallbackRequest_CodeVerifier_IsNullable()
+    {
+        var request = new OAuthCallbackRequest("code123", null, "state456");
+        Assert.Null(request.CodeVerifier);
+    }
+
+    [Fact]
+    public async Task GenerateAuthUrl_CallsOAuthManager()
+    {
+        var expectedUrl = new OAuthAuthorizationUrl("https://twitter.com/oauth", "state-123");
+        _oauthManager.Setup(o => o.GenerateAuthUrlAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(expectedUrl));
+
+        var result = await _oauthManager.Object.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("https://twitter.com/oauth", result.Value!.Url);
+        Assert.Equal("state-123", result.Value.State);
+    }
+
+    [Fact]
+    public async Task ExchangeCode_CallsOAuthManagerWithAllFields()
+    {
+        var tokens = new OAuthTokens("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), null);
+        _oauthManager.Setup(o => o.ExchangeCodeAsync(
+                PlatformType.TwitterX, "code", "state", "verifier", It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(tokens));
+
+        var result = await _oauthManager.Object.ExchangeCodeAsync(
+            PlatformType.TwitterX, "code", "state", "verifier", CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("access", result.Value!.AccessToken);
+    }
+
+    [Fact]
+    public async Task RevokeToken_CallsOAuthManager()
+    {
+        _oauthManager.Setup(o => o.RevokeTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(Unit.Value));
+
+        var result = await _oauthManager.Object.RevokeTokenAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task TestPost_PublishesViaSocialPlatform()
+    {
+        var publishResult = new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow);
+        _twitterAdapter.Setup(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(publishResult));
+
+        var adapters = new[] { _twitterAdapter.Object };
+        var adapter = adapters.FirstOrDefault(a => a.Type == PlatformType.TwitterX);
+
+        Assert.NotNull(adapter);
+
+        var content = new PlatformContent(
+            $"Test post - {DateTime.UtcNow:O}",
+            null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await adapter!.PublishAsync(content, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("t-1", result.Value!.PlatformPostId);
+    }
+
+    [Fact]
+    public async Task GetEngagement_ReturnsStats()
+    {
+        var stats = new EngagementStats(10, 5, 3, 1000, 0, new Dictionary<string, int>().AsReadOnly());
+        _twitterAdapter.Setup(a => a.GetEngagementAsync("12345", It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(stats));
+
+        var result = await _twitterAdapter.Object.GetEngagementAsync("12345", CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(10, result.Value!.Likes);
+        Assert.Equal(5, result.Value.Comments);
+    }
+
+    [Fact]
+    public void PlatformType_ParsesCorrectly()
+    {
+        Assert.True(Enum.TryParse<PlatformType>("TwitterX", ignoreCase: true, out var result));
+        Assert.Equal(PlatformType.TwitterX, result);
+
+        Assert.True(Enum.TryParse<PlatformType>("linkedin", ignoreCase: true, out var linkedin));
+        Assert.Equal(PlatformType.LinkedIn, linkedin);
+
+        Assert.False(Enum.TryParse<PlatformType>("invalid", ignoreCase: true, out _));
+    }
+
+    [Fact]
+    public void AdapterResolution_FindsCorrectPlatform()
+    {
+        var linkedInAdapter = new Mock<ISocialPlatform>();
+        linkedInAdapter.Setup(a => a.Type).Returns(PlatformType.LinkedIn);
+
+        var adapters = new[] { _twitterAdapter.Object, linkedInAdapter.Object };
+
+        var twitter = adapters.FirstOrDefault(a => a.Type == PlatformType.TwitterX);
+        var linkedin = adapters.FirstOrDefault(a => a.Type == PlatformType.LinkedIn);
+        var youtube = adapters.FirstOrDefault(a => a.Type == PlatformType.YouTube);
+
+        Assert.NotNull(twitter);
+        Assert.NotNull(linkedin);
+        Assert.Null(youtube);
+    }
+}
