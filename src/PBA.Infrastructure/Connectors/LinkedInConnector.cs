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
    ILogger<LinkedInConnector> logger) : IPlatformConnector
{
    private readonly IOptionsMonitor<LinkedInOptions> _options = options;
    private const string LinkedInVersion = "202604";
    private const string RestliProtocolVersion = "2.0.0";
    private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(5);

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

            var payload = BuildPostPayload(personUrn, request);

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
        SupportsImages: false,
        SupportsScheduling: false,
        SupportsThreads: false,
        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif"]
    );

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

    private static object BuildPostPayload(string personUrn, PlatformPublishRequest request)
    {
        var distribution = new
        {
            feedDistribution = "MAIN_FEED",
            targetEntities = Array.Empty<object>(),
            thirdPartyDistributionChannels = Array.Empty<object>()
        };

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

    internal record LinkedInUserInfo(string? Sub, string? Name, string? Email);
    internal record LinkedInErrorResponse(int Status, int ServiceErrorCode, string Code, string Message);
}
