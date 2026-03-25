using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public sealed class RedditPlatformAdapter : PlatformAdapterBase, ISocialEngagementAdapter
{
    private readonly HttpClient _httpClient;

    public RedditPlatformAdapter(
        HttpClient httpClient,
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IRateLimiter rateLimiter,
        IOAuthManager oauthManager,
        IMediaStorage mediaStorage,
        ILogger<RedditPlatformAdapter> logger)
        : base(dbContext, encryption, rateLimiter, oauthManager, mediaStorage, logger)
    {
        _httpClient = httpClient;
    }

    public override PlatformType Type => PlatformType.Reddit;

    PlatformType ISocialEngagementAdapter.Platform => PlatformType.Reddit;

    public override Task<Result<ContentValidation>> ValidateContentAsync(
        PlatformContent content, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content.Text))
            errors.Add("Post text cannot be empty");
        if (content.Title?.Length > 300)
            errors.Add("Reddit title exceeds 300 character limit");

        return Task.FromResult(Result.Success(
            new ContentValidation(errors.Count == 0, errors, [])));
    }

    protected override async Task<Result<PublishResult>> ExecutePublishAsync(
        string accessToken, PlatformContent content, CancellationToken ct)
    {
        var subreddit = content.Metadata.GetValueOrDefault("subreddit", "self");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/submit");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["kind"] = "self",
            ["sr"] = subreddit,
            ["title"] = content.Title ?? content.Text[..Math.Min(content.Text.Length, 300)],
            ["text"] = content.Text,
            ["api_type"] = "json",
        });

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(response, "Reddit submit");

        await RecordRateLimitAsync(response, "publish", ct);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = json.GetProperty("json").GetProperty("data");
        var postId = data.GetProperty("id").GetString()!;
        var postUrl = data.GetProperty("url").GetString()!;

        return Result.Success(new PublishResult(postId, postUrl, DateTimeOffset.UtcNow));
    }

    protected override async Task<Result<Unit>> ExecuteDeletePostAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/del");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = $"t3_{platformPostId}",
        });

        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? Result.Success(Unit.Value)
            : HandleHttpError<Unit>(response, "Reddit delete");
    }

    protected override async Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
        string accessToken, string platformPostId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/info?id=t3_{platformPostId}");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<EngagementStats>(response, "Reddit engagement");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var post = json.GetProperty("data").GetProperty("children")[0].GetProperty("data");

        return Result.Success(new EngagementStats(
            Likes: post.GetProperty("ups").GetInt32(),
            Comments: post.GetProperty("num_comments").GetInt32(),
            Shares: 0,
            Impressions: 0,
            Clicks: 0,
            PlatformSpecific: new Dictionary<string, int>
            {
                ["downs"] = post.TryGetProperty("downs", out var d) ? d.GetInt32() : 0,
                ["score"] = post.GetProperty("score").GetInt32(),
            }.AsReadOnly()));
    }

    protected override async Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
        string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PlatformProfile>(response, "Reddit profile");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        return Result.Success(new PlatformProfile(
            PlatformUserId: json.GetProperty("id").GetString()!,
            DisplayName: json.GetProperty("name").GetString()!,
            AvatarUrl: json.TryGetProperty("icon_img", out var avatar) ? avatar.GetString() : null,
            FollowerCount: null));
    }

    protected override (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
        HttpResponseMessage response)
    {
        int? remaining = null;
        DateTimeOffset? resetAt = null;

        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues) &&
            double.TryParse(remainingValues.FirstOrDefault(), out var r))
        {
            remaining = (int)r;
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues) &&
            int.TryParse(resetValues.FirstOrDefault(), out var seconds))
        {
            resetAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }

        return (remaining, resetAt);
    }

    // ISocialEngagementAdapter implementation

    public async Task<Result<IReadOnlyList<EngagementTarget>>> FindRelevantPostsAsync(
        string targetCriteriaJson, int maxResults, CancellationToken ct)
    {
        var criteria = JsonSerializer.Deserialize<RedditTargetCriteria>(targetCriteriaJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (criteria is null || criteria.Subreddits.Count == 0)
            return Result.ValidationFailure<IReadOnlyList<EngagementTarget>>(
                ["Target criteria must include at least one subreddit"]);

        var targets = new List<EngagementTarget>();

        var tokenResult = await GetAccessTokenAsync(ct);
        if (!tokenResult.IsSuccess)
            return Result.Failure<IReadOnlyList<EngagementTarget>>(ErrorCode.Unauthorized, "Reddit not authenticated");
        var accessToken = tokenResult.Value!;

        foreach (var subreddit in criteria.Subreddits)
        {
            if (targets.Count >= maxResults) break;

            var sort = criteria.Sort ?? "new";
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/r/{subreddit}/{sort}?limit=20&raw_json=1");
            request.Headers.Authorization = new("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) continue;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var children = json.GetProperty("data").GetProperty("children");

            foreach (var child in children.EnumerateArray())
            {
                if (targets.Count >= maxResults) break;

                var post = child.GetProperty("data");
                var title = post.GetProperty("title").GetString() ?? "";
                var selftext = post.TryGetProperty("selftext", out var st) ? st.GetString() ?? "" : "";

                if (criteria.Keywords.Count > 0 &&
                    !criteria.Keywords.Any(k =>
                        title.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                        selftext.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var numComments = post.TryGetProperty("num_comments", out var nc) ? nc.GetInt32() : 0;
                var createdUtc = post.TryGetProperty("created_utc", out var cu)
                    ? DateTimeOffset.FromUnixTimeSeconds((long)cu.GetDouble())
                    : DateTimeOffset.UtcNow;

                targets.Add(new EngagementTarget(
                    PostId: $"t3_{post.GetProperty("id").GetString()}",
                    PostUrl: $"https://reddit.com{post.GetProperty("permalink").GetString()}",
                    Title: title,
                    Content: selftext.Length > 500 ? selftext[..500] : selftext,
                    Community: subreddit,
                    CommentsCount: numComments,
                    CreatedAt: createdUtc));
            }
        }

        return Result.Success<IReadOnlyList<EngagementTarget>>(targets.AsReadOnly());
    }

    public async Task<Result<string>> PostCommentAsync(string postId, string text, CancellationToken ct)
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (!tokenResult.IsSuccess)
            return Result.Failure<string>(ErrorCode.Unauthorized, "Reddit not authenticated");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/comment");
        request.Headers.Authorization = new("Bearer", tokenResult.Value!);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["thing_id"] = postId,
            ["text"] = text,
            ["api_type"] = "json",
        });

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<string>(response, "Reddit comment");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var commentId = json.GetProperty("json").GetProperty("data")
            .GetProperty("things")[0].GetProperty("data")
            .GetProperty("id").GetString()!;

        return Result.Success(commentId);
    }

    public async Task<Result<IReadOnlyList<InboxEntry>>> PollInboxAsync(
        DateTimeOffset? since, CancellationToken ct)
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (!tokenResult.IsSuccess)
            return Result.Failure<IReadOnlyList<InboxEntry>>(ErrorCode.Unauthorized, "Reddit not authenticated");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/message/inbox?limit=25&raw_json=1");
        request.Headers.Authorization = new("Bearer", tokenResult.Value!);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<IReadOnlyList<InboxEntry>>(response, "Reddit inbox poll");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var children = json.GetProperty("data").GetProperty("children");
        var entries = new List<InboxEntry>();

        foreach (var child in children.EnumerateArray())
        {
            var data = child.GetProperty("data");
            var created = DateTimeOffset.FromUnixTimeSeconds(
                (long)data.GetProperty("created_utc").GetDouble());

            if (since.HasValue && created <= since.Value) continue;

            var kind = child.GetProperty("kind").GetString();
            var itemType = kind switch
            {
                "t1" => InboxItemType.Comment,
                "t4" => InboxItemType.DirectMessage,
                _ => InboxItemType.Reply,
            };

            entries.Add(new InboxEntry(
                PlatformItemId: data.GetProperty("name").GetString()!,
                ItemType: itemType,
                AuthorName: data.GetProperty("author").GetString() ?? "[deleted]",
                AuthorProfileUrl: $"https://reddit.com/u/{data.GetProperty("author").GetString()}",
                Content: data.GetProperty("body").GetString() ?? "",
                SourceUrl: data.TryGetProperty("context", out var ctx)
                    ? $"https://reddit.com{ctx.GetString()}" : "",
                ReceivedAt: created));
        }

        return Result.Success<IReadOnlyList<InboxEntry>>(entries.AsReadOnly());
    }

    public async Task<Result<string>> SendReplyAsync(string platformItemId, string text, CancellationToken ct)
    {
        return await PostCommentAsync(platformItemId, text, ct);
    }

    private record RedditTargetCriteria
    {
        public List<string> Subreddits { get; init; } = [];
        public List<string> Keywords { get; init; } = [];
        public string? Sort { get; init; }
    }
}
