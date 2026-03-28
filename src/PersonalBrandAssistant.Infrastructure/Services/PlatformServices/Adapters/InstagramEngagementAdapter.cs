using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public sealed class InstagramEngagementAdapter : ISocialEngagementAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IApplicationDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<InstagramEngagementAdapter> _logger;

    public InstagramEngagementAdapter(
        HttpClient httpClient,
        IApplicationDbContext db,
        IEncryptionService encryption,
        ILogger<InstagramEngagementAdapter> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    public PlatformType Platform => PlatformType.Instagram;

    public async Task<Result<IReadOnlyList<EngagementTarget>>> FindRelevantPostsAsync(
        string targetCriteriaJson, int maxResults, CancellationToken ct)
    {
        var criteria = JsonSerializer.Deserialize<InstagramTargetCriteria>(targetCriteriaJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (criteria is null || criteria.Hashtags.Count == 0)
            return Result.ValidationFailure<IReadOnlyList<EngagementTarget>>(
                ["Target criteria must include hashtags"]);

        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<IReadOnlyList<EngagementTarget>>(ErrorCode.Unauthorized, "Instagram not connected");

        var targets = new List<EngagementTarget>();

        foreach (var hashtag in criteria.Hashtags)
        {
            if (targets.Count >= maxResults) break;

            // Step 1: Search for hashtag ID
            // WARNING: access token appears in URL (Meta Graph API convention).
            using var searchReq = new HttpRequestMessage(HttpMethod.Get,
                $"/ig_hashtag_search?q={Uri.EscapeDataString(hashtag)}&access_token={token}");
            var searchResp = await _httpClient.SendAsync(searchReq, ct);
            if (!searchResp.IsSuccessStatusCode) continue;

            var searchJson = await searchResp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!searchJson.TryGetProperty("data", out var hashtagData) ||
                hashtagData.GetArrayLength() == 0) continue;

            var hashtagId = hashtagData[0].GetProperty("id").GetString()!;

            // Step 2: Get recent media for hashtag
            // WARNING: access token appears in URL (Meta Graph API convention).
            var userIdResult = await GetUserIdAsync(token, ct);
            if (userIdResult is null) continue;

            using var mediaReq = new HttpRequestMessage(HttpMethod.Get,
                $"/{hashtagId}/recent_media?user_id={userIdResult}&fields=id,caption,permalink&limit={Math.Min(maxResults - targets.Count, 10)}&access_token={token}");
            var mediaResp = await _httpClient.SendAsync(mediaReq, ct);
            if (!mediaResp.IsSuccessStatusCode) continue;

            var mediaJson = await mediaResp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (mediaJson.TryGetProperty("data", out var media))
            {
                foreach (var post in media.EnumerateArray())
                {
                    if (targets.Count >= maxResults) break;

                    targets.Add(new EngagementTarget(
                        PostId: post.GetProperty("id").GetString()!,
                        PostUrl: post.TryGetProperty("permalink", out var pl) ? pl.GetString() ?? "" : "",
                        Title: "",
                        Content: post.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "",
                        Community: $"#{hashtag}"));
                }
            }
        }

        return Result.Success<IReadOnlyList<EngagementTarget>>(targets.AsReadOnly());
    }

    public async Task<Result<string>> PostCommentAsync(string postId, string text, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<string>(ErrorCode.Unauthorized, "Instagram not connected");

        // WARNING: access token appears in URL (Meta Graph API convention).
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/{postId}/comments?message={Uri.EscapeDataString(text)}&access_token={token}");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<string>(ErrorCode.InternalError, "Instagram comment failed");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var commentId = json.GetProperty("id").GetString()!;
        return Result.Success(commentId);
    }

    public Task<Result<IReadOnlyList<InboxEntry>>> PollInboxAsync(
        DateTimeOffset? since, CancellationToken ct)
    {
        // Instagram DM/comment polling requires Instagram Messaging API
        // which needs approved app review. Return empty for now.
        return Task.FromResult(Result.Success<IReadOnlyList<InboxEntry>>(
            new List<InboxEntry>().AsReadOnly()));
    }

    public async Task<Result<string>> SendReplyAsync(string platformItemId, string text, CancellationToken ct)
    {
        return await PostCommentAsync(platformItemId, text, ct);
    }

    private async Task<string?> LoadTokenAsync(CancellationToken ct)
    {
        var platform = await _db.Platforms
            .FirstOrDefaultAsync(p => p.Type == PlatformType.Instagram, ct);
        if (platform?.EncryptedAccessToken is null) return null;
        return _encryption.Decrypt(platform.EncryptedAccessToken);
    }

    private async Task<string?> GetUserIdAsync(string token, CancellationToken ct)
    {
        // WARNING: access token appears in URL (Meta Graph API convention).
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/me?fields=id&access_token={token}");
        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString();
    }

    private record InstagramTargetCriteria
    {
        public List<string> Hashtags { get; init; } = [];
    }
}
