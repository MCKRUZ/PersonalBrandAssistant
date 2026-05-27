using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed class SubstackConnector(
    HttpClient httpClient,
    IAppDbContext db,
    ITokenEncryptor encryptor,
    IOptionsMonitor<SubstackOptions> options,
    ILogger<SubstackConnector> logger) : IPlatformConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Platform Platform => Platform.Substack;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            if (!options.CurrentValue.Enabled)
                return new PlatformPublishResult(false, null, null,
                    "Substack publishing is disabled. Enable it in configuration.");

            var credential = await GetActiveCredentialAsync(ct);
            var cookies = GetCookies(credential);
            if (cookies is null)
                return new PlatformPublishResult(false, null, null,
                    "Substack session cookies not found. Please log in via Settings.");

            var bylineId = await GetBylineIdAsync(cookies, ct);
            if (bylineId is null)
                return new PlatformPublishResult(false, null, null,
                    "Substack session expired. Please re-login in Settings.");

            if (await CreateDraftAsync(request, bylineId.Value, cookies, ct) is not { } draftId)
                return new PlatformPublishResult(false, null, null,
                    "Failed to create Substack draft.");

            if (request.Tags.Count > 0)
                await AddTagsAsync(draftId, request.Tags, cookies, ct);

            if (request.Mode is PublishMode.Draft or PublishMode.Schedule)
                return new PlatformPublishResult(true, null, draftId, null);

            var publishedUrl = await PublishDraftAsync(draftId, cookies, ct);
            if (publishedUrl is null)
                return new PlatformPublishResult(false, null, draftId,
                    "Draft created but publish failed. Check Substack dashboard.");

            return new PlatformPublishResult(true, publishedUrl, draftId, null);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to publish to Substack");
            return new PlatformPublishResult(false, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to Substack");
            return new PlatformPublishResult(false, null, null,
                "An unexpected error occurred while publishing to Substack.");
        }
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var credential = await GetActiveCredentialAsync(ct);
            var cookies = GetCookies(credential);
            if (cookies is null) return false;

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            AttachCookies(request, cookies);

            var response = await httpClient.SendAsync(request, ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Substack credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: int.MaxValue,
        SupportsMarkdown: false,
        SupportsHtml: false,
        SupportsImages: true,
        SupportsScheduling: false,
        SupportsThreads: false,
        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "image/webp"]
    );

    private async Task<int?> GetBylineIdAsync(
        Dictionary<string, string> cookies, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        AttachCookies(request, cookies);

        var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Substack GET /api/v1/me ({StatusCode}, {Length} bytes)",
            response.StatusCode, body.Length);
        logger.LogTrace("Substack GET /api/v1/me body: {Body}", body);

        if (!response.IsSuccessStatusCode)
            return null;

        var user = JsonSerializer.Deserialize<SubstackUser>(body, JsonOptions);
        return user?.BylineId;
    }

    private async Task<string?> CreateDraftAsync(
        PlatformPublishRequest publishRequest, int bylineId,
        Dictionary<string, string> cookies, CancellationToken ct)
    {
        var tiptapBody = JsonNode.Parse(publishRequest.TransformedContent);

        var payload = new JsonObject
        {
            ["draft_title"] = publishRequest.Content.Title,
            ["draft_subtitle"] = "",
            ["draft_body"] = tiptapBody,
            ["draft_bylines"] = new JsonArray { new JsonObject { ["id"] = bylineId } },
            ["type"] = "newsletter"
        };

        var json = payload.ToJsonString();
        logger.LogDebug("Substack POST /api/v1/drafts ({Length} bytes)", json.Length);
        logger.LogTrace("Substack POST /api/v1/drafts request: {Payload}", json);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/drafts")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        AttachCookies(request, cookies);

        var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Substack POST /api/v1/drafts ({StatusCode})", response.StatusCode);
        logger.LogTrace("Substack POST /api/v1/drafts response: {Body}", body);

        if (!response.IsSuccessStatusCode)
            return null;

        var draft = JsonSerializer.Deserialize<SubstackDraftResponse>(body, JsonOptions);
        return draft?.Id.ToString();
    }

    private async Task AddTagsAsync(
        string draftId, IReadOnlyList<string> tags,
        Dictionary<string, string> cookies, CancellationToken ct)
    {
        var tagsArray = new JsonArray();
        foreach (var tag in tags)
            tagsArray.Add(JsonValue.Create(tag));

        var payload = new JsonObject { ["tags"] = tagsArray }.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/post/{draftId}/tags")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        AttachCookies(request, cookies);

        var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Substack PUT tags for {DraftId} ({StatusCode})",
            draftId, response.StatusCode);
    }

    private async Task<string?> PublishDraftAsync(
        string draftId, Dictionary<string, string> cookies, CancellationToken ct)
    {
        using var prepubRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/drafts/{draftId}/prepublish");
        AttachCookies(prepubRequest, cookies);

        var prepubResponse = await httpClient.SendAsync(prepubRequest, ct);
        var prepubBody = await prepubResponse.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Substack POST prepublish ({StatusCode})", prepubResponse.StatusCode);

        if (!prepubResponse.IsSuccessStatusCode)
            return null;

        var audience = options.CurrentValue.DefaultAudience;
        var publishPayload = new JsonObject
        {
            ["send_email"] = options.CurrentValue.SendEmailOnPublish,
            ["audience"] = audience
        }.ToJsonString();

        using var publishRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/drafts/{draftId}/publish")
        {
            Content = new StringContent(publishPayload, Encoding.UTF8, "application/json")
        };
        AttachCookies(publishRequest, cookies);

        var publishResponse = await httpClient.SendAsync(publishRequest, ct);
        var publishBody = await publishResponse.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Substack POST publish ({StatusCode})", publishResponse.StatusCode);

        if (!publishResponse.IsSuccessStatusCode)
            return null;

        var result = JsonSerializer.Deserialize<SubstackPublishResponse>(publishBody, JsonOptions);
        return result?.CanonicalUrl;
    }

    private Dictionary<string, string>? GetCookies(PlatformCredential credential)
    {
        if (string.IsNullOrEmpty(credential.EncryptedCookies)) return null;

        try
        {
            var json = encryptor.Decrypt(credential.EncryptedCookies);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decrypt Substack cookies");
            return null;
        }
    }

    private static void AttachCookies(
        HttpRequestMessage request, Dictionary<string, string> cookies)
    {
        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
        request.Headers.Add("Cookie", cookieHeader);
    }

    private async Task<PlatformCredential> GetActiveCredentialAsync(CancellationToken ct)
    {
        return await db.PlatformCredentials
            .FirstOrDefaultAsync(c => c.Platform == Platform.Substack && c.IsActive, ct)
            ?? throw new InvalidOperationException(
                "No active Substack credential found. Connect Substack in Settings.");
    }

    internal record SubstackUser(int Id, string? Name, string? Email, int BylineId);
    internal record SubstackDraftResponse(int Id, string? Slug, string? Title);
    internal record SubstackPublishResponse(int Id, string? Slug, string? CanonicalUrl);
}
