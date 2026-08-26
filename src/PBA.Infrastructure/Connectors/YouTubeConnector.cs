using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Uploads a video to YouTube and lets YouTube release it.
///
/// This lane is the opposite shape to TikTok and Instagram. Those publish AT the moment; here the
/// video is handed over early as a private upload carrying a publish time, and YouTube flips it
/// public itself. Nothing of ours needs to be running at the release, which is the whole point.
///
/// Two things make it different in ways worth stating:
///
/// 1. YouTube takes BYTES — it will not fetch from a URL like Buffer and Meta do. When PBA has been
///    holding the clip, the bytes are on R2 and this connector pulls them back down before pushing.
/// 2. An upload costs 1,600 of a 10,000-unit daily quota shared with the analytics polling, so the
///    handover is paced elsewhere (see <see cref="PlatformCapabilities.RequiresPacedHandover"/>).
///    Nothing here rations anything; by the time this runs, the decision has been made.
///
/// There are no retries, for the same reason the Buffer lane has none: videos.insert is not
/// idempotent and has no dedup key, so a retried upload is a second video on the channel that
/// somebody has to notice and delete.
/// </summary>
public sealed class YouTubeConnector(
    HttpClient httpClient,
    IPlatformTokenProvider tokens,
    IOptionsMonitor<YouTubePublishingOptions> options,
    TimeProvider clock,
    ILogger<YouTubeConnector> logger) : IPlatformConnector
{
    private const int MaxTitleCharacters = 100;
    private const int MaxDescriptionCharacters = 5000;
    private const string WatchUrl = "https://youtu.be/";

    public Platform Platform => Platform.YouTube;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            var title = BuildTitle(request);
            if (string.IsNullOrWhiteSpace(title))
                return Fail("A YouTube upload needs a title.");

            if (request.Media is null && request.HostedMediaUrl is null)
                return Fail("YouTube publishing requires a video (mp4/mov). No video was provided.");

            if (request.Media is { IsVideo: false })
                return Fail("YouTube publishing requires a video (mp4/mov). The attachment is not a video.");

            // A publish time already gone by is not something YouTube accepts: it rejects a publishAt
            // in the past outright. Say so here rather than letting the upload burn quota first.
            if (request.ScheduledAt is { } at && at <= clock.GetUtcNow())
                return Fail($"The requested publish time ({at:u}) has already passed. " +
                            "YouTube only accepts a future publish time on a scheduled upload.");

            var token = await tokens.GetFreshAccessTokenAsync(
                Platform.YouTube, CredentialPurpose.Publishing, ct);
            if (!token.IsSuccess)
                return Fail(token.Errors.FirstOrDefault() ?? "No usable YouTube publishing credential.");

            await using var video = await OpenVideoAsync(request, ct);
            if (video is null)
                return Fail("The staged clip could not be read back from storage, so there was " +
                            "nothing to upload. It may have been reaped before its upload slot.");

            using var service = CreateService(token.Value!);

            // Check WHERE this is about to go before spending 1,600 quota units finding out. An
            // upload to the wrong channel cannot be undone from here and leaves no visible trace on
            // the channel anyone is watching. Costs one quota unit, and runs after the free local
            // checks so a missing clip does not surface as a Google error.
            var wrongChannel = await CheckChannelAsync(service, ct);
            if (wrongChannel is not null)
                return Fail(wrongChannel);

            return await UploadAsync(service, request, title, video, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to YouTube");
            return Fail("An unexpected error occurred while uploading to YouTube. Check logs for details.");
        }
    }

    /// <summary>
    /// Confirms a publishing credential exists and Google still accepts it, by reading the channel
    /// it belongs to. A token that is merely present tells us nothing — the analytics credential for
    /// this same platform is a valid token that cannot upload, and only an upload would find out.
    /// </summary>
    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var token = await tokens.GetFreshAccessTokenAsync(
                Platform.YouTube, CredentialPurpose.Publishing, ct);
            if (!token.IsSuccess)
                return false;

            using var service = CreateService(token.Value!);
            return await CheckChannelAsync(service, ct) is null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTube credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: MaxDescriptionCharacters,
        SupportsMarkdown: false,
        SupportsHtml: false,
        SupportsImages: false,
        // YouTube holds the video itself: uploaded private with a publish time, it goes public on
        // its own. PBA does not need to be running at the release.
        SupportsScheduling: true,
        SupportsThreads: false,
        SupportedMediaTypes: ["video/mp4", "video/quicktime"],
        // ...but accepting the video costs a rationed daily quota, so the handover is paced.
        RequiresPacedHandover: true
    );

    /// <summary>
    /// Returns null when the credential owns the channel we intend to publish to, otherwise the
    /// reason it does not.
    ///
    /// This exists because a credential for the WRONG channel is completely valid. A Google account
    /// can own several channels, OAuth consent grants whichever was picked in Google's chooser, and
    /// an upload with the wrong one succeeds and returns a real video id — the video is just not on
    /// the channel anyone is looking at. The first test upload here did exactly that, onto a second
    /// channel carrying the same display name and a different handle.
    /// </summary>
    private async Task<string?> CheckChannelAsync(YouTubeService service, CancellationToken ct)
    {
        var listRequest = service.Channels.List("id,snippet");
        listRequest.Mine = true;
        var response = await listRequest.ExecuteAsync(ct);

        if (response.Items is not { Count: > 0 })
            return "The YouTube credential does not own a channel.";

        var channel = response.Items[0];
        var mismatch = ChannelMismatch(
            options.CurrentValue.ChannelId, channel.Id, channel.Snippet?.Title, channel.Snippet?.CustomUrl);
        if (mismatch is not null)
        {
            // Logged, not just returned. The readiness probe reduces this to a bool, so without a
            // log line the refusal reaches a caller as "validation failed" with no way to learn WHY
            // — which is the same nothing-to-go-on that made the wrong-channel upload hard to find.
            logger.LogWarning("YouTube publishing credential rejected: {Reason}", mismatch);
            return mismatch;
        }

        logger.LogInformation(
            "YouTube publishing credential belongs to channel {ChannelTitle} ({ChannelId}), handle {Handle}",
            channel.Snippet?.Title, channel.Id, channel.Snippet?.CustomUrl ?? "(none)");
        return null;
    }

    /// <summary>
    /// The rule on its own: null when the credential's channel is the intended one (or none was
    /// configured), otherwise the reason it is not. Separated from the API call so the decision that
    /// actually matters can be tested without a live Google client.
    /// </summary>
    internal static string? ChannelMismatch(string? expected, string actualId, string? title, string? handle)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.Equals(expected, actualId, StringComparison.Ordinal))
            return null;

        return $"The YouTube credential is for channel {title ?? "(untitled)"} " +
               $"({actualId}, {handle ?? "no handle"}), not the configured {expected}. Re-consent and " +
               "pick the right channel in Google's chooser — an upload with this credential would go " +
               "somewhere nobody is watching.";
    }

    private YouTubeService CreateService(string accessToken) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
        ApplicationName = "Personal Brand Assistant"
    });

    private async Task<Stream?> OpenVideoAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        // The bytes came with the request: publish is happening as the clip arrived.
        if (request.Media is { } media)
            return new MemoryStream(media.Data, writable: false);

        // PBA held this one. YouTube will not fetch a URL, so pull the staged object back down.
        using var response = await httpClient.GetAsync(
            request.HostedMediaUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Could not fetch staged clip {Url}: HTTP {Status}",
                request.HostedMediaUrl, (int)response.StatusCode);
            return null;
        }

        // Buffered deliberately: the upload is resumable and rewinds the stream on a retry of a
        // failed chunk, which a network stream cannot do.
        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }

    private async Task<PlatformPublishResult> UploadAsync(
        YouTubeService service, PlatformPublishRequest request, string title, Stream video, CancellationToken ct)
    {
        var config = options.CurrentValue;
        var scheduled = request.ScheduledAt;

        var payload = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = title,
                Description = Truncate(request.TransformedContent, MaxDescriptionCharacters),
                Tags = request.Tags.Count > 0 ? request.Tags.ToList() : null,
                CategoryId = config.CategoryId
            },
            Status = new VideoStatus
            {
                // Scheduling on YouTube IS "private plus a publish time" — there is no other form of
                // it, and a public video cannot carry one. With no schedule the caller asked to
                // publish now, so it goes public now.
                PrivacyStatus = scheduled is null ? "public" : "private",
                PublishAtDateTimeOffset = scheduled?.ToUniversalTime(),
                SelfDeclaredMadeForKids = config.MadeForKids
            }
        };

        var insert = service.Videos.Insert(payload, "snippet,status", video, "video/*");
        insert.ChunkSize = ResumableUpload.MinimumChunkSize * 4;

        var progress = await insert.UploadAsync(ct);
        if (progress.Status != UploadStatus.Completed)
        {
            var reason = progress.Exception?.Message ?? "no reason reported by the API";
            logger.LogError(progress.Exception, "YouTube upload did not complete: {Reason}", reason);
            return Fail($"YouTube upload did not complete: {reason}");
        }

        var videoId = insert.ResponseBody?.Id;
        if (string.IsNullOrWhiteSpace(videoId))
            return Fail("YouTube accepted the upload but returned no video id.");

        logger.LogInformation("Uploaded YouTube video {VideoId}, publishing at {PublishAt}",
            videoId, scheduled?.ToString("u") ?? "immediately");

        return new PlatformPublishResult(true, WatchUrl + videoId, videoId, null);
    }

    /// <summary>
    /// The video's own title, which on YouTube is a headline the viewer reads — not the description.
    /// Truncated rather than rejected at 100 characters because that is YouTube's hard limit and a
    /// title one word over should not fail an upload that has already cost quota to attempt.
    /// </summary>
    private static string BuildTitle(PlatformPublishRequest request) =>
        Truncate(request.Content.Title.Trim(), MaxTitleCharacters);

    private static string Truncate(string value, int max) =>
        value.Length > max ? value[..max].TrimEnd() : value;

    private static PlatformPublishResult Fail(string reason) => new(false, null, null, reason);
}
