diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/InstagramPlatformAdapter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/InstagramPlatformAdapter.cs
new file mode 100644
index 0000000..ff874a5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/InstagramPlatformAdapter.cs
@@ -0,0 +1,170 @@
+using System.Net.Http.Json;
+using System.Text.Json;
+using MediatR;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+
+public sealed class InstagramPlatformAdapter : PlatformAdapterBase
+{
+    private readonly HttpClient _httpClient;
+
+    public InstagramPlatformAdapter(
+        HttpClient httpClient,
+        IApplicationDbContext dbContext,
+        IEncryptionService encryption,
+        IRateLimiter rateLimiter,
+        IOAuthManager oauthManager,
+        IMediaStorage mediaStorage,
+        ILogger<InstagramPlatformAdapter> logger)
+        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
+    {
+        _httpClient = httpClient;
+    }
+
+    public override PlatformType Type => PlatformType.Instagram;
+
+    public override Task<Result<ContentValidation>> ValidateContentAsync(
+        PlatformContent content, CancellationToken ct)
+    {
+        var errors = new List<string>();
+        if (content.Media.Count == 0)
+            errors.Add("Instagram requires at least one media attachment");
+        if (content.Text.Length > 2200)
+            errors.Add("Instagram caption exceeds 2200 character limit");
+
+        return Task.FromResult(Result.Success(
+            new ContentValidation(errors.Count == 0, errors, [])));
+    }
+
+    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
+        string accessToken, PlatformContent content, CancellationToken ct)
+    {
+        // Step 1: Get IG user ID
+        var userIdResult = await GetInstagramUserIdAsync(accessToken, ct);
+        if (!userIdResult.IsSuccess)
+            return Result.Failure<PublishResult>(ErrorCode.InternalError, "Failed to get Instagram user ID");
+
+        var igUserId = userIdResult.Value!;
+
+        // Step 2: Create media container
+        var mediaUrl = content.Media.Count > 0
+            ? await MediaStorage.GetSignedUrlAsync(content.Media[0].FileId, TimeSpan.FromHours(1), ct)
+            : null;
+
+        // WARNING: access token appears in URL (Meta Graph API convention).
+        // DI config must suppress HTTP client request logging.
+        var containerUrl = $"/{igUserId}/media?image_url={Uri.EscapeDataString(mediaUrl ?? "")}" +
+                           $"&caption={Uri.EscapeDataString(content.Text)}" +
+                           $"&access_token={accessToken}";
+
+        using var containerReq = new HttpRequestMessage(HttpMethod.Post, containerUrl);
+        var containerResp = await _httpClient.SendAsync(containerReq, ct);
+
+        if (!containerResp.IsSuccessStatusCode)
+            return HandleHttpError<PublishResult>(containerResp, "Instagram container creation");
+
+        var containerJson = await containerResp.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var containerId = containerJson.GetProperty("id").GetString()!;
+
+        // Step 3: Publish container
+        // WARNING: access token appears in URL (Meta Graph API convention).
+        var publishUrl = $"/{igUserId}/media_publish?creation_id={containerId}&access_token={accessToken}";
+        using var publishReq = new HttpRequestMessage(HttpMethod.Post, publishUrl);
+        var publishResp = await _httpClient.SendAsync(publishReq, ct);
+
+        if (!publishResp.IsSuccessStatusCode)
+            return HandleHttpError<PublishResult>(publishResp, "Instagram publish");
+
+        await RecordRateLimitAsync(publishResp, "publish", ct);
+
+        var publishJson = await publishResp.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var postId = publishJson.GetProperty("id").GetString()!;
+
+        return Result.Success(new PublishResult(
+            postId, $"https://www.instagram.com/p/{postId}", DateTimeOffset.UtcNow));
+    }
+
+    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        // WARNING: access token appears in URL (Meta Graph API convention).
+        using var request = new HttpRequestMessage(HttpMethod.Delete,
+            $"/{platformPostId}?access_token={accessToken}");
+
+        var response = await _httpClient.SendAsync(request, ct);
+        return response.IsSuccessStatusCode
+            ? Result.Success(Unit.Value)
+            : HandleHttpError<Unit>(response, "Instagram delete");
+    }
+
+    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        // WARNING: access token appears in URL (Meta Graph API convention).
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            $"/{platformPostId}?fields=like_count,comments_count,impressions&access_token={accessToken}");
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<EngagementStats>(response, "Instagram engagement");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+
+        return Result.Success(new EngagementStats(
+            Likes: json.TryGetProperty("like_count", out var likes) ? likes.GetInt32() : 0,
+            Comments: json.TryGetProperty("comments_count", out var comments) ? comments.GetInt32() : 0,
+            Shares: 0,
+            Impressions: json.TryGetProperty("impressions", out var imp) ? imp.GetInt32() : 0,
+            Clicks: 0,
+            PlatformSpecific: new Dictionary<string, int>()));
+    }
+
+    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
+        string accessToken, CancellationToken ct)
+    {
+        // WARNING: access token appears in URL (Meta Graph API convention).
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            $"/me?fields=id,name,profile_picture_url,followers_count&access_token={accessToken}");
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<PlatformProfile>(response, "Instagram profile");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+
+        return Result.Success(new PlatformProfile(
+            PlatformUserId: json.GetProperty("id").GetString()!,
+            DisplayName: json.TryGetProperty("name", out var name) ? name.GetString()! : "Instagram User",
+            AvatarUrl: json.TryGetProperty("profile_picture_url", out var avatar) ? avatar.GetString() : null,
+            FollowerCount: json.TryGetProperty("followers_count", out var fc) ? fc.GetInt32() : null));
+    }
+
+    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
+        HttpResponseMessage response)
+    {
+        // Instagram/Meta API doesn't provide per-request rate limit headers.
+        return (null, null);
+    }
+
+    private async Task<Result<string>> GetInstagramUserIdAsync(string accessToken, CancellationToken ct)
+    {
+        // WARNING: access token appears in URL (Meta Graph API convention).
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            $"/me?fields=id&access_token={accessToken}");
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<string>(response, "Instagram user ID");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+        return Result.Success(json.GetProperty("id").GetString()!);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/LinkedInPlatformAdapter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/LinkedInPlatformAdapter.cs
new file mode 100644
index 0000000..8fed1c2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/LinkedInPlatformAdapter.cs
@@ -0,0 +1,147 @@
+using System.Net.Http.Json;
+using System.Text.Json;
+using MediatR;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+
+public sealed class LinkedInPlatformAdapter : PlatformAdapterBase
+{
+    private readonly HttpClient _httpClient;
+    private readonly PlatformOptions _options;
+
+    public LinkedInPlatformAdapter(
+        HttpClient httpClient,
+        IApplicationDbContext dbContext,
+        IEncryptionService encryption,
+        IRateLimiter rateLimiter,
+        IOAuthManager oauthManager,
+        IMediaStorage mediaStorage,
+        IOptions<PlatformIntegrationOptions> options,
+        ILogger<LinkedInPlatformAdapter> logger)
+        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
+    {
+        _httpClient = httpClient;
+        _options = options.Value.LinkedIn;
+    }
+
+    public override PlatformType Type => PlatformType.LinkedIn;
+
+    public override Task<Result<ContentValidation>> ValidateContentAsync(
+        PlatformContent content, CancellationToken ct)
+    {
+        var errors = new List<string>();
+        if (string.IsNullOrWhiteSpace(content.Text))
+            errors.Add("LinkedIn post text cannot be empty");
+        if (content.Text.Length > 3000)
+            errors.Add("LinkedIn post exceeds 3000 character limit");
+
+        return Task.FromResult(Result.Success(
+            new ContentValidation(errors.Count == 0, errors, [])));
+    }
+
+    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
+        string accessToken, PlatformContent content, CancellationToken ct)
+    {
+        // Get user URN for post authorship
+        var profileResult = await ExecuteGetProfileAsync(accessToken, ct);
+        if (!profileResult.IsSuccess)
+            return Result.Failure<PublishResult>(ErrorCode.InternalError, "Failed to get LinkedIn profile for post authorship");
+
+        var authorUrn = $"urn:li:person:{profileResult.Value!.PlatformUserId}";
+
+        using var request = new HttpRequestMessage(HttpMethod.Post, "/posts");
+        request.Headers.Authorization = new("Bearer", accessToken);
+        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
+        request.Headers.Add("Linkedin-Version", _options.ApiVersion ?? "202401");
+
+        request.Content = JsonContent.Create(new
+        {
+            author = authorUrn,
+            commentary = content.Text,
+            visibility = "PUBLIC",
+            distribution = new
+            {
+                feedDistribution = "MAIN_FEED",
+                targetEntities = Array.Empty<object>(),
+                thirdPartyDistributionChannels = Array.Empty<object>(),
+            },
+            lifecycleState = "PUBLISHED",
+        });
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<PublishResult>(response, "LinkedIn publish");
+
+        await RecordRateLimitAsync(response, "publish", ct);
+
+        // LinkedIn returns the post URN in the x-restli-id header
+        var postId = response.Headers.TryGetValues("x-restli-id", out var idValues)
+            ? idValues.FirstOrDefault() ?? Guid.NewGuid().ToString()
+            : Guid.NewGuid().ToString();
+
+        return Result.Success(new PublishResult(
+            postId, $"https://www.linkedin.com/feed/update/{postId}", DateTimeOffset.UtcNow));
+    }
+
+    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/posts/{platformPostId}");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+        return response.IsSuccessStatusCode
+            ? Result.Success(Unit.Value)
+            : HandleHttpError<Unit>(response, "LinkedIn delete");
+    }
+
+    protected override Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        // LinkedIn engagement stats require additional permissions and complex UGC API calls.
+        // Return empty stats for now; full implementation deferred.
+        return Task.FromResult(Result.Success(new EngagementStats(0, 0, 0, 0, 0,
+            new Dictionary<string, int>())));
+    }
+
+    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
+        string accessToken, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get, "/userinfo");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<PlatformProfile>(response, "LinkedIn profile");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+
+        return Result.Success(new PlatformProfile(
+            PlatformUserId: json.GetProperty("sub").GetString()!,
+            DisplayName: json.GetProperty("name").GetString()!,
+            AvatarUrl: json.TryGetProperty("picture", out var pic) ? pic.GetString() : null,
+            FollowerCount: null));
+    }
+
+    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
+        HttpResponseMessage response)
+    {
+        // LinkedIn doesn't provide per-request rate limit headers.
+        // Only Retry-After on 429 responses.
+        if (response.Headers.TryGetValues("Retry-After", out var retryValues) &&
+            int.TryParse(retryValues.FirstOrDefault(), out var seconds))
+        {
+            return (0, DateTimeOffset.UtcNow.AddSeconds(seconds));
+        }
+
+        return (null, null);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs
new file mode 100644
index 0000000..98668b7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs
@@ -0,0 +1,165 @@
+using System.Net;
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+
+public abstract class PlatformAdapterBase : ISocialPlatform
+{
+    private readonly IApplicationDbContext _dbContext;
+    private readonly IEncryptionService _encryption;
+    private readonly IRateLimiter _rateLimiter;
+    private readonly IOAuthManager _oauthManager;
+    protected readonly IMediaStorage MediaStorage;
+    protected readonly ILogger Logger;
+
+    protected PlatformAdapterBase(
+        IApplicationDbContext dbContext,
+        IEncryptionService encryption,
+        IRateLimiter rateLimiter,
+        IOAuthManager oauthManager,
+        IMediaStorage mediaStorage,
+        ILogger logger)
+    {
+        _dbContext = dbContext;
+        _encryption = encryption;
+        _rateLimiter = rateLimiter;
+        _oauthManager = oauthManager;
+        MediaStorage = mediaStorage;
+        Logger = logger;
+    }
+
+    public abstract PlatformType Type { get; }
+
+    public async Task<Result<PublishResult>> PublishAsync(PlatformContent content, CancellationToken ct)
+    {
+        return await ExecuteWithTokenAsync("publish", ct,
+            (token, c) => ExecutePublishAsync(token, content, c));
+    }
+
+    public async Task<Result<Unit>> DeletePostAsync(string platformPostId, CancellationToken ct)
+    {
+        return await ExecuteWithTokenAsync<Unit>("delete", ct,
+            (token, c) => ExecuteDeletePostAsync(token, platformPostId, c));
+    }
+
+    public async Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct)
+    {
+        return await ExecuteWithTokenAsync("engagement", ct,
+            (token, c) => ExecuteGetEngagementAsync(token, platformPostId, c));
+    }
+
+    public async Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct)
+    {
+        return await ExecuteWithTokenAsync("profile", ct,
+            (token, c) => ExecuteGetProfileAsync(token, c));
+    }
+
+    public abstract Task<Result<ContentValidation>> ValidateContentAsync(
+        PlatformContent content, CancellationToken ct);
+
+    protected abstract Task<Result<PublishResult>> ExecutePublishAsync(
+        string accessToken, PlatformContent content, CancellationToken ct);
+
+    protected abstract Task<Result<Unit>> ExecuteDeletePostAsync(
+        string accessToken, string platformPostId, CancellationToken ct);
+
+    protected abstract Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
+        string accessToken, string platformPostId, CancellationToken ct);
+
+    protected abstract Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
+        string accessToken, CancellationToken ct);
+
+    protected abstract (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
+        HttpResponseMessage response);
+
+    private async Task<Result<T>> ExecuteWithTokenAsync<T>(
+        string endpoint, CancellationToken ct,
+        Func<string, CancellationToken, Task<Result<T>>> execute)
+    {
+        var tokenResult = await LoadAccessTokenAsync(ct);
+        if (!tokenResult.IsSuccess)
+        {
+            return Result.Failure<T>(tokenResult.ErrorCode, tokenResult.Errors.ToArray());
+        }
+
+        var rateLimitResult = await _rateLimiter.CanMakeRequestAsync(Type, endpoint, ct);
+        if (rateLimitResult.IsSuccess && !rateLimitResult.Value!.Allowed)
+        {
+            Logger.LogWarning("Rate limited for {Platform}/{Endpoint}: {Reason}",
+                Type, endpoint, rateLimitResult.Value.Reason);
+            return Result.Failure<T>(ErrorCode.ValidationFailed,
+                $"Rate limited: {rateLimitResult.Value.Reason}");
+        }
+
+        var result = await execute(tokenResult.Value!, ct);
+
+        if (!result.IsSuccess && result.ErrorCode == ErrorCode.Unauthorized)
+        {
+            Logger.LogInformation("401 received for {Platform}, attempting token refresh", Type);
+            var refreshResult = await _oauthManager.RefreshTokenAsync(Type, ct);
+            if (refreshResult.IsSuccess)
+            {
+                var newToken = await LoadAccessTokenAsync(ct);
+                if (newToken.IsSuccess)
+                {
+                    result = await execute(newToken.Value!, ct);
+                }
+            }
+            else
+            {
+                Logger.LogWarning("Token refresh failed for {Platform}", Type);
+                return Result.Failure<T>(ErrorCode.Unauthorized, "Token refresh failed");
+            }
+        }
+
+        return result;
+    }
+
+    private async Task<Result<string>> LoadAccessTokenAsync(CancellationToken ct)
+    {
+        var platform = await _dbContext.Platforms
+            .FirstOrDefaultAsync(p => p.Type == Type, ct);
+
+        if (platform is null)
+        {
+            return Result.NotFound<string>($"Platform '{Type}' not configured");
+        }
+
+        if (!platform.IsConnected || platform.EncryptedAccessToken is null)
+        {
+            return Result.Failure<string>(ErrorCode.Unauthorized, "Platform not connected");
+        }
+
+        var token = _encryption.Decrypt(platform.EncryptedAccessToken);
+        return Result.Success(token);
+    }
+
+    protected async Task RecordRateLimitAsync(
+        HttpResponseMessage response, string endpoint, CancellationToken ct)
+    {
+        var (remaining, resetAt) = ParseRateLimitHeaders(response);
+        if (remaining.HasValue)
+        {
+            await _rateLimiter.RecordRequestAsync(
+                Type, endpoint, remaining.Value, resetAt, ct);
+        }
+    }
+
+    protected static Result<T> HandleHttpError<T>(HttpResponseMessage response, string context)
+    {
+        return response.StatusCode switch
+        {
+            HttpStatusCode.Unauthorized => Result.Failure<T>(ErrorCode.Unauthorized, $"{context}: unauthorized"),
+            HttpStatusCode.Forbidden => Result.Failure<T>(ErrorCode.ValidationFailed, $"{context}: forbidden"),
+            HttpStatusCode.TooManyRequests => Result.Failure<T>(ErrorCode.ValidationFailed, $"{context}: rate limited"),
+            HttpStatusCode.NotFound => Result.NotFound<T>($"{context}: not found"),
+            _ => Result.Failure<T>(ErrorCode.InternalError, $"{context}: failed with status {(int)response.StatusCode}"),
+        };
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/TwitterPlatformAdapter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/TwitterPlatformAdapter.cs
new file mode 100644
index 0000000..db98f35
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/TwitterPlatformAdapter.cs
@@ -0,0 +1,174 @@
+using System.Net.Http.Json;
+using System.Text.Json;
+using MediatR;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+
+public sealed class TwitterPlatformAdapter : PlatformAdapterBase
+{
+    private readonly HttpClient _httpClient;
+
+    public TwitterPlatformAdapter(
+        HttpClient httpClient,
+        IApplicationDbContext dbContext,
+        IEncryptionService encryption,
+        IRateLimiter rateLimiter,
+        IOAuthManager oauthManager,
+        IMediaStorage mediaStorage,
+        ILogger<TwitterPlatformAdapter> logger)
+        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
+    {
+        _httpClient = httpClient;
+    }
+
+    public override PlatformType Type => PlatformType.TwitterX;
+
+    public override Task<Result<ContentValidation>> ValidateContentAsync(
+        PlatformContent content, CancellationToken ct)
+    {
+        var errors = new List<string>();
+        if (string.IsNullOrWhiteSpace(content.Text))
+            errors.Add("Tweet text cannot be empty");
+
+        return Task.FromResult(Result.Success(
+            new ContentValidation(errors.Count == 0, errors, [])));
+    }
+
+    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
+        string accessToken, PlatformContent content, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Post, "/tweets");
+        request.Headers.Authorization = new("Bearer", accessToken);
+        request.Content = JsonContent.Create(new { text = content.Text });
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<PublishResult>(response, "Tweet publish");
+
+        await RecordRateLimitAsync(response, "publish", ct);
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var tweetId = json.GetProperty("data").GetProperty("id").GetString()!;
+        var postUrl = $"https://x.com/i/status/{tweetId}";
+
+        // Handle thread if present
+        if (content.Metadata.Keys.Any(k => k.StartsWith("thread:")))
+        {
+            var previousId = tweetId;
+            foreach (var key in content.Metadata.Keys.Where(k => k.StartsWith("thread:")).OrderBy(k => k))
+            {
+                var threadText = content.Metadata[key];
+                using var threadReq = new HttpRequestMessage(HttpMethod.Post, "/tweets");
+                threadReq.Headers.Authorization = new("Bearer", accessToken);
+                threadReq.Content = JsonContent.Create(new
+                {
+                    text = threadText,
+                    reply = new { in_reply_to_tweet_id = previousId },
+                });
+
+                var threadResp = await _httpClient.SendAsync(threadReq, ct);
+                if (!threadResp.IsSuccessStatusCode)
+                {
+                    Logger.LogWarning("Thread tweet failed at {Key}", key);
+                    break;
+                }
+
+                var threadJson = await threadResp.Content.ReadFromJsonAsync<JsonElement>(ct);
+                previousId = threadJson.GetProperty("data").GetProperty("id").GetString()!;
+            }
+        }
+
+        return Result.Success(new PublishResult(tweetId, postUrl, DateTimeOffset.UtcNow));
+    }
+
+    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/tweets/{platformPostId}");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+        return response.IsSuccessStatusCode
+            ? Result.Success(Unit.Value)
+            : HandleHttpError<Unit>(response, "Tweet delete");
+    }
+
+    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            $"/tweets/{platformPostId}?tweet.fields=public_metrics");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<EngagementStats>(response, "Tweet engagement");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var metrics = json.GetProperty("data").GetProperty("public_metrics");
+
+        return Result.Success(new EngagementStats(
+            Likes: metrics.GetProperty("like_count").GetInt32(),
+            Comments: metrics.GetProperty("reply_count").GetInt32(),
+            Shares: metrics.GetProperty("retweet_count").GetInt32(),
+            Impressions: metrics.TryGetProperty("impression_count", out var imp) ? imp.GetInt32() : 0,
+            Clicks: 0,
+            PlatformSpecific: new Dictionary<string, int>
+            {
+                ["quote_count"] = metrics.TryGetProperty("quote_count", out var q) ? q.GetInt32() : 0,
+                ["bookmark_count"] = metrics.TryGetProperty("bookmark_count", out var b) ? b.GetInt32() : 0,
+            }));
+    }
+
+    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
+        string accessToken, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            "/users/me?user.fields=profile_image_url,public_metrics");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<PlatformProfile>(response, "Twitter profile");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var data = json.GetProperty("data");
+
+        return Result.Success(new PlatformProfile(
+            PlatformUserId: data.GetProperty("id").GetString()!,
+            DisplayName: data.GetProperty("name").GetString()!,
+            AvatarUrl: data.TryGetProperty("profile_image_url", out var avatar) ? avatar.GetString() : null,
+            FollowerCount: data.TryGetProperty("public_metrics", out var pm)
+                ? pm.GetProperty("followers_count").GetInt32()
+                : null));
+    }
+
+    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
+        HttpResponseMessage response)
+    {
+        int? remaining = null;
+        DateTimeOffset? resetAt = null;
+
+        if (response.Headers.TryGetValues("x-rate-limit-remaining", out var remainingValues) &&
+            int.TryParse(remainingValues.FirstOrDefault(), out var r))
+        {
+            remaining = r;
+        }
+
+        if (response.Headers.TryGetValues("x-rate-limit-reset", out var resetValues) &&
+            long.TryParse(resetValues.FirstOrDefault(), out var epoch))
+        {
+            resetAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
+        }
+
+        return (remaining, resetAt);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/YouTubePlatformAdapter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/YouTubePlatformAdapter.cs
new file mode 100644
index 0000000..01fce4a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/YouTubePlatformAdapter.cs
@@ -0,0 +1,205 @@
+using System.Net.Http.Json;
+using System.Text.Json;
+using MediatR;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+
+public sealed class YouTubePlatformAdapter : PlatformAdapterBase
+{
+    private readonly HttpClient _httpClient;
+    private const int UploadQuotaCost = 1600;
+    private const int ListQuotaCost = 1;
+
+    public YouTubePlatformAdapter(
+        HttpClient httpClient,
+        IApplicationDbContext dbContext,
+        IEncryptionService encryption,
+        IRateLimiter rateLimiter,
+        IOAuthManager oauthManager,
+        IMediaStorage mediaStorage,
+        ILogger<YouTubePlatformAdapter> logger)
+        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
+    {
+        _httpClient = httpClient;
+    }
+
+    public override PlatformType Type => PlatformType.YouTube;
+
+    public override Task<Result<ContentValidation>> ValidateContentAsync(
+        PlatformContent content, CancellationToken ct)
+    {
+        var errors = new List<string>();
+        if (string.IsNullOrWhiteSpace(content.Title))
+            errors.Add("YouTube video requires a title");
+        if (content.Media.Count == 0)
+            errors.Add("YouTube video requires a video file");
+
+        return Task.FromResult(Result.Success(
+            new ContentValidation(errors.Count == 0, errors, [])));
+    }
+
+    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
+        string accessToken, PlatformContent content, CancellationToken ct)
+    {
+        // Build video metadata
+        var tags = content.Metadata.TryGetValue("tags", out var tagStr)
+            ? tagStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
+            : Array.Empty<string>();
+
+        var privacy = content.Metadata.TryGetValue("privacy", out var p) ? p : "public";
+
+        var videoMetadata = new
+        {
+            snippet = new
+            {
+                title = content.Title ?? "Untitled",
+                description = content.Text,
+                tags,
+                categoryId = "22", // People & Blogs default
+            },
+            status = new
+            {
+                privacyStatus = privacy,
+            },
+        };
+
+        // Initiate resumable upload
+        using var initRequest = new HttpRequestMessage(HttpMethod.Post,
+            "/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
+        initRequest.Headers.Authorization = new("Bearer", accessToken);
+        initRequest.Content = JsonContent.Create(videoMetadata);
+
+        var initResponse = await _httpClient.SendAsync(initRequest, ct);
+
+        if (!initResponse.IsSuccessStatusCode)
+            return HandleHttpError<PublishResult>(initResponse, "YouTube upload init");
+
+        var uploadUrl = initResponse.Headers.Location?.ToString();
+        if (string.IsNullOrEmpty(uploadUrl))
+            return Result.Failure<PublishResult>(ErrorCode.InternalError, "YouTube upload: no resumable URL returned");
+
+        // Upload video file
+        if (content.Media.Count > 0)
+        {
+            await using var videoStream = await MediaStorage.GetStreamAsync(content.Media[0].FileId, ct);
+            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
+            uploadRequest.Headers.Authorization = new("Bearer", accessToken);
+            uploadRequest.Content = new StreamContent(videoStream);
+            uploadRequest.Content.Headers.ContentType = new("video/*");
+
+            var uploadResponse = await _httpClient.SendAsync(uploadRequest, ct);
+
+            if (!uploadResponse.IsSuccessStatusCode)
+                return HandleHttpError<PublishResult>(uploadResponse, "YouTube video upload");
+
+            var json = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
+            var videoId = json.GetProperty("id").GetString()!;
+
+            // Record quota usage
+            await RecordRateLimitAsync(uploadResponse, "publish", ct);
+
+            return Result.Success(new PublishResult(
+                videoId, $"https://www.youtube.com/watch?v={videoId}", DateTimeOffset.UtcNow));
+        }
+
+        return Result.Failure<PublishResult>(ErrorCode.ValidationFailed, "No video file provided");
+    }
+
+    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Delete,
+            $"/youtube/v3/videos?id={platformPostId}");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+        return response.IsSuccessStatusCode
+            ? Result.Success(Unit.Value)
+            : HandleHttpError<Unit>(response, "YouTube delete");
+    }
+
+    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
+        string accessToken, string platformPostId, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            $"/youtube/v3/videos?part=statistics&id={platformPostId}");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<EngagementStats>(response, "YouTube engagement");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var items = json.GetProperty("items");
+
+        if (items.GetArrayLength() == 0)
+            return Result.NotFound<EngagementStats>("Video not found");
+
+        var stats = items[0].GetProperty("statistics");
+
+        return Result.Success(new EngagementStats(
+            Likes: ParseInt(stats, "likeCount"),
+            Comments: ParseInt(stats, "commentCount"),
+            Shares: 0,
+            Impressions: ParseInt(stats, "viewCount"),
+            Clicks: 0,
+            PlatformSpecific: new Dictionary<string, int>
+            {
+                ["favoriteCount"] = ParseInt(stats, "favoriteCount"),
+            }));
+    }
+
+    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
+        string accessToken, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get,
+            "/youtube/v3/channels?part=snippet,statistics&mine=true");
+        request.Headers.Authorization = new("Bearer", accessToken);
+
+        var response = await _httpClient.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+            return HandleHttpError<PlatformProfile>(response, "YouTube profile");
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+        var items = json.GetProperty("items");
+
+        if (items.GetArrayLength() == 0)
+            return Result.NotFound<PlatformProfile>("YouTube channel not found");
+
+        var channel = items[0];
+        var snippet = channel.GetProperty("snippet");
+
+        return Result.Success(new PlatformProfile(
+            PlatformUserId: channel.GetProperty("id").GetString()!,
+            DisplayName: snippet.GetProperty("title").GetString()!,
+            AvatarUrl: snippet.TryGetProperty("thumbnails", out var thumbs) &&
+                       thumbs.TryGetProperty("default", out var def)
+                ? def.GetProperty("url").GetString()
+                : null,
+            FollowerCount: channel.TryGetProperty("statistics", out var s) &&
+                           s.TryGetProperty("subscriberCount", out var sc)
+                ? int.TryParse(sc.GetString(), out var count) ? count : null
+                : null));
+    }
+
+    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
+        HttpResponseMessage response)
+    {
+        // YouTube API uses daily quota, not per-request rate limit headers.
+        // Quota tracking is handled by the rate limiter's daily quota feature.
+        return (null, null);
+    }
+
+    private static int ParseInt(JsonElement element, string property) =>
+        element.TryGetProperty(property, out var value) &&
+        int.TryParse(value.GetString() ?? value.ToString(), out var result)
+            ? result
+            : 0;
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramPlatformAdapterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramPlatformAdapterTests.cs
new file mode 100644
index 0000000..badd99e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramPlatformAdapterTests.cs
@@ -0,0 +1,107 @@
+using System.Net;
+using System.Text.Json;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using Moq.Protected;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class InstagramPlatformAdapterTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<IEncryptionService> _encryption = new();
+    private readonly Mock<IRateLimiter> _rateLimiter = new();
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<IMediaStorage> _mediaStorage = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly InstagramPlatformAdapter _sut;
+
+    public InstagramPlatformAdapterTests()
+    {
+        var httpClient = new HttpClient(_httpHandler.Object)
+        {
+            BaseAddress = new Uri("https://graph.facebook.com/v21.0"),
+        };
+
+        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("ig-token");
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(true));
+
+        _sut = new InstagramPlatformAdapter(
+            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
+            _oauthManager.Object, _mediaStorage.Object,
+            NullLogger<InstagramPlatformAdapter>.Instance);
+    }
+
+    [Fact]
+    public void Type_IsInstagram() =>
+        Assert.Equal(PlatformType.Instagram, _sut.Type);
+
+    [Fact]
+    public async Task ValidateContentAsync_NoMedia_ReturnsInvalid()
+    {
+        var content = new PlatformContent("Caption", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.False(result.Value!.IsValid);
+        Assert.Contains("Instagram requires at least one media attachment", result.Value.Errors);
+    }
+
+    [Fact]
+    public async Task ValidateContentAsync_WithMedia_ReturnsValid()
+    {
+        var media = new List<MediaFile> { new("file1", "image/jpeg", null) };
+        var content = new PlatformContent("Caption", null, ContentType.SocialPost, media, new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.True(result.Value!.IsValid);
+    }
+
+    [Fact]
+    public async Task PublishAsync_UsesSignedUrlForMedia()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.Instagram,
+            DisplayName = "Instagram",
+            IsConnected = true,
+            EncryptedAccessToken = [1, 2, 3],
+        };
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+
+        _mediaStorage.Setup(m => m.GetSignedUrlAsync("file1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync("https://storage.example.com/signed/file1");
+
+        var callCount = 0;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(() =>
+            {
+                callCount++;
+                // First call: get user ID, second: create container, third: publish
+                return new HttpResponseMessage(HttpStatusCode.OK)
+                {
+                    Content = new StringContent(JsonSerializer.Serialize(new { id = $"ig-{callCount}" })),
+                };
+            });
+
+        var media = new List<MediaFile> { new("file1", "image/jpeg", null) };
+        var content = new PlatformContent("Caption", null, ContentType.SocialPost, media, new Dictionary<string, string>());
+        var result = await _sut.PublishAsync(content, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _mediaStorage.Verify(m => m.GetSignedUrlAsync("file1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs
new file mode 100644
index 0000000..0bf7ec2
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs
@@ -0,0 +1,112 @@
+using System.Net;
+using System.Text.Json;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class LinkedInPlatformAdapterTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<IEncryptionService> _encryption = new();
+    private readonly Mock<IRateLimiter> _rateLimiter = new();
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<IMediaStorage> _mediaStorage = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly LinkedInPlatformAdapter _sut;
+
+    public LinkedInPlatformAdapterTests()
+    {
+        var httpClient = new HttpClient(_httpHandler.Object)
+        {
+            BaseAddress = new Uri("https://api.linkedin.com/rest"),
+        };
+
+        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("linkedin-token");
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(true));
+
+        var options = Options.Create(new PlatformIntegrationOptions
+        {
+            LinkedIn = new PlatformOptions { ApiVersion = "202401" },
+        });
+
+        _sut = new LinkedInPlatformAdapter(
+            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
+            _oauthManager.Object, _mediaStorage.Object, options,
+            NullLogger<LinkedInPlatformAdapter>.Instance);
+    }
+
+    private void SetupConnectedPlatform()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.LinkedIn,
+            DisplayName = "LinkedIn",
+            IsConnected = true,
+            EncryptedAccessToken = [1, 2, 3],
+        };
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+    }
+
+    [Fact]
+    public void Type_IsLinkedIn() =>
+        Assert.Equal(PlatformType.LinkedIn, _sut.Type);
+
+    [Fact]
+    public async Task GetProfileAsync_ReturnsProfile()
+    {
+        SetupConnectedPlatform();
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
+            {
+                Content = new StringContent(JsonSerializer.Serialize(new
+                {
+                    sub = "user-123",
+                    name = "Test User",
+                    picture = "https://example.com/pic.jpg",
+                })),
+            });
+
+        var result = await _sut.GetProfileAsync(CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("user-123", result.Value!.PlatformUserId);
+        Assert.Equal("Test User", result.Value.DisplayName);
+    }
+
+    [Fact]
+    public async Task ValidateContentAsync_EmptyText_ReturnsInvalid()
+    {
+        var content = new PlatformContent("", null, ContentType.BlogPost, [], new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.False(result.Value!.IsValid);
+    }
+
+    [Fact]
+    public async Task ValidateContentAsync_TooLong_ReturnsInvalid()
+    {
+        var content = new PlatformContent(new string('A', 3500), null, ContentType.BlogPost, [], new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.False(result.Value!.IsValid);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterPlatformAdapterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterPlatformAdapterTests.cs
new file mode 100644
index 0000000..fbec270
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterPlatformAdapterTests.cs
@@ -0,0 +1,256 @@
+using System.Net;
+using System.Text.Json;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using Moq.Protected;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class TwitterPlatformAdapterTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<IEncryptionService> _encryption = new();
+    private readonly Mock<IRateLimiter> _rateLimiter = new();
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<IMediaStorage> _mediaStorage = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly TwitterPlatformAdapter _sut;
+
+    public TwitterPlatformAdapterTests()
+    {
+        var httpClient = new HttpClient(_httpHandler.Object)
+        {
+            BaseAddress = new Uri("https://api.x.com/2"),
+        };
+
+        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("test-access-token");
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+
+        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(true));
+
+        _sut = new TwitterPlatformAdapter(
+            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
+            _oauthManager.Object, _mediaStorage.Object, NullLogger<TwitterPlatformAdapter>.Instance);
+    }
+
+    private void SetupConnectedPlatform()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.TwitterX,
+            DisplayName = "Twitter",
+            IsConnected = true,
+            EncryptedAccessToken = [1, 2, 3],
+        };
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+    }
+
+    private void SetupHttpResponse(HttpStatusCode status, object body, Dictionary<string, string>? headers = null)
+    {
+        var response = new HttpResponseMessage(status)
+        {
+            Content = new StringContent(JsonSerializer.Serialize(body)),
+        };
+
+        if (headers != null)
+        {
+            foreach (var (key, value) in headers)
+            {
+                response.Headers.TryAddWithoutValidation(key, value);
+            }
+        }
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(response);
+    }
+
+    [Fact]
+    public void Type_IsTwitterX() =>
+        Assert.Equal(PlatformType.TwitterX, _sut.Type);
+
+    [Fact]
+    public async Task PublishAsync_DecryptsTokenBeforeApiCall()
+    {
+        SetupConnectedPlatform();
+        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "123" } });
+
+        var content = new PlatformContent("Hello world", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        await _sut.PublishAsync(content, CancellationToken.None);
+
+        _encryption.Verify(e => e.Decrypt(It.IsAny<byte[]>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ChecksRateLimitBeforeRequest()
+    {
+        SetupConnectedPlatform();
+        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "123" } });
+
+        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        await _sut.PublishAsync(content, CancellationToken.None);
+
+        _rateLimiter.Verify(r => r.CanMakeRequestAsync(PlatformType.TwitterX, "publish", It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ReturnsFailure_WhenRateLimited()
+    {
+        SetupConnectedPlatform();
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(false, DateTimeOffset.UtcNow.AddMinutes(5), "Too many requests")));
+
+        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await _sut.PublishAsync(content, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task PublishAsync_PostsTweetAndReturnsResult()
+    {
+        SetupConnectedPlatform();
+        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "tweet-456" } });
+
+        var content = new PlatformContent("Test tweet", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await _sut.PublishAsync(content, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("tweet-456", result.Value!.PlatformPostId);
+        Assert.Contains("tweet-456", result.Value.PostUrl);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RecordsRateLimitFromHeaders()
+    {
+        SetupConnectedPlatform();
+        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "123" } },
+            new Dictionary<string, string>
+            {
+                ["x-rate-limit-remaining"] = "42",
+                ["x-rate-limit-reset"] = "1700000000",
+            });
+
+        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        await _sut.PublishAsync(content, CancellationToken.None);
+
+        _rateLimiter.Verify(r => r.RecordRequestAsync(
+            PlatformType.TwitterX, "publish", 42, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ReturnsFailure_WhenNotConnected()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.TwitterX,
+            DisplayName = "Twitter",
+            IsConnected = false,
+        };
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+
+        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await _sut.PublishAsync(content, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RetriesOn401AfterTokenRefresh()
+    {
+        SetupConnectedPlatform();
+
+        var callCount = 0;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(() =>
+            {
+                callCount++;
+                if (callCount == 1)
+                {
+                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
+                }
+                return new HttpResponseMessage(HttpStatusCode.OK)
+                {
+                    Content = new StringContent(JsonSerializer.Serialize(new { data = new { id = "refreshed-tweet" } })),
+                };
+            });
+
+        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new OAuthTokens("new-token", null, DateTimeOffset.UtcNow.AddHours(1), null)));
+
+        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await _sut.PublishAsync(content, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("refreshed-tweet", result.Value!.PlatformPostId);
+        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task GetEngagementAsync_ReturnsMetrics()
+    {
+        SetupConnectedPlatform();
+        SetupHttpResponse(HttpStatusCode.OK, new
+        {
+            data = new
+            {
+                public_metrics = new
+                {
+                    like_count = 10,
+                    reply_count = 5,
+                    retweet_count = 3,
+                    impression_count = 1000,
+                    quote_count = 2,
+                    bookmark_count = 1,
+                },
+            },
+        });
+
+        var result = await _sut.GetEngagementAsync("tweet-123", CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(10, result.Value!.Likes);
+        Assert.Equal(5, result.Value.Comments);
+        Assert.Equal(3, result.Value.Shares);
+    }
+
+    [Fact]
+    public async Task GetEngagementAsync_ReturnsFailureOn403()
+    {
+        SetupConnectedPlatform();
+        SetupHttpResponse(HttpStatusCode.Forbidden, new { });
+
+        var result = await _sut.GetEngagementAsync("tweet-123", CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task ValidateContentAsync_EmptyText_ReturnsInvalid()
+    {
+        var content = new PlatformContent("", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.False(result.Value!.IsValid);
+        Assert.Contains("Tweet text cannot be empty", result.Value.Errors);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubePlatformAdapterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubePlatformAdapterTests.cs
new file mode 100644
index 0000000..b21a4ef
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubePlatformAdapterTests.cs
@@ -0,0 +1,141 @@
+using System.Net;
+using System.Text.Json;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using Moq.Protected;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class YouTubePlatformAdapterTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<IEncryptionService> _encryption = new();
+    private readonly Mock<IRateLimiter> _rateLimiter = new();
+    private readonly Mock<IOAuthManager> _oauthManager = new();
+    private readonly Mock<IMediaStorage> _mediaStorage = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly YouTubePlatformAdapter _sut;
+
+    public YouTubePlatformAdapterTests()
+    {
+        var httpClient = new HttpClient(_httpHandler.Object)
+        {
+            BaseAddress = new Uri("https://www.googleapis.com"),
+        };
+
+        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("yt-token");
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(true));
+
+        _sut = new YouTubePlatformAdapter(
+            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
+            _oauthManager.Object, _mediaStorage.Object,
+            NullLogger<YouTubePlatformAdapter>.Instance);
+    }
+
+    [Fact]
+    public void Type_IsYouTube() =>
+        Assert.Equal(PlatformType.YouTube, _sut.Type);
+
+    [Fact]
+    public async Task ValidateContentAsync_NoTitle_ReturnsInvalid()
+    {
+        var content = new PlatformContent("Description", null, ContentType.VideoDescription, [], new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.False(result.Value!.IsValid);
+        Assert.Contains("YouTube video requires a title", result.Value.Errors);
+    }
+
+    [Fact]
+    public async Task ValidateContentAsync_NoMedia_ReturnsInvalid()
+    {
+        var content = new PlatformContent("Description", "Title", ContentType.VideoDescription, [], new Dictionary<string, string>());
+        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);
+
+        Assert.False(result.Value!.IsValid);
+        Assert.Contains("YouTube video requires a video file", result.Value.Errors);
+    }
+
+    [Fact]
+    public async Task GetEngagementAsync_ReturnsStats()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.YouTube,
+            DisplayName = "YouTube",
+            IsConnected = true,
+            EncryptedAccessToken = [1, 2, 3],
+        };
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
+            {
+                Content = new StringContent(JsonSerializer.Serialize(new
+                {
+                    items = new[]
+                    {
+                        new
+                        {
+                            statistics = new
+                            {
+                                viewCount = "5000",
+                                likeCount = "200",
+                                commentCount = "50",
+                                favoriteCount = "0",
+                            },
+                        },
+                    },
+                })),
+            });
+
+        var result = await _sut.GetEngagementAsync("video-123", CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(200, result.Value!.Likes);
+        Assert.Equal(50, result.Value.Comments);
+        Assert.Equal(5000, result.Value.Impressions);
+    }
+
+    [Fact]
+    public async Task GetEngagementAsync_VideoNotFound_ReturnsNotFound()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.YouTube,
+            DisplayName = "YouTube",
+            IsConnected = true,
+            EncryptedAccessToken = [1, 2, 3],
+        };
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
+            {
+                Content = new StringContent(JsonSerializer.Serialize(new { items = Array.Empty<object>() })),
+            });
+
+        var result = await _sut.GetEngagementAsync("nonexistent", CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+}
