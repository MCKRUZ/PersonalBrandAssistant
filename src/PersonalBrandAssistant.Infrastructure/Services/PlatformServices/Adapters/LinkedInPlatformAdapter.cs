using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public sealed class LinkedInPlatformAdapter : PlatformAdapterBase
{
    private static readonly Regex LinkedInPostIdPattern = new(@"^urn:li:(share|ugcPost):\d+$", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly PlatformOptions _options;

    public LinkedInPlatformAdapter(
        HttpClient httpClient,
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IRateLimiter rateLimiter,
        IOAuthManager oauthManager,
        IMediaStorage mediaStorage,
        IOptions<PlatformIntegrationOptions> options,
        ILogger<LinkedInPlatformAdapter> logger)
        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
    {
        _httpClient = httpClient;
        _options = options.Value.LinkedIn;
    }

    public override PlatformType Type => PlatformType.LinkedIn;

    public override Task<Result<ContentValidation>> ValidateContentAsync(
        PlatformContent content, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content.Text))
            errors.Add("LinkedIn post text cannot be empty");
        if (content.Text.Length > 3000)
            errors.Add("LinkedIn post exceeds 3000 character limit");

        return Task.FromResult(Result.Success(
            new ContentValidation(errors.Count == 0, errors, [])));
    }

    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
        string accessToken, PlatformContent content, CancellationToken ct)
    {
        var profileResult = await ExecuteGetProfileAsync(accessToken, ct);
        if (!profileResult.IsSuccess)
            return Result.Failure<PublishResult>(ErrorCode.InternalError, "Failed to get LinkedIn profile for post authorship");

        var authorUrn = $"urn:li:person:{profileResult.Value!.PlatformUserId}";

        // Upload image if media is present
        string? imageUrn = null;
        string? altText = null;
        if (content.Media.Count > 0)
        {
            var mediaFile = content.Media[0];
            altText = mediaFile.AltText;
            var imageBytes = await LoadMediaBytesAsync(mediaFile.FileId, ct);
            var uploadResult = await UploadImageAsync(imageBytes, authorUrn, accessToken, ct);
            if (!uploadResult.IsSuccess)
                return Result.Failure<PublishResult>(uploadResult.ErrorCode, uploadResult.Errors.ToArray());
            imageUrn = uploadResult.Value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/posts");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        request.Headers.Add("Linkedin-Version", _options.ApiVersion ?? "202401");

        if (imageUrn is not null)
        {
            request.Content = JsonContent.Create(new
            {
                author = authorUrn,
                commentary = content.Text,
                visibility = "PUBLIC",
                distribution = new
                {
                    feedDistribution = "MAIN_FEED",
                    targetEntities = Array.Empty<object>(),
                    thirdPartyDistributionChannels = Array.Empty<object>(),
                },
                content = new
                {
                    media = new { altText = altText ?? "", id = imageUrn },
                },
                lifecycleState = "PUBLISHED",
            });
        }
        else
        {
            request.Content = JsonContent.Create(new
            {
                author = authorUrn,
                commentary = content.Text,
                visibility = "PUBLIC",
                distribution = new
                {
                    feedDistribution = "MAIN_FEED",
                    targetEntities = Array.Empty<object>(),
                    thirdPartyDistributionChannels = Array.Empty<object>(),
                },
                lifecycleState = "PUBLISHED",
            });
        }

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(response, "LinkedIn publish");

        await RecordRateLimitAsync(response, "publish", ct);

        if (!response.Headers.TryGetValues("x-restli-id", out var idValues) ||
            string.IsNullOrEmpty(idValues.FirstOrDefault()))
        {
            Logger.LogError("LinkedIn publish succeeded but returned no post ID");
            return Result.Failure<PublishResult>(ErrorCode.InternalError,
                "LinkedIn publish: no post ID in response");
        }
        var postId = idValues.First();

        return Result.Success(new PublishResult(
            postId, $"https://www.linkedin.com/feed/update/{postId}", DateTimeOffset.UtcNow));
    }

    private async Task<byte[]> LoadMediaBytesAsync(string fileId, CancellationToken ct)
    {
        await using var stream = await MediaStorage.GetStreamAsync(fileId, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private async Task<Result<string>> UploadImageAsync(
        byte[] imageData, string authorUrn, string accessToken, CancellationToken ct)
    {
        // Step 1: Initialize upload
        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "/rest/images?action=initializeUpload");
        initRequest.Headers.Authorization = new("Bearer", accessToken);
        initRequest.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        initRequest.Headers.Add("Linkedin-Version", _options.ApiVersion ?? "202401");
        initRequest.Content = JsonContent.Create(new
        {
            initializeUploadRequest = new { owner = authorUrn },
        });

        var initResponse = await _httpClient.SendAsync(initRequest, ct);
        if (!initResponse.IsSuccessStatusCode)
            return HandleHttpError<string>(initResponse, "LinkedIn image initializeUpload");

        var initJson = await initResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var uploadUrl = initJson.GetProperty("value").GetProperty("uploadUrl").GetString()!;
        var imageUrn = initJson.GetProperty("value").GetProperty("image").GetString()!;

        // Step 2: Upload binary
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        uploadRequest.Headers.Authorization = new("Bearer", accessToken);
        uploadRequest.Content = new ByteArrayContent(imageData);
        uploadRequest.Content.Headers.ContentType = new("application/octet-stream");

        var uploadResponse = await _httpClient.SendAsync(uploadRequest, ct);
        if (!uploadResponse.IsSuccessStatusCode)
            return HandleHttpError<string>(uploadResponse, "LinkedIn image upload");

        // Step 3: Poll until available
        const int maxPollSeconds = 30;
        const int pollIntervalSeconds = 2;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(maxPollSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get,
                $"/rest/images/{Uri.EscapeDataString(imageUrn)}");
            statusRequest.Headers.Authorization = new("Bearer", accessToken);
            statusRequest.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            statusRequest.Headers.Add("Linkedin-Version", _options.ApiVersion ?? "202401");

            var statusResponse = await _httpClient.SendAsync(statusRequest, ct);
            if (statusResponse.IsSuccessStatusCode)
            {
                var statusJson = await statusResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (statusJson.TryGetProperty("status", out var statusProp) &&
                    statusProp.GetString() == "AVAILABLE")
                {
                    return Result.Success(imageUrn);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);
        }

        return Result.Failure<string>(ErrorCode.InternalError,
            "LinkedIn image processing timed out after 30s");
    }

    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!LinkedInPostIdPattern.IsMatch(platformPostId))
            return Result.Failure<Unit>(ErrorCode.ValidationFailed, "Invalid LinkedIn post ID format");

        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/posts/{Uri.EscapeDataString(platformPostId)}");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? Result.Success(Unit.Value)
            : HandleHttpError<Unit>(response, "LinkedIn delete");
    }

    protected override Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        // LinkedIn engagement stats require additional permissions and complex UGC API calls.
        // Return empty stats for now; full implementation deferred.
        return Task.FromResult(Result.Success(new EngagementStats(0, 0, 0, 0, 0,
            new Dictionary<string, int>().AsReadOnly())));
    }

    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
        string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PlatformProfile>(response, "LinkedIn profile");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        return Result.Success(new PlatformProfile(
            PlatformUserId: json.GetProperty("sub").GetString()!,
            DisplayName: json.GetProperty("name").GetString()!,
            AvatarUrl: json.TryGetProperty("picture", out var pic) ? pic.GetString() : null,
            FollowerCount: null));
    }

    public async Task<Result<IReadOnlyList<DiscoveredLinkedInPost>>> DiscoverUserPostsAsync(
        int limit, CancellationToken ct)
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (!tokenResult.IsSuccess)
            return Result.Failure<IReadOnlyList<DiscoveredLinkedInPost>>(ErrorCode.Unauthorized, "LinkedIn not authenticated");

        var profileResult = await ExecuteGetProfileAsync(tokenResult.Value!, ct);
        if (!profileResult.IsSuccess)
            return Result.Failure<IReadOnlyList<DiscoveredLinkedInPost>>(ErrorCode.InternalError, "Could not fetch LinkedIn profile");

        var personUrn = $"urn:li:person:{profileResult.Value!.PlatformUserId}";
        var clampedLimit = Math.Clamp(limit, 1, 50);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/rest/posts?author={Uri.EscapeDataString(personUrn)}&q=author&count={clampedLimit}&sortBy=LAST_MODIFIED");
        request.Headers.Authorization = new("Bearer", tokenResult.Value!);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        request.Headers.Add("Linkedin-Version", _options.ApiVersion ?? "202401");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return HandleHttpError<IReadOnlyList<DiscoveredLinkedInPost>>(response, "LinkedIn user posts");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var posts = new List<DiscoveredLinkedInPost>();

        if (json.TryGetProperty("elements", out var elements))
        {
            foreach (var post in elements.EnumerateArray())
            {
                var postId = post.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                var commentary = post.TryGetProperty("commentary", out var c) ? c.GetString() ?? "" : "";
                var createdAt = post.TryGetProperty("createdAt", out var ca)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ca.GetInt64())
                    : DateTimeOffset.UtcNow;
                var lifecycleState = post.TryGetProperty("lifecycleState", out var ls) ? ls.GetString() : null;

                if (lifecycleState != "PUBLISHED" || string.IsNullOrEmpty(postId)) continue;

                var title = commentary.Length > 100 ? commentary[..100] + "..." : commentary;

                posts.Add(new DiscoveredLinkedInPost(
                    PlatformPostId: postId,
                    Title: title,
                    Body: commentary,
                    Url: $"https://www.linkedin.com/feed/update/{postId}",
                    PublishedAt: createdAt));
            }
        }

        return Result.Success<IReadOnlyList<DiscoveredLinkedInPost>>(posts.AsReadOnly());
    }

    public record DiscoveredLinkedInPost(
        string PlatformPostId, string Title, string Body, string Url, DateTimeOffset PublishedAt);

    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
        HttpResponseMessage response)
    {
        // LinkedIn doesn't provide per-request rate limit headers.
        // Only Retry-After on 429 responses.
        if (response.Headers.TryGetValues("Retry-After", out var retryValues) &&
            int.TryParse(retryValues.FirstOrDefault(), out var seconds))
        {
            return (0, DateTimeOffset.UtcNow.AddSeconds(seconds));
        }

        return (null, null);
    }
}
