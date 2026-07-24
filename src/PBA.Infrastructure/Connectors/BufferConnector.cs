using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Publishes a video to TikTok through Buffer's GraphQL API. Buffer is the transport; TikTok is the
/// destination (<see cref="Platform.TikTok"/>). Buffer only fetches media from a public URL — never
/// raw bytes — so the video is hosted on R2 first (<see cref="IMediaHost"/>) and its public URL is
/// handed to the createPost mutation.
/// </summary>
public sealed class BufferConnector(
    HttpClient httpClient,
    IMediaHost mediaHost,
    IOptionsMonitor<BufferOptions> options,
    ILogger<BufferConnector> logger) : IPlatformConnector
{
    // Buffer fetches the media asynchronously (at publish time for a scheduled post), so the hosted
    // object must still exist then. The R2 bucket reaps objects on a lifecycle rule (~8 days); cap
    // scheduling below that so a post can never reference an already-expired object.
    private static readonly TimeSpan MaxScheduleWindow = TimeSpan.FromDays(7);
    private const int ThumbnailOffsetMs = 0; // first frame

    private const string AccountQuery = "{ account { organizations { id name } } }";
    private const string CreatePostMutation =
        "mutation CreatePost($input: CreatePostInput!) { createPost(input: $input) { __typename ... on PostActionSuccess { post { id } } ... on MutationError { message } } }";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public Platform Platform => Platform.TikTok;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            // Buffer → TikTok is video-only in this lane.
            if (request.Media is not { } media || !media.IsVideo)
                return new PlatformPublishResult(false, null, null,
                    "TikTok via Buffer requires a video attachment (mp4/mov). No supported video was provided.");

            // The hosted object is reaped by an R2 lifecycle rule; a schedule beyond that window
            // would post a URL to an object that no longer exists when Buffer fetches it. Reject
            // rather than publish a broken link.
            if (request.ScheduledAt is { } scheduledAt &&
                scheduledAt - DateTimeOffset.UtcNow > MaxScheduleWindow)
                return new PlatformPublishResult(false, null, null,
                    "TikTok via Buffer cannot schedule more than 7 days out: the hosted media object " +
                    "would be reaped before Buffer fetches the video. Schedule within 7 days.");

            var hosted = await mediaHost.UploadAsync(media.Data, media.FileName, media.ContentType, ct);

            var organizationId = await ResolveOrganizationIdAsync(ct);
            if (organizationId is null)
                return new PlatformPublishResult(false, null, null,
                    "Buffer organization lookup failed. Verify the Buffer API key.");

            var channelId = await ResolveTikTokChannelIdAsync(organizationId, ct);
            if (channelId is null)
                return new PlatformPublishResult(false, null, null,
                    "No TikTok channel is connected in Buffer for this organization.");

            return await CreatePostAsync(request, channelId, hosted.Url, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to TikTok via Buffer");
            return new PlatformPublishResult(false, null, null,
                "An unexpected error occurred while publishing to TikTok via Buffer. Check logs for details.");
        }
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var data = await SendGraphQlAsync(AccountQuery, null, ct);
            return data is { } d && TryGetFirstOrganizationId(d, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Buffer credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: 2200,
        SupportsMarkdown: false,
        SupportsHtml: false,
        SupportsImages: false,
        SupportsScheduling: true,
        SupportsThreads: false,
        SupportedMediaTypes: ["video/mp4", "video/quicktime"]
    );

    private async Task<string?> ResolveOrganizationIdAsync(CancellationToken ct)
    {
        var data = await SendGraphQlAsync(AccountQuery, null, ct);
        if (data is { } d && TryGetFirstOrganizationId(d, out var id))
            return id;
        return null;
    }

    private async Task<string?> ResolveTikTokChannelIdAsync(string organizationId, CancellationToken ct)
    {
        // organizationId is a Buffer-issued opaque id, and Buffer types this argument as a custom
        // scalar — a String!-typed GraphQL variable fails schema validation ("no channel found").
        // Inline it as an escaped GraphQL string literal (JSON string syntax is valid here), matching
        // Buffer's own docs, which only ever show the id inlined.
        var query =
            "{ channels(input: { organizationId: " +
            JsonSerializer.Serialize(organizationId) +
            " }) { id name service } }";
        var data = await SendGraphQlAsync(query, null, ct);
        if (data is not { } d || !d.TryGetProperty("channels", out var channels) ||
            channels.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var channel in channels.EnumerateArray())
        {
            if (channel.TryGetProperty("service", out var service) &&
                string.Equals(service.GetString(), "tiktok", StringComparison.OrdinalIgnoreCase) &&
                channel.TryGetProperty("id", out var id))
                return id.GetString();
        }

        return null;
    }

    private async Task<PlatformPublishResult> CreatePostAsync(
        PlatformPublishRequest request, string channelId, string mediaUrl, CancellationToken ct)
    {
        var scheduled = request.ScheduledAt is not null;

        var videoAsset = new
        {
            video = new
            {
                url = mediaUrl,
                metadata = new { thumbnailOffset = ThumbnailOffsetMs }
            }
        };

        var input = new Dictionary<string, object?>
        {
            ["text"] = request.TransformedContent,
            ["channelId"] = channelId,
            ["schedulingType"] = "automatic",
            ["mode"] = scheduled ? "customScheduled" : "shareNow",
            ["assets"] = new[] { videoAsset }
        };

        if (scheduled)
            input["dueAt"] = request.ScheduledAt!.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        // AI-content disclosure: Buffer's TikTok metadata carries an AI-generated flag.
        // Verified against https://developers.buffer.com/llms-full.txt —
        // TiktokPostMetadataInput.isAiGenerated (Boolean, "Whether the post discloses AI-generated
        // content (TikTok video only)"), set via the channel-specific `metadata.tiktok` input.
        if (options.CurrentValue.DiscloseAiGenerated)
            input["metadata"] = new { tiktok = new { isAiGenerated = true } };

        var data = await SendGraphQlAsync(CreatePostMutation, new { input }, ct);
        if (data is not { } d || !d.TryGetProperty("createPost", out var createPost))
            return new PlatformPublishResult(false, null, null, "Buffer createPost returned no data.");

        var typeName = createPost.TryGetProperty("__typename", out var tn) ? tn.GetString() : null;

        if (typeName == "PostActionSuccess" &&
            createPost.TryGetProperty("post", out var post) &&
            post.TryGetProperty("id", out var postId))
        {
            // Buffer does not return a public TikTok URL at create time; only the Buffer post id.
            return new PlatformPublishResult(true, null, postId.GetString(), null);
        }

        var message = createPost.TryGetProperty("message", out var msg)
            ? msg.GetString()
            : "Buffer createPost failed.";
        logger.LogError("Buffer createPost error: {Message}", message);
        return new PlatformPublishResult(false, null, null, message);
    }

    /// <summary>
    /// POSTs a GraphQL operation to the Buffer root endpoint and returns the cloned <c>data</c>
    /// element, or null on transport error / a top-level <c>errors</c> array. Buffer returns
    /// operation errors as typed unions inside <c>data</c>, so a 200 with data is the norm.
    /// </summary>
    private async Task<JsonElement?> SendGraphQlAsync(string query, object? variables, CancellationToken ct)
    {
        var apiKey = options.CurrentValue.ApiKey;
        var body = JsonSerializer.Serialize(new { query, variables }, JsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await httpClient.SendAsync(httpRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Buffer GraphQL HTTP {Status}: {Body}", response.StatusCode, responseBody);
            return null;
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            logger.LogError("Buffer GraphQL errors: {Body}", responseBody);
            return null;
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return null;

        return data.Clone();
    }

    private static bool TryGetFirstOrganizationId(JsonElement data, out string id)
    {
        id = string.Empty;
        if (data.TryGetProperty("account", out var account) &&
            account.TryGetProperty("organizations", out var orgs) &&
            orgs.ValueKind == JsonValueKind.Array &&
            orgs.GetArrayLength() > 0 &&
            orgs[0].TryGetProperty("id", out var orgId) &&
            orgId.GetString() is { } value)
        {
            id = value;
            return true;
        }

        return false;
    }
}
