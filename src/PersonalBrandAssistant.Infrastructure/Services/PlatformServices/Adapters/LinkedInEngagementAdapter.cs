using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public sealed class LinkedInEngagementAdapter : ISocialEngagementAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IApplicationDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<LinkedInEngagementAdapter> _logger;

    public LinkedInEngagementAdapter(
        HttpClient httpClient,
        IApplicationDbContext db,
        IEncryptionService encryption,
        ILogger<LinkedInEngagementAdapter> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    public PlatformType Platform => PlatformType.LinkedIn;

    public async Task<Result<IReadOnlyList<EngagementTarget>>> FindRelevantPostsAsync(
        string targetCriteriaJson, int maxResults, CancellationToken ct)
    {
        var criteria = JsonSerializer.Deserialize<LinkedInTargetCriteria>(targetCriteriaJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (criteria is null || criteria.Keywords.Count == 0)
            return Result.ValidationFailure<IReadOnlyList<EngagementTarget>>(
                ["Target criteria must include keywords"]);

        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<IReadOnlyList<EngagementTarget>>(ErrorCode.Unauthorized, "LinkedIn not connected");

        // LinkedIn API v2 doesn't have a public post search endpoint for regular apps.
        // Use the feed to find posts matching keywords.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/posts?q=author&count={Math.Min(maxResults, 10)}");
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LinkedIn post search failed with {StatusCode}", response.StatusCode);
            return Result.Success<IReadOnlyList<EngagementTarget>>(
                new List<EngagementTarget>().AsReadOnly());
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var targets = new List<EngagementTarget>();

        if (json.TryGetProperty("elements", out var elements))
        {
            foreach (var post in elements.EnumerateArray())
            {
                if (targets.Count >= maxResults) break;

                var commentary = post.TryGetProperty("commentary", out var c) ? c.GetString() ?? "" : "";

                if (criteria.Keywords.Any(k =>
                    commentary.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    var postId = post.GetProperty("id").GetString()!;
                    targets.Add(new EngagementTarget(
                        PostId: postId,
                        PostUrl: $"https://www.linkedin.com/feed/update/{postId}",
                        Title: "",
                        Content: commentary.Length > 500 ? commentary[..500] : commentary,
                        Community: "LinkedIn"));
                }
            }
        }

        return Result.Success<IReadOnlyList<EngagementTarget>>(targets.AsReadOnly());
    }

    public async Task<Result<string>> PostCommentAsync(string postId, string text, CancellationToken ct)
    {
        var token = await LoadTokenAsync(ct);
        if (token is null)
            return Result.Failure<string>(ErrorCode.Unauthorized, "LinkedIn not connected");

        // Get actor URN
        using var profileReq = new HttpRequestMessage(HttpMethod.Get, "/userinfo");
        profileReq.Headers.Authorization = new("Bearer", token);
        var profileResp = await _httpClient.SendAsync(profileReq, ct);
        if (!profileResp.IsSuccessStatusCode)
            return Result.Failure<string>(ErrorCode.InternalError, "Failed to get LinkedIn profile");

        var profileJson = await profileResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var actorUrn = $"urn:li:person:{profileJson.GetProperty("sub").GetString()}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/socialActions/{postId}/comments");
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        request.Content = JsonContent.Create(new
        {
            actor = actorUrn,
            message = new { text },
        });

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<string>(ErrorCode.InternalError, "LinkedIn comment failed");

        return Result.Success("comment-posted");
    }

    public Task<Result<IReadOnlyList<InboxEntry>>> PollInboxAsync(
        DateTimeOffset? since, CancellationToken ct)
    {
        // LinkedIn API doesn't provide a messaging inbox endpoint for regular apps.
        // Notifications can be polled via partner programs only.
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
            .FirstOrDefaultAsync(p => p.Type == PlatformType.LinkedIn, ct);
        if (platform?.EncryptedAccessToken is null) return null;
        return _encryption.Decrypt(platform.EncryptedAccessToken);
    }

    private record LinkedInTargetCriteria
    {
        public List<string> Keywords { get; init; } = [];
    }
}
