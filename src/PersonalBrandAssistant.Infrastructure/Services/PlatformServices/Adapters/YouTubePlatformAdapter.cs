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

public sealed class YouTubePlatformAdapter : PlatformAdapterBase
{
    private static readonly Regex VideoIdPattern = new(@"^[A-Za-z0-9_\-]{11}$", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly IRateLimiter _ytRateLimiter;
    private const int UploadQuotaCost = 1600;
    private const int ListQuotaCost = 1;

    public YouTubePlatformAdapter(
        HttpClient httpClient,
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IRateLimiter rateLimiter,
        IOAuthManager oauthManager,
        IMediaStorage mediaStorage,
        ILogger<YouTubePlatformAdapter> logger)
        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
    {
        _httpClient = httpClient;
        _ytRateLimiter = rateLimiter;
    }

    public override PlatformType Type => PlatformType.YouTube;

    public override Task<Result<ContentValidation>> ValidateContentAsync(
        PlatformContent content, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content.Title))
            errors.Add("YouTube video requires a title");
        if (content.Media.Count == 0)
            errors.Add("YouTube video requires a video file");

        return Task.FromResult(Result.Success(
            new ContentValidation(errors.Count == 0, errors, [])));
    }

    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
        string accessToken, PlatformContent content, CancellationToken ct)
    {
        // Build video metadata
        var tags = content.Metadata.TryGetValue("tags", out var tagStr)
            ? tagStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

        var privacy = content.Metadata.TryGetValue("privacy", out var p) ? p : "public";

        var videoMetadata = new
        {
            snippet = new
            {
                title = content.Title ?? "Untitled",
                description = content.Text,
                tags,
                categoryId = "22", // People & Blogs default
            },
            status = new
            {
                privacyStatus = privacy,
            },
        };

        // Initiate resumable upload
        using var initRequest = new HttpRequestMessage(HttpMethod.Post,
            "/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
        initRequest.Headers.Authorization = new("Bearer", accessToken);
        initRequest.Content = JsonContent.Create(videoMetadata);

        var initResponse = await _httpClient.SendAsync(initRequest, ct);

        if (!initResponse.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(initResponse, "YouTube upload init");

        var uploadUrl = initResponse.Headers.Location?.ToString();
        if (string.IsNullOrEmpty(uploadUrl))
            return Result.Failure<PublishResult>(ErrorCode.InternalError, "YouTube upload: no resumable URL returned");

        // Upload video file (validation ensures Media.Count > 0)
        await using var videoStream = await MediaStorage.GetStreamAsync(content.Media[0].FileId, ct);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        uploadRequest.Headers.Authorization = new("Bearer", accessToken);
        uploadRequest.Content = new StreamContent(videoStream);
        uploadRequest.Content.Headers.ContentType = new("video/*");

        var uploadResponse = await _httpClient.SendAsync(uploadRequest, ct);

        if (!uploadResponse.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(uploadResponse, "YouTube video upload");

        var json = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var videoId = json.GetProperty("id").GetString()!;

        // Record YouTube quota cost (daily quota, resets at midnight Pacific)
        var pacificMidnight = DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(7); // UTC approximation of midnight PT
        await _ytRateLimiter.RecordRequestAsync(Type, "publish", UploadQuotaCost, pacificMidnight, ct);

        return Result.Success(new PublishResult(
            videoId, $"https://www.youtube.com/watch?v={videoId}", DateTimeOffset.UtcNow));
    }

    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!VideoIdPattern.IsMatch(platformPostId))
            return Result.Failure<Unit>(ErrorCode.ValidationFailed, "Invalid YouTube video ID format");

        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/youtube/v3/videos?id={platformPostId}");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? Result.Success(Unit.Value)
            : HandleHttpError<Unit>(response, "YouTube delete");
    }

    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!VideoIdPattern.IsMatch(platformPostId))
            return Result.Failure<EngagementStats>(ErrorCode.ValidationFailed, "Invalid YouTube video ID format");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/youtube/v3/videos?part=statistics&id={platformPostId}");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<EngagementStats>(response, "YouTube engagement");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = json.GetProperty("items");

        if (items.GetArrayLength() == 0)
            return Result.NotFound<EngagementStats>("Video not found");

        var stats = items[0].GetProperty("statistics");

        return Result.Success(new EngagementStats(
            Likes: ParseInt(stats, "likeCount"),
            Comments: ParseInt(stats, "commentCount"),
            Shares: 0,
            Impressions: ParseInt(stats, "viewCount"),
            Clicks: 0,
            PlatformSpecific: new Dictionary<string, int>
            {
                ["favoriteCount"] = ParseInt(stats, "favoriteCount"),
            }.AsReadOnly()));
    }

    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
        string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/youtube/v3/channels?part=snippet,statistics&mine=true");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PlatformProfile>(response, "YouTube profile");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = json.GetProperty("items");

        if (items.GetArrayLength() == 0)
            return Result.NotFound<PlatformProfile>("YouTube channel not found");

        var channel = items[0];
        var snippet = channel.GetProperty("snippet");

        return Result.Success(new PlatformProfile(
            PlatformUserId: channel.GetProperty("id").GetString()!,
            DisplayName: snippet.GetProperty("title").GetString()!,
            AvatarUrl: snippet.TryGetProperty("thumbnails", out var thumbs) &&
                       thumbs.TryGetProperty("default", out var def)
                ? def.GetProperty("url").GetString()
                : null,
            FollowerCount: channel.TryGetProperty("statistics", out var s) &&
                           s.TryGetProperty("subscriberCount", out var sc)
                ? int.TryParse(sc.GetString(), out var count) ? count : null
                : null));
    }

    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
        HttpResponseMessage response)
    {
        // YouTube API uses daily quota, not per-request rate limit headers.
        // Quota tracking is handled by the rate limiter's daily quota feature.
        return (null, null);
    }

    private static int ParseInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        int.TryParse(value.GetString() ?? value.ToString(), out var result)
            ? result
            : 0;
}
