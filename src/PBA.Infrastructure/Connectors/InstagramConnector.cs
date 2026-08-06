using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Media;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Publishes a video to Instagram as a Reel through Meta's Content Publishing API.
///
/// Meta never accepts raw bytes here — it FETCHES the video from a URL you give it — so the clip is
/// hosted on R2 first (<see cref="IMediaHost"/>) and the public URL is handed to the container.
/// That is the same reason the Buffer/TikTok lane stages to R2, and it is why both can share a host.
///
/// The flow is necessarily four calls, not one: create a REELS container, poll it until Meta has
/// finished transcoding, publish it, then unstage. Meta's processing is asynchronous and publishing
/// a container before it reaches FINISHED fails.
///
/// This connector reports SupportsScheduling: false, and that is a fact about Meta rather than an
/// unimplemented feature — the Content Publishing API has no scheduling concept at all. Anything
/// that wants a Reel to appear later must hold it and call this at the moment of posting.
/// </summary>
public sealed class InstagramConnector(
    HttpClient httpClient,
    IMediaHost mediaHost,
    IPlatformTokenProvider tokens,
    IOptionsMonitor<InstagramPublishingOptions> options,
    ILogger<InstagramConnector> logger) : IPlatformConnector
{
    private const int MaxCaptionCharacters = 2200;
    private const int FallbackCoverOffsetMs = 2000;

    public Platform Platform => Platform.Instagram;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        HostedMedia? hosted = null;
        try
        {
            if (request.Media is not { } media || !media.IsVideo)
                return Fail("Instagram publishing via this lane requires a video attachment (mp4/mov).");

            if (request.TransformedContent.Length > MaxCaptionCharacters)
                return Fail($"Caption is {request.TransformedContent.Length} characters, " +
                            $"over Instagram's {MaxCaptionCharacters} limit.");

            // Said explicitly rather than ignored silently: a caller that set a slot and got an
            // immediate post would have no way to tell from a success result that the timing was
            // dropped, and would find out from the account.
            if (request.ScheduledAt is not null)
                return Fail("Instagram cannot schedule a post: Meta's Content Publishing API has no " +
                            "scheduling concept. Publish at the intended moment instead.");

            var token = await tokens.GetFreshAccessTokenAsync(
                Platform.Instagram, CredentialPurpose.Publishing, ct);
            if (!token.IsSuccess)
                return Fail(token.Errors.FirstOrDefault()
                            ?? "No usable Instagram publishing credential.");

            var config = options.CurrentValue;
            hosted = await mediaHost.UploadAsync(media.Data, media.FileName, media.ContentType, ct);

            var containerId = await CreateReelContainerAsync(
                config, token.Value!, hosted.Url, request.TransformedContent, media.Data, ct);
            if (containerId.Error is not null)
                return Fail(containerId.Error);

            var ready = await WaitForContainerAsync(config, token.Value!, containerId.Value!, ct);
            if (ready is not null)
                return Fail(ready);

            var mediaId = await PublishContainerAsync(config, token.Value!, containerId.Value!, ct);
            if (mediaId.Error is not null)
                return Fail(mediaId.Error);

            logger.LogInformation("Published Instagram Reel {MediaId}", mediaId.Value);
            return new PlatformPublishResult(true, null, mediaId.Value, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to Instagram");
            return Fail("An unexpected error occurred while publishing to Instagram. Check logs for details.");
        }
        finally
        {
            // Meta has copied the video by now. The staged object is dead weight, but failing to
            // remove it must never turn a published Reel into a reported failure — the bucket has a
            // lifecycle rule that reaps whatever is left behind.
            if (hosted is not null)
            {
                try
                {
                    await mediaHost.DeleteAsync(hosted.Key, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not unstage {Key}; the lifecycle rule will reap it", hosted.Key);
                }
            }
        }
    }

    /// <summary>
    /// Confirms a publishing credential exists and Meta still accepts it, by reading the configured
    /// account. Deliberately more than a token-present check: an expired or wrongly-scoped token
    /// looks identical in the database and only fails at publish time.
    /// </summary>
    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var token = await tokens.GetFreshAccessTokenAsync(
                Platform.Instagram, CredentialPurpose.Publishing, ct);
            if (!token.IsSuccess)
                return false;

            var config = options.CurrentValue;
            using var response = await httpClient.GetAsync(
                $"{config.GraphBase}/{config.IgUserId}?fields=id,username&access_token={token.Value}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Instagram credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: MaxCaptionCharacters,
        SupportsMarkdown: false,
        SupportsHtml: false,
        SupportsImages: false,
        // Not a gap in this connector: Meta's Content Publishing API cannot schedule.
        SupportsScheduling: false,
        SupportsThreads: false,
        SupportedMediaTypes: ["video/mp4", "video/quicktime"]
    );

    private async Task<(string? Value, string? Error)> CreateReelContainerAsync(
        InstagramPublishingOptions config, string token, string videoUrl, string caption,
        byte[] videoData, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["media_type"] = "REELS",
            ["video_url"] = videoUrl,
            ["caption"] = caption,
            ["share_to_feed"] = config.ShareToFeed ? "true" : "false",
            ["thumb_offset"] = ResolveCoverOffsetMs(config, videoData).ToString(),
            ["access_token"] = token
        };

        var (json, error) = await SendAsync(
            HttpMethod.Post, $"{config.GraphBase}/{config.IgUserId}/media", form, ct);
        if (error is not null)
            return (null, error);

        return json!.Value.TryGetProperty("id", out var id) && id.GetString() is { } containerId
            ? (containerId, null)
            : (null, "Instagram accepted the upload but returned no container id.");
    }

    /// <summary>Polls until Meta finishes transcoding. Returns null on success, else the reason.</summary>
    private async Task<string?> WaitForContainerAsync(
        InstagramPublishingOptions config, string token, string containerId, CancellationToken ct)
    {
        var lastStatus = "IN_PROGRESS";

        for (var attempt = 0; attempt < config.ContainerPollMaxAttempts; attempt++)
        {
            var (json, error) = await SendAsync(
                HttpMethod.Get,
                $"{config.GraphBase}/{containerId}?fields=status_code,status&access_token={token}",
                null, ct);
            if (error is not null)
                return error;

            lastStatus = json!.Value.TryGetProperty("status_code", out var code)
                ? code.GetString() ?? ""
                : "";

            if (lastStatus == "FINISHED")
                return null;

            if (lastStatus == "ERROR")
            {
                var detail = json.Value.TryGetProperty("status", out var s) ? s.GetString() : null;
                return $"Instagram rejected the video while processing it: {detail ?? "no detail given"}";
            }

            await Task.Delay(TimeSpan.FromSeconds(config.ContainerPollSeconds), ct);
        }

        var waited = config.ContainerPollMaxAttempts * config.ContainerPollSeconds;
        return $"Instagram was still processing the video after {waited}s (last status {lastStatus}). " +
               "Nothing was published; the clip can be retried.";
    }

    private async Task<(string? Value, string? Error)> PublishContainerAsync(
        InstagramPublishingOptions config, string token, string containerId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["creation_id"] = containerId,
            ["access_token"] = token
        };

        var (json, error) = await SendAsync(
            HttpMethod.Post, $"{config.GraphBase}/{config.IgUserId}/media_publish", form, ct);
        if (error is not null)
            return (null, error);

        return json!.Value.TryGetProperty("id", out var id) && id.GetString() is { } mediaId
            ? (mediaId, null)
            : (null, "Instagram published the Reel but returned no media id.");
    }

    /// <summary>
    /// One request. Meta puts the useful part of a failure in a JSON <c>error.message</c>, so that is
    /// surfaced verbatim — a bare status code turns a fixable permissions or media problem into a
    /// guessing game, which is how the previous lane stayed broken without anyone knowing why.
    /// </summary>
    private async Task<(JsonElement? Json, string? Error)> SendAsync(
        HttpMethod method, string url, Dictionary<string, string>? form, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, url);
        if (form is not null)
            message.Content = new FormUrlEncodedContent(form);

        using var response = await httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
            return response.IsSuccessStatusCode
                ? (null, "Instagram returned an empty response.")
                : (null, $"Instagram returned {(int)response.StatusCode} with no body.");

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return (null, $"Instagram returned {(int)response.StatusCode} with an unreadable body.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = root.TryGetProperty("error", out var err) &&
                         err.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : null;
            logger.LogError("Instagram {Status} for {Url}: {Body}", (int)response.StatusCode, url, body);
            return (null, $"Instagram returned {(int)response.StatusCode}: {detail ?? "no reason given"}");
        }

        return (root, null);
    }

    /// <summary>
    /// Picks the Reel cover offset (ms). Instagram would otherwise use frame 0, which on these clips
    /// is the black title card — every cover would be a black square.
    /// </summary>
    private static int ResolveCoverOffsetMs(InstagramPublishingOptions config, byte[] videoData)
    {
        if (Mp4Probe.TryGetDurationMs(videoData, out var durationMs) && durationMs > 0)
        {
            var fraction = Math.Clamp(config.CoverFrameFraction, 0.0, 0.95);
            return (int)(durationMs * fraction);
        }

        return FallbackCoverOffsetMs;
    }

    private static PlatformPublishResult Fail(string reason) => new(false, null, null, reason);
}
