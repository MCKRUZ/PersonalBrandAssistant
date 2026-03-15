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
        // Get user URN for post authorship
        var profileResult = await ExecuteGetProfileAsync(accessToken, ct);
        if (!profileResult.IsSuccess)
            return Result.Failure<PublishResult>(ErrorCode.InternalError, "Failed to get LinkedIn profile for post authorship");

        var authorUrn = $"urn:li:person:{profileResult.Value!.PlatformUserId}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/posts");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        request.Headers.Add("Linkedin-Version", _options.ApiVersion ?? "202401");

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

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return HandleHttpError<PublishResult>(response, "LinkedIn publish");

        await RecordRateLimitAsync(response, "publish", ct);

        // LinkedIn returns the post URN in the x-restli-id header
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
        using var request = new HttpRequestMessage(HttpMethod.Get, "/userinfo");
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
