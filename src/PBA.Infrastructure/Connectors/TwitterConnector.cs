using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Connectors;

public sealed class TwitterConnector(
    HttpClient httpClient,
    IAppDbContext db,
    ITokenEncryptor encryptor,
    IOAuthService oauthService,
    IOptionsMonitor<TwitterOptions> options,
    ILogger<TwitterConnector> logger) : IPlatformConnector
{
    // Twitter access tokens expire after 2 hours; refresh early to avoid mid-thread failures
    private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(10);
    private const int MaxMediaPollAttempts = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Platform Platform => Platform.Twitter;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            if (!options.CurrentValue.Enabled)
                return new PlatformPublishResult(false, null, null,
                    "Twitter publishing is not enabled. Enable it in Settings.");

            if (request.Mode is PublishMode.Draft or PublishMode.Schedule)
                return new PlatformPublishResult(false, null, null,
                    "Twitter does not support draft or scheduled posts via API.");

            var credential = await GetActiveCredentialAsync(ct);
            var token = await GetValidTokenAsync(credential, ct);
            if (token is null)
                return new PlatformPublishResult(false, null, null,
                    "Twitter token refresh failed. Please reconnect in Settings.");

            var segments = ParseSegments(request.TransformedContent);

            string? firstTweetId = null;
            string? previousTweetId = null;

            for (var i = 0; i < segments.Count; i++)
            {
                var payload = new TweetPayload(segments[i],
                    previousTweetId is not null ? new TweetReply(previousTweetId) : null);

                var json = JsonSerializer.Serialize(payload, JsonOptions);

                using var tweetRequest = new HttpRequestMessage(HttpMethod.Post, "/2/tweets")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                tweetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await httpClient.SendAsync(tweetRequest, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    var errorMsg = response.StatusCode switch
                    {
                        HttpStatusCode.TooManyRequests => "Twitter rate limit exceeded. Retry scheduled.",
                        HttpStatusCode.Unauthorized => "Twitter authentication failed. Please reconnect in Settings.",
                        HttpStatusCode.Forbidden => "Twitter API access denied. Check app permissions.",
                        _ => $"Twitter publish failed ({response.StatusCode})"
                    };

                    logger.LogError("Twitter publish failed at segment {Index}/{Total}: {Status} {Body}",
                        i + 1, segments.Count, response.StatusCode, errorBody);

                    if (firstTweetId is not null)
                        return new PlatformPublishResult(false,
                            $"https://x.com/i/status/{firstTweetId}", firstTweetId,
                            $"Thread partially published ({i}/{segments.Count} tweets). {errorMsg}");

                    return new PlatformPublishResult(false, null, null, errorMsg);
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var tweetResponse = JsonSerializer.Deserialize<TwitterTweetResponse>(responseJson, JsonOptions);

                if (tweetResponse?.Data?.Id is null)
                    return new PlatformPublishResult(false, null, null,
                        "Twitter returned an unexpected response format.");

                previousTweetId = tweetResponse.Data.Id;
                firstTweetId ??= previousTweetId;
            }

            var publishedUrl = $"https://x.com/i/status/{firstTweetId}";
            return new PlatformPublishResult(true, publishedUrl, firstTweetId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to Twitter");
            return new PlatformPublishResult(false, null, null,
                "An unexpected error occurred while publishing to Twitter. Check logs for details.");
        }
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var credential = await GetActiveCredentialAsync(ct);
            var token = await GetValidTokenAsync(credential, ct);
            if (token is null) return false;

            using var request = new HttpRequestMessage(HttpMethod.Get, "/2/users/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(request, ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Twitter credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: 280,
        SupportsMarkdown: false,
        SupportsHtml: false,
        SupportsImages: true,
        SupportsScheduling: false,
        SupportsThreads: true,
        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "video/mp4"]
    );

    internal async Task<string?> UploadMediaAsync(
        string mediaType, long totalBytes, byte[] data, string token, CancellationToken ct)
    {
        var initPayload = new MediaInitPayload(mediaType, totalBytes);
        var initJson = JsonSerializer.Serialize(initPayload, JsonOptions);

        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "/2/media/upload/initialize")
        {
            Content = new StringContent(initJson, Encoding.UTF8, "application/json")
        };
        initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var initResponse = await httpClient.SendAsync(initRequest, ct);
        if (!initResponse.IsSuccessStatusCode)
        {
            var body = await initResponse.Content.ReadAsStringAsync(ct);
            // v2 media endpoints may return 403 with OAuth 2.0; v1.1 fallback deferred
            logger.LogError("Twitter media INIT failed: {Status} {Body}", initResponse.StatusCode, body);
            return null;
        }

        var initResult = JsonSerializer.Deserialize<TwitterMediaInitResponse>(
            await initResponse.Content.ReadAsStringAsync(ct), JsonOptions);
        if (initResult?.MediaId is null) return null;

        var mediaId = initResult.MediaId;

        using var appendRequest = new HttpRequestMessage(HttpMethod.Post, $"/2/media/upload/{mediaId}/append");
        appendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(new StringContent(Convert.ToBase64String(data)), "media_data");
        multipartContent.Add(new StringContent("0"), "segment_index");
        appendRequest.Content = multipartContent;

        var appendResponse = await httpClient.SendAsync(appendRequest, ct);
        if (!appendResponse.IsSuccessStatusCode)
        {
            logger.LogError("Twitter media APPEND failed: {Status}", appendResponse.StatusCode);
            return null;
        }

        using var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, $"/2/media/upload/{mediaId}/finalize");
        finalizeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var finalizeResponse = await httpClient.SendAsync(finalizeRequest, ct);
        if (!finalizeResponse.IsSuccessStatusCode)
        {
            logger.LogError("Twitter media FINALIZE failed: {Status}", finalizeResponse.StatusCode);
            return null;
        }

        var finalizeResult = JsonSerializer.Deserialize<TwitterMediaFinalizeResponse>(
            await finalizeResponse.Content.ReadAsStringAsync(ct), JsonOptions);

        if (finalizeResult?.ProcessingInfo is not null)
            await PollMediaProcessingAsync(mediaId, token, ct);

        return mediaId;
    }

    private async Task PollMediaProcessingAsync(string mediaId, string token, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxMediaPollAttempts; attempt++)
        {
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"/2/media/upload/{mediaId}");
            statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var statusResponse = await httpClient.SendAsync(statusRequest, ct);
            if (!statusResponse.IsSuccessStatusCode) break;

            var statusResult = JsonSerializer.Deserialize<TwitterMediaFinalizeResponse>(
                await statusResponse.Content.ReadAsStringAsync(ct), JsonOptions);

            var state = statusResult?.ProcessingInfo?.State;
            if (state is "succeeded" or "failed" or null)
                break;

            var waitSecs = statusResult?.ProcessingInfo?.CheckAfterSecs ?? 5;
            await Task.Delay(TimeSpan.FromSeconds(waitSecs), ct);
        }
    }

    private async Task<string?> GetValidTokenAsync(PlatformCredential credential, CancellationToken ct)
    {
        var token = encryptor.Decrypt(credential.EncryptedAccessToken);

        if (credential.AccessTokenExpiresAt.HasValue &&
            credential.AccessTokenExpiresAt.Value - DateTimeOffset.UtcNow < TokenRefreshWindow)
        {
            var refreshResult = await oauthService.RefreshTokenAsync(credential, ct);
            if (!refreshResult.IsSuccess)
            {
                logger.LogWarning("Twitter token refresh failed: {Errors}", string.Join(", ", refreshResult.Errors));
                return null;
            }
            token = refreshResult.Value!;
        }

        return token;
    }

    private async Task<PlatformCredential> GetActiveCredentialAsync(CancellationToken ct)
    {
        return await db.PlatformCredentials
            .FirstOrDefaultAsync(c => c.Platform == Platform.Twitter && c.IsActive, ct)
            ?? throw new InvalidOperationException("No active Twitter credential found");
    }

    private static List<string> ParseSegments(string transformedContent)
    {
        if (transformedContent.StartsWith('['))
        {
            try
            {
                var segments = JsonSerializer.Deserialize<string[]>(transformedContent);
                if (segments is { Length: > 0 })
                    return [.. segments];
            }
            catch (JsonException)
            {
                // Content starts with '[' but is not a JSON thread array
            }
        }

        return [transformedContent];
    }

    private record TweetPayload(string Text, TweetReply? Reply = null, TweetMedia? Media = null);
    private record TweetReply(string InReplyToTweetId);
    private record TweetMedia(IReadOnlyList<string> MediaIds);
    private record MediaInitPayload(string MediaType, long TotalBytes);

    internal record TwitterTweetResponse(TwitterTweetData? Data);
    internal record TwitterTweetData(string Id, string? Text);
    internal record TwitterMediaInitResponse(string? MediaId);
    internal record TwitterMediaFinalizeResponse(string? MediaId, TwitterProcessingInfo? ProcessingInfo);
    internal record TwitterProcessingInfo(string State, int? CheckAfterSecs);
    internal record TwitterErrorResponse(string? Title, int? Status, string? Detail);
}
