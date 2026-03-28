using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public sealed class InstagramPlatformAdapter : PlatformAdapterBase
{
    private static readonly Regex IgMediaIdPattern = new(@"^\d{1,25}$", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    public InstagramPlatformAdapter(
        HttpClient httpClient,
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IRateLimiter rateLimiter,
        IOAuthManager oauthManager,
        IMediaStorage mediaStorage,
        ILogger<InstagramPlatformAdapter> logger)
        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
    {
        _httpClient = httpClient;
    }

    public override PlatformType Type => PlatformType.Instagram;

    public override Task<Result<ContentValidation>> ValidateContentAsync(
        PlatformContent content, CancellationToken ct)
    {
        var errors = new List<string>();
        if (content.Media.Count == 0)
            errors.Add("Instagram requires at least one media attachment");
        if (content.Text.Length > 2200)
            errors.Add("Instagram caption exceeds 2200 character limit");

        return Task.FromResult(Result.Success(
            new ContentValidation(errors.Count == 0, errors, [])));
    }

    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
        string accessToken, PlatformContent content, CancellationToken ct)
    {
        // Step 1: Get IG user ID
        var userIdResult = await GetInstagramUserIdAsync(accessToken, ct);
        if (!userIdResult.IsSuccess)
            return Result.Failure<PublishResult>(ErrorCode.InternalError, "Failed to get Instagram user ID");

        var igUserId = userIdResult.Value!;

        // Step 2: Create media container
        var mediaUrl = content.Media.Count > 0
            ? await MediaStorage.GetSignedUrlAsync(content.Media[0].FileId, TimeSpan.FromHours(1), ct)
            : null;

        // WARNING: access token appears in URL (Meta Graph API convention).
        // DI config must suppress HTTP client request logging.
        var containerUrl = $"/{igUserId}/media?image_url={Uri.EscapeDataString(mediaUrl ?? "")}" +
                           $"&caption={Uri.EscapeDataString(content.Text)}" +
                           $"&access_token={accessToken}";

        using var containerReq = new HttpRequestMessage(HttpMethod.Post, containerUrl);
        var containerResp = await _httpClient.SendAsync(containerReq, ct);

        if (!containerResp.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(containerResp, "Instagram container creation");

        var containerJson = await containerResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var containerId = containerJson.GetProperty("id").GetString()!;

        // Step 3: Publish container
        // WARNING: access token appears in URL (Meta Graph API convention).
        var publishUrl = $"/{igUserId}/media_publish?creation_id={containerId}&access_token={accessToken}";
        using var publishReq = new HttpRequestMessage(HttpMethod.Post, publishUrl);
        var publishResp = await _httpClient.SendAsync(publishReq, ct);

        if (!publishResp.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(publishResp, "Instagram publish");

        await RecordRateLimitAsync(publishResp, "publish", ct);

        var publishJson = await publishResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var postId = publishJson.GetProperty("id").GetString()!;

        // Instagram Graph API returns numeric media ID, not the shortcode used in web URLs.
        // The correct web URL requires an extra API call for the shortcode.
        // Store the API ID; URL can be resolved later via /{media-id}?fields=permalink.
        return Result.Success(new PublishResult(
            postId, $"https://www.instagram.com/", DateTimeOffset.UtcNow));
    }

    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!IgMediaIdPattern.IsMatch(platformPostId))
            return Result.Failure<Unit>(ErrorCode.ValidationFailed, "Invalid Instagram media ID format");

        // WARNING: access token appears in URL (Meta Graph API convention).
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/{platformPostId}?access_token={accessToken}");

        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? Result.Success(Unit.Value)
            : HandleHttpError<Unit>(response, "Instagram delete");
    }

    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!IgMediaIdPattern.IsMatch(platformPostId))
            return Result.Failure<EngagementStats>(ErrorCode.ValidationFailed, "Invalid Instagram media ID format");

        // WARNING: access token appears in URL (Meta Graph API convention).
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/{platformPostId}?fields=like_count,comments_count,impressions&access_token={accessToken}");

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<EngagementStats>(response, "Instagram engagement");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        return Result.Success(new EngagementStats(
            Likes: json.TryGetProperty("like_count", out var likes) ? likes.GetInt32() : 0,
            Comments: json.TryGetProperty("comments_count", out var comments) ? comments.GetInt32() : 0,
            Shares: 0,
            Impressions: json.TryGetProperty("impressions", out var imp) ? imp.GetInt32() : 0,
            Clicks: 0,
            PlatformSpecific: new Dictionary<string, int>().AsReadOnly()));
    }

    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
        string accessToken, CancellationToken ct)
    {
        // WARNING: access token appears in URL (Meta Graph API convention).
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/me?fields=id,name,profile_picture_url,followers_count&access_token={accessToken}");

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PlatformProfile>(response, "Instagram profile");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        return Result.Success(new PlatformProfile(
            PlatformUserId: json.GetProperty("id").GetString()!,
            DisplayName: json.TryGetProperty("name", out var name) ? name.GetString()! : "Instagram User",
            AvatarUrl: json.TryGetProperty("profile_picture_url", out var avatar) ? avatar.GetString() : null,
            FollowerCount: json.TryGetProperty("followers_count", out var fc) ? fc.GetInt32() : null));
    }

    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
        HttpResponseMessage response)
    {
        // Instagram/Meta API doesn't provide per-request rate limit headers.
        return (null, null);
    }

    private async Task<Result<string>> GetInstagramUserIdAsync(string accessToken, CancellationToken ct)
    {
        // WARNING: access token appears in URL (Meta Graph API convention).
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/me?fields=id&access_token={accessToken}");

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<string>(response, "Instagram user ID");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return Result.Success(json.GetProperty("id").GetString()!);
    }
}
