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

public sealed partial class TwitterPlatformAdapter : PlatformAdapterBase
{
    private static readonly Regex TweetIdPattern = TweetIdRegex();
    private readonly HttpClient _httpClient;

    public TwitterPlatformAdapter(
        HttpClient httpClient,
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IRateLimiter rateLimiter,
        IOAuthManager oauthManager,
        IMediaStorage mediaStorage,
        ILogger<TwitterPlatformAdapter> logger)
        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
    {
        _httpClient = httpClient;
    }

    public override PlatformType Type => PlatformType.TwitterX;

    public override Task<Result<ContentValidation>> ValidateContentAsync(
        PlatformContent content, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content.Text))
            errors.Add("Tweet text cannot be empty");
        else if (content.Text.Length > 280)
            errors.Add("Tweet exceeds 280 character limit");

        return Task.FromResult(Result.Success(
            new ContentValidation(errors.Count == 0, errors, [])));
    }

    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
        string accessToken, PlatformContent content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tweets");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(new { text = content.Text });

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(response, "Tweet publish");

        await RecordRateLimitAsync(response, "publish", ct);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var tweetId = json.GetProperty("data").GetProperty("id").GetString()!;
        var postUrl = $"https://x.com/i/status/{tweetId}";

        // Handle thread if present
        if (content.Metadata.Keys.Any(k => k.StartsWith("thread:")))
        {
            var previousId = tweetId;
            foreach (var key in content.Metadata.Keys.Where(k => k.StartsWith("thread:")).OrderBy(k => k))
            {
                var threadText = content.Metadata[key];
                using var threadReq = new HttpRequestMessage(HttpMethod.Post, "/tweets");
                threadReq.Headers.Authorization = new("Bearer", accessToken);
                threadReq.Content = JsonContent.Create(new
                {
                    text = threadText,
                    reply = new { in_reply_to_tweet_id = previousId },
                });

                var threadResp = await _httpClient.SendAsync(threadReq, ct);
                if (!threadResp.IsSuccessStatusCode)
                {
                    var totalThreadTweets = content.Metadata.Keys.Count(k => k.StartsWith("thread:"));
                    var postedCount = content.Metadata.Keys
                        .Where(k => k.StartsWith("thread:")).OrderBy(k => k)
                        .TakeWhile(k => k != key).Count();
                    Logger.LogWarning("Thread tweet failed at {Key}, {Posted}/{Total} tweets posted",
                        key, postedCount, totalThreadTweets);
                    return Result.Failure<PublishResult>(ErrorCode.InternalError,
                        $"Thread partially published: {postedCount}/{totalThreadTweets} tweets posted. First tweet: {tweetId}");
                }

                var threadJson = await threadResp.Content.ReadFromJsonAsync<JsonElement>(ct);
                previousId = threadJson.GetProperty("data").GetProperty("id").GetString()!;
            }
        }

        return Result.Success(new PublishResult(tweetId, postUrl, DateTimeOffset.UtcNow));
    }

    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!TweetIdPattern.IsMatch(platformPostId))
            return Result.Failure<Unit>(ErrorCode.ValidationFailed, "Invalid tweet ID format");

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/tweets/{platformPostId}");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? Result.Success(Unit.Value)
            : HandleHttpError<Unit>(response, "Tweet delete");
    }

    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        if (!TweetIdPattern.IsMatch(platformPostId))
            return Result.Failure<EngagementStats>(ErrorCode.ValidationFailed, "Invalid tweet ID format");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/tweets/{platformPostId}?tweet.fields=public_metrics");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<EngagementStats>(response, "Tweet engagement");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var metrics = json.GetProperty("data").GetProperty("public_metrics");

        return Result.Success(new EngagementStats(
            Likes: metrics.GetProperty("like_count").GetInt32(),
            Comments: metrics.GetProperty("reply_count").GetInt32(),
            Shares: metrics.GetProperty("retweet_count").GetInt32(),
            Impressions: metrics.TryGetProperty("impression_count", out var imp) ? imp.GetInt32() : 0,
            Clicks: 0,
            PlatformSpecific: new Dictionary<string, int>
            {
                ["quote_count"] = metrics.TryGetProperty("quote_count", out var q) ? q.GetInt32() : 0,
                ["bookmark_count"] = metrics.TryGetProperty("bookmark_count", out var b) ? b.GetInt32() : 0,
            }.AsReadOnly()));
    }

    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
        string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/users/me?user.fields=profile_image_url,public_metrics");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PlatformProfile>(response, "Twitter profile");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = json.GetProperty("data");

        return Result.Success(new PlatformProfile(
            PlatformUserId: data.GetProperty("id").GetString()!,
            DisplayName: data.GetProperty("name").GetString()!,
            AvatarUrl: data.TryGetProperty("profile_image_url", out var avatar) ? avatar.GetString() : null,
            FollowerCount: data.TryGetProperty("public_metrics", out var pm)
                ? pm.GetProperty("followers_count").GetInt32()
                : null));
    }

    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
        HttpResponseMessage response)
    {
        int? remaining = null;
        DateTimeOffset? resetAt = null;

        if (response.Headers.TryGetValues("x-rate-limit-remaining", out var remainingValues) &&
            int.TryParse(remainingValues.FirstOrDefault(), out var r))
        {
            remaining = r;
        }

        if (response.Headers.TryGetValues("x-rate-limit-reset", out var resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out var epoch))
        {
            resetAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        return (remaining, resetAt);
    }

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex TweetIdRegex();
}
