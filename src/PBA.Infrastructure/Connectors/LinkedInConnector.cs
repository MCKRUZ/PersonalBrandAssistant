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

public sealed class LinkedInConnector(
    HttpClient httpClient,
    IAppDbContext db,
    ITokenEncryptor encryptor,
    IOAuthService oauthService,
    IOptionsMonitor<LinkedInOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<LinkedInConnector> logger) : IPlatformConnector
{
    private readonly IOptionsMonitor<LinkedInOptions> _options = options;
    private const string LinkedInVersion = "202604";
    private const string RestliProtocolVersion = "2.0.0";
    private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(5);

    // Video processing after finalizeUpload is async on LinkedIn's side; a member video must reach
    // AVAILABLE before it can be attached to a post. Poll for up to ~2 minutes.
    private static readonly TimeSpan VideoStatusPollInterval = TimeSpan.FromSeconds(3);
    private const int VideoStatusMaxPolls = 40;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Platform Platform => Platform.LinkedIn;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            if (request.Mode is PublishMode.Draft or PublishMode.Schedule)
                return new PlatformPublishResult(false, null, null,
                    "LinkedIn API does not support draft or scheduled posts. Content can only be published immediately.");

            var credential = await GetActiveCredentialAsync(ct);
            var token = await GetValidTokenAsync(credential, ct);
            if (token is null)
                return new PlatformPublishResult(false, null, null,
                    "LinkedIn token refresh failed. Please reconnect in Settings.");

            var personUrn = await GetPersonUrnAsync(token, ct);
            if (personUrn is null)
                return new PlatformPublishResult(false, null, null,
                    "LinkedIn access token is invalid or expired. Please reconnect in Settings.");

            // A native media attachment (video or image) is uploaded to LinkedIn first, then
            // referenced by URN in the post. Video and image use different LinkedIn APIs; an
            // unsupported media type is rejected rather than silently dropped.
            UploadedMedia? uploaded = null;
            if (request.Media is { } media)
            {
                if (media.IsVideo)
                {
                    var urn = await UploadMemberVideoAsync(personUrn, token, media, ct);
                    if (urn is null)
                        return new PlatformPublishResult(false, null, null,
                            "LinkedIn video upload failed. Check logs for details.");
                    uploaded = new UploadedMedia(urn, MediaKind.Video);
                }
                else if (media.IsImage)
                {
                    var urn = await UploadMemberImageAsync(personUrn, token, media, ct);
                    if (urn is null)
                        return new PlatformPublishResult(false, null, null,
                            "LinkedIn image upload failed. Check logs for details.");
                    uploaded = new UploadedMedia(urn, MediaKind.Image);
                }
                else
                {
                    return new PlatformPublishResult(false, null, null,
                        $"LinkedIn does not support this media type ({media.ContentType}, {media.FileName}). " +
                        "Supported: MP4/MOV video and PNG/JPEG/GIF images.");
                }
            }

            var payload = BuildPostPayload(personUrn, request, uploaded);

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/rest/posts")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            SetLinkedInHeaders(postRequest, token);

            var response = await httpClient.SendAsync(postRequest, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new PlatformPublishResult(false, null, null, "LinkedIn rate limit exceeded. Retry scheduled.");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new PlatformPublishResult(false, null, null,
                    "LinkedIn access token is invalid or expired. Please reconnect in Settings.");

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new PlatformPublishResult(false, null, null,
                    "LinkedIn API access denied. Verify your app has w_member_social scope.");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("LinkedIn publish failed: {Status} {Body}", response.StatusCode, errorBody);
                return new PlatformPublishResult(false, null, null, $"LinkedIn publish failed ({response.StatusCode})");
            }

            var postUrn = response.Headers.TryGetValues("x-restli-id", out var values)
                ? values.FirstOrDefault()
                : null;

            var publishedUrl = postUrn is not null
                ? $"https://www.linkedin.com/feed/update/{postUrn}"
                : null;

            return new PlatformPublishResult(true, publishedUrl, postUrn, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to LinkedIn");
            return new PlatformPublishResult(false, null, null,
                "An unexpected error occurred while publishing to LinkedIn. Check logs for details.");
        }
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var credential = await GetActiveCredentialAsync(ct);
            var token = await GetValidTokenAsync(credential, ct);
            if (token is null) return false;
            return await GetPersonUrnAsync(token, ct) is not null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LinkedIn credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: 3000,
        SupportsMarkdown: false,
        SupportsHtml: false,
        SupportsImages: true,
        SupportsScheduling: false,
        SupportsThreads: false,
        SupportedMediaTypes: ["video/mp4", "video/quicktime", "image/png", "image/jpeg", "image/gif"]
    );

    // ---- Video upload (LinkedIn Videos API) -----------------------------------------------------

    /// <summary>
    /// Uploads a member-owned video via the LinkedIn Videos API and returns its URN once AVAILABLE.
    /// Flow: POST initializeUpload -> PUT each 4 MB part to its pre-signed URL (capturing ETags) ->
    /// POST finalizeUpload -> poll GET /rest/videos/{urn} until status == AVAILABLE. Returns null on
    /// any failure (already logged).
    /// </summary>
    private async Task<string?> UploadMemberVideoAsync(
        string personUrn, string token, MediaAttachment media, CancellationToken ct)
    {
        // 1. Initialize — LinkedIn slices fileSizeBytes into 4 MB parts and returns a pre-signed
        //    upload URL per part.
        var initPayload = new
        {
            initializeUploadRequest = new
            {
                owner = personUrn,
                fileSizeBytes = media.Data.Length,
                uploadCaptions = false,
                uploadThumbnail = false
            }
        };

        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "/rest/videos?action=initializeUpload")
        {
            Content = new StringContent(JsonSerializer.Serialize(initPayload, JsonOptions), Encoding.UTF8, "application/json")
        };
        SetLinkedInHeaders(initRequest, token);

        var initResponse = await httpClient.SendAsync(initRequest, ct);
        if (!initResponse.IsSuccessStatusCode)
        {
            var body = await initResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("LinkedIn video initializeUpload failed: {Status} {Body}", initResponse.StatusCode, body);
            return null;
        }

        var initBody = await initResponse.Content.ReadAsStringAsync(ct);
        var init = JsonSerializer.Deserialize<VideoInitResponse>(initBody, JsonOptions);
        var value = init?.Value;
        if (value?.Video is null || value.UploadInstructions is not { Count: > 0 })
        {
            logger.LogError("LinkedIn video initializeUpload returned no upload instructions: {Body}", initBody);
            return null;
        }

        // 2. Upload each byte range with a PUT to its pre-signed URL. These URLs carry their own
        //    signature — no bearer token — and can be large, so use a bare, long-timeout client
        //    rather than the resilience-wrapped api.linkedin.com client.
        using var uploadClient = httpClientFactory.CreateClient();
        uploadClient.Timeout = TimeSpan.FromMinutes(10);

        var uploadedPartIds = new List<string>(value.UploadInstructions.Count);
        foreach (var part in value.UploadInstructions.OrderBy(p => p.FirstByte))
        {
            var offset = (int)part.FirstByte;
            var count = (int)(part.LastByte - part.FirstByte + 1);
            using var partContent = new ByteArrayContent(media.Data, offset, count);
            partContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var putRequest = new HttpRequestMessage(HttpMethod.Put, part.UploadUrl) { Content = partContent };
            var putResponse = await uploadClient.SendAsync(putRequest, ct);
            if (!putResponse.IsSuccessStatusCode)
            {
                logger.LogError("LinkedIn video part upload failed at byte {Offset}: {Status}", offset, putResponse.StatusCode);
                return null;
            }

            // The upload response returns the part id in the ETag header; finalize needs them in order.
            var etag = putResponse.Headers.TryGetValues("ETag", out var etagValues)
                ? etagValues.FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(etag))
            {
                logger.LogError("LinkedIn video part upload missing ETag at byte {Offset}", offset);
                return null;
            }

            uploadedPartIds.Add(etag.Trim('"'));
        }

        // 3. Finalize — echo the uploadToken back verbatim (empty string is normal for single-part).
        var finalizePayload = new
        {
            finalizeUploadRequest = new
            {
                video = value.Video,
                uploadToken = value.UploadToken ?? string.Empty,
                uploadedPartIds
            }
        };

        using var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, "/rest/videos?action=finalizeUpload")
        {
            Content = new StringContent(JsonSerializer.Serialize(finalizePayload, JsonOptions), Encoding.UTF8, "application/json")
        };
        SetLinkedInHeaders(finalizeRequest, token);

        var finalizeResponse = await httpClient.SendAsync(finalizeRequest, ct);
        if (!finalizeResponse.IsSuccessStatusCode)
        {
            var body = await finalizeResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("LinkedIn video finalizeUpload failed: {Status} {Body}", finalizeResponse.StatusCode, body);
            return null;
        }

        // 4. Wait for processing to complete before the video can be attached to a post.
        return await WaitForVideoAvailableAsync(value.Video, token, ct) ? value.Video : null;
    }

    /// <summary>
    /// Uploads a member-owned image via the LinkedIn Images API and returns its URN once AVAILABLE.
    /// Simpler than video: a single PUT (no chunking, ETag, or finalize). Unlike video, the upload
    /// URL requires the bearer token. Returns null on any failure (already logged).
    /// </summary>
    private async Task<string?> UploadMemberImageAsync(
        string personUrn, string token, MediaAttachment media, CancellationToken ct)
    {
        var initPayload = new { initializeUploadRequest = new { owner = personUrn } };

        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "/rest/images?action=initializeUpload")
        {
            Content = new StringContent(JsonSerializer.Serialize(initPayload, JsonOptions), Encoding.UTF8, "application/json")
        };
        SetLinkedInHeaders(initRequest, token);

        var initResponse = await httpClient.SendAsync(initRequest, ct);
        if (!initResponse.IsSuccessStatusCode)
        {
            var body = await initResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("LinkedIn image initializeUpload failed: {Status} {Body}", initResponse.StatusCode, body);
            return null;
        }

        var initBody = await initResponse.Content.ReadAsStringAsync(ct);
        var value = JsonSerializer.Deserialize<ImageInitResponse>(initBody, JsonOptions)?.Value;
        if (value?.Image is null || string.IsNullOrEmpty(value.UploadUrl))
        {
            logger.LogError("LinkedIn image initializeUpload returned no upload URL: {Body}", initBody);
            return null;
        }

        // Single PUT of the whole image. The dms-uploads URL is LinkedIn-signed but still gates on
        // the bearer token (unlike video's pre-signed part URLs). Use a bare, generous-timeout client.
        using var uploadClient = httpClientFactory.CreateClient();
        uploadClient.Timeout = TimeSpan.FromMinutes(5);

        using var content = new ByteArrayContent(media.Data);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(media.ContentType) ? "application/octet-stream" : media.ContentType);
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, value.UploadUrl) { Content = content };
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var putResponse = await uploadClient.SendAsync(putRequest, ct);
        if (!putResponse.IsSuccessStatusCode)
        {
            logger.LogError("LinkedIn image upload failed: {Status}", putResponse.StatusCode);
            return null;
        }

        return await WaitForImageAvailableAsync(value.Image, token, ct) ? value.Image : null;
    }

    private async Task<bool> WaitForImageAvailableAsync(string imageUrn, string token, CancellationToken ct)
    {
        var encodedUrn = Uri.EscapeDataString(imageUrn);
        for (var attempt = 0; attempt < VideoStatusMaxPolls; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/images/{encodedUrn}");
            SetLinkedInHeaders(request, token);

            var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var parsed = JsonSerializer.Deserialize<ImageStatusResponse>(body, JsonOptions);
                // The status field has appeared both at the root and under `value`; accept either.
                var status = parsed?.Status ?? parsed?.Value?.Status;
                if (status == "AVAILABLE")
                    return true;
                if (status is not null && status.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError("LinkedIn image processing failed for {Urn}: {Body}", imageUrn, body);
                    return false;
                }
            }
            else
            {
                logger.LogWarning("LinkedIn image status check returned {Status} for {Urn}", response.StatusCode, imageUrn);
            }

            await Task.Delay(VideoStatusPollInterval, ct);
        }

        logger.LogError("LinkedIn image {Urn} did not reach AVAILABLE within the poll window", imageUrn);
        return false;
    }

    private async Task<bool> WaitForVideoAvailableAsync(string videoUrn, string token, CancellationToken ct)
    {
        var encodedUrn = Uri.EscapeDataString(videoUrn);
        for (var attempt = 0; attempt < VideoStatusMaxPolls; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/videos/{encodedUrn}");
            SetLinkedInHeaders(request, token);

            var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var status = JsonSerializer.Deserialize<VideoStatusResponse>(body, JsonOptions)?.Status;
                switch (status)
                {
                    case "AVAILABLE":
                        return true;
                    case "PROCESSING_FAILED":
                        logger.LogError("LinkedIn video processing failed for {Urn}: {Body}", videoUrn, body);
                        return false;
                }
            }
            else
            {
                logger.LogWarning("LinkedIn video status check returned {Status} for {Urn}", response.StatusCode, videoUrn);
            }

            await Task.Delay(VideoStatusPollInterval, ct);
        }

        logger.LogError("LinkedIn video {Urn} did not reach AVAILABLE within the poll window", videoUrn);
        return false;
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
                logger.LogWarning("LinkedIn token refresh failed: {Errors}", string.Join(", ", refreshResult.Errors));
                return null;
            }
            token = refreshResult.Value!;
        }

        return token;
    }

    private async Task<string?> GetPersonUrnAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("LinkedIn /v2/userinfo failed: {Status} {Body}", response.StatusCode, body);
            throw new HttpRequestException($"LinkedIn user lookup failed ({response.StatusCode})");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var userInfo = JsonSerializer.Deserialize<LinkedInUserInfo>(json, JsonOptions);
        return userInfo?.Sub is not null ? $"urn:li:person:{userInfo.Sub}" : null;
    }

    private static object BuildPostPayload(string personUrn, PlatformPublishRequest request, UploadedMedia? media)
    {
        var distribution = new
        {
            feedDistribution = "MAIN_FEED",
            targetEntities = Array.Empty<object>(),
            thirdPartyDistributionChannels = Array.Empty<object>()
        };

        // A native media attachment takes precedence over an article link — a post carries one
        // content object. Video describes itself with `title`; image with `altText`.
        if (media is not null)
        {
            var label = request.Media?.Title ?? request.Content.Title;
            object mediaObject = media.Kind == MediaKind.Video
                ? new { title = label, id = media.Urn }
                : new { altText = label, id = media.Urn };

            return new
            {
                author = personUrn,
                commentary = request.TransformedContent,
                visibility = "PUBLIC",
                distribution,
                lifecycleState = "PUBLISHED",
                isReshareDisabledByAuthor = false,
                content = new { media = mediaObject }
            };
        }

        if (!string.IsNullOrEmpty(request.CanonicalUrl))
        {
            var description = request.TransformedContent.Length > 200
                ? request.TransformedContent[..200]
                : request.TransformedContent;

            return new
            {
                author = personUrn,
                commentary = request.TransformedContent,
                visibility = "PUBLIC",
                distribution,
                lifecycleState = "PUBLISHED",
                content = new
                {
                    article = new
                    {
                        source = request.CanonicalUrl,
                        title = request.Content.Title,
                        description
                    }
                }
            };
        }

        return new
        {
            author = personUrn,
            commentary = request.TransformedContent,
            visibility = "PUBLIC",
            distribution,
            lifecycleState = "PUBLISHED"
        };
    }

    private static void SetLinkedInHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Restli-Protocol-Version", RestliProtocolVersion);
        request.Headers.Add("LinkedIn-Version", LinkedInVersion);
    }

    private async Task<PlatformCredential> GetActiveCredentialAsync(CancellationToken ct)
    {
        return await db.PlatformCredentials
            .FirstOrDefaultAsync(c => c.Platform == Platform.LinkedIn && c.IsActive, ct)
            ?? throw new InvalidOperationException("No active LinkedIn credential found");
    }

    private enum MediaKind { Video, Image }
    private sealed record UploadedMedia(string Urn, MediaKind Kind);

    internal record LinkedInUserInfo(string? Sub, string? Name, string? Email);
    internal record LinkedInErrorResponse(int Status, int ServiceErrorCode, string Code, string Message);

    internal sealed record VideoInitResponse(VideoInitValue? Value);
    internal sealed record VideoInitValue(
        string? Video,
        IReadOnlyList<VideoUploadInstruction>? UploadInstructions,
        string? UploadToken);
    internal sealed record VideoUploadInstruction(string UploadUrl, long FirstByte, long LastByte);
    internal sealed record VideoStatusResponse(string? Status);

    internal sealed record ImageInitResponse(ImageInitValue? Value);
    internal sealed record ImageInitValue(string? Image, string? UploadUrl);
    internal sealed record ImageStatusResponse(string? Status, ImageStatusValue? Value);
    internal sealed record ImageStatusValue(string? Status);
}
