using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public sealed class TwitterEngagementAdapter : ISocialEngagementAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IApplicationDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<TwitterEngagementAdapter> _logger;

    public TwitterEngagementAdapter(
        HttpClient httpClient,
        IApplicationDbContext db,
        IEncryptionService encryption,
        ILogger<TwitterEngagementAdapter> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    public PlatformType Platform => PlatformType.TwitterX;

    public async Task<Result<IReadOnlyList<EngagementTarget>>> FindRelevantPostsAsync(
        string targetCriteriaJson, int maxResults, CancellationToken ct)
    {
        var criteria = JsonSerializer.Deserialize<TwitterTargetCriteria>(targetCriteriaJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (criteria is null || (criteria.Keywords.Count == 0 && criteria.Hashtags.Count == 0))
            return Result.ValidationFailure<IReadOnlyList<EngagementTarget>>(
                ["Target criteria must include keywords or hashtags"]);

        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<IReadOnlyList<EngagementTarget>>(ErrorCode.Unauthorized, "Twitter not connected");

        var queryParts = new List<string>();
        queryParts.AddRange(criteria.Keywords.Select(k => $"\"{k}\""));
        queryParts.AddRange(criteria.Hashtags.Select(h => h.StartsWith('#') ? h : $"#{h}"));
        var query = string.Join(" OR ", queryParts) + " -is:retweet lang:en";

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/tweets/search/recent?query={Uri.EscapeDataString(query)}&max_results={Math.Min(maxResults, 10)}&tweet.fields=author_id,text,conversation_id");
        request.Headers.Authorization = new("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<IReadOnlyList<EngagementTarget>>(ErrorCode.InternalError, "Twitter search failed");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var targets = new List<EngagementTarget>();

        if (json.TryGetProperty("data", out var data))
        {
            foreach (var tweet in data.EnumerateArray())
            {
                var tweetId = tweet.GetProperty("id").GetString()!;
                targets.Add(new EngagementTarget(
                    PostId: tweetId,
                    PostUrl: $"https://x.com/i/status/{tweetId}",
                    Title: "",
                    Content: tweet.GetProperty("text").GetString() ?? "",
                    Community: "Twitter"));
            }
        }

        return Result.Success<IReadOnlyList<EngagementTarget>>(targets.AsReadOnly());
    }

    public async Task<Result<string>> PostCommentAsync(string postId, string text, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<string>(ErrorCode.Unauthorized, "Twitter not connected");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tweets");
        request.Headers.Authorization = new("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            text,
            reply = new { in_reply_to_tweet_id = postId },
        });

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<string>(ErrorCode.InternalError, "Twitter reply failed");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var replyId = json.GetProperty("data").GetProperty("id").GetString()!;
        return Result.Success(replyId);
    }

    public async Task<Result<IReadOnlyList<InboxEntry>>> PollInboxAsync(
        DateTimeOffset? since, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<IReadOnlyList<InboxEntry>>(ErrorCode.Unauthorized, "Twitter not connected");

        // Get mentions timeline
        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        meReq.Headers.Authorization = new("Bearer", token);
        var meResp = await _httpClient.SendAsync(meReq, ct);
        if (!meResp.IsSuccessStatusCode)
            return Result.Failure<IReadOnlyList<InboxEntry>>(ErrorCode.InternalError, "Failed to get Twitter user");

        var meJson = await meResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userId = meJson.GetProperty("data").GetProperty("id").GetString()!;

        var url = $"/users/{userId}/mentions?max_results=20&tweet.fields=created_at,author_id,text";
        if (since.HasValue)
            url += $"&start_time={since.Value:yyyy-MM-ddTHH:mm:ssZ}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Success<IReadOnlyList<InboxEntry>>(new List<InboxEntry>().AsReadOnly());

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var entries = new List<InboxEntry>();

        if (json.TryGetProperty("data", out var data))
        {
            foreach (var tweet in data.EnumerateArray())
            {
                var tweetId = tweet.GetProperty("id").GetString()!;
                var createdAt = tweet.TryGetProperty("created_at", out var ca)
                    ? DateTimeOffset.Parse(ca.GetString()!)
                    : DateTimeOffset.UtcNow;

                entries.Add(new InboxEntry(
                    PlatformItemId: tweetId,
                    ItemType: InboxItemType.Mention,
                    AuthorName: tweet.TryGetProperty("author_id", out var aid) ? aid.GetString()! : "Unknown",
                    AuthorProfileUrl: tweet.TryGetProperty("author_id", out var a2) ? $"https://x.com/i/user/{a2.GetString()}" : "",
                    Content: tweet.GetProperty("text").GetString() ?? "",
                    SourceUrl: $"https://x.com/i/status/{tweetId}",
                    ReceivedAt: createdAt));
            }
        }

        return Result.Success<IReadOnlyList<InboxEntry>>(entries.AsReadOnly());
    }

    public async Task<Result<string>> SendReplyAsync(string platformItemId, string text, CancellationToken ct)
    {
        return await PostCommentAsync(platformItemId, text, ct);
    }

    private async Task<string?> LoadTokenAsync(CancellationToken ct)
    {
        var platform = await _db.Platforms
            .FirstOrDefaultAsync(p => p.Type == PlatformType.TwitterX, ct);
        if (platform?.EncryptedAccessToken is null) return null;
        return _encryption.Decrypt(platform.EncryptedAccessToken);
    }

    private record TwitterTargetCriteria
    {
        public List<string> Keywords { get; init; } = [];
        public List<string> Hashtags { get; init; } = [];
    }
}
