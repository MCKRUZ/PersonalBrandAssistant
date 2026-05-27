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
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed class MediumConnector(
    HttpClient httpClient,
    IAppDbContext db,
    ITokenEncryptor encryptor,
    IOptionsMonitor<MediumOptions> options,
    ILogger<MediumConnector> logger) : IPlatformConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Platform Platform => Platform.Medium;

    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
    {
        try
        {
            var token = await GetDecryptedTokenAsync(ct);

            var userId = await GetUserIdAsync(token, ct);
            if (userId is null)
                return new PlatformPublishResult(false, null, null,
                    "Medium integration token is invalid or expired. Please reconfigure in Settings.");

            var publishStatus = request.Mode switch
            {
                PublishMode.Publish => "public",
                PublishMode.Schedule => options.CurrentValue.DefaultPublishStatus,
                _ => options.CurrentValue.DefaultPublishStatus
            };

            var tags = request.Tags
                .Take(3)
                .Select(t => t.Length > 25 ? t[..25] : t)
                .ToList();

            var payload = new
            {
                title = request.Content.Title,
                contentFormat = "markdown",
                content = request.TransformedContent,
                tags,
                canonicalUrl = request.CanonicalUrl,
                publishStatus
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/users/{userId}/posts")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(postRequest, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new PlatformPublishResult(false, null, null, "Medium rate limit exceeded. Retry scheduled.");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Medium publish failed: {Status} {Body}", response.StatusCode, errorBody);
                return new PlatformPublishResult(false, null, null, $"Medium publish failed ({response.StatusCode})");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var postData = JsonSerializer.Deserialize<MediumResponse<MediumPost>>(responseJson, JsonOptions);

            return new PlatformPublishResult(
                true,
                postData?.Data?.Url,
                postData?.Data?.Id,
                null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish to Medium");
            return new PlatformPublishResult(false, null, null,
                "An unexpected error occurred while publishing to Medium. Check logs for details.");
        }
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
    {
        try
        {
            var token = await GetDecryptedTokenAsync(ct);
            return await GetUserIdAsync(token, ct) is not null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Medium credential validation failed");
            return false;
        }
    }

    public PlatformCapabilities GetCapabilities() => new(
        MaxCharacters: int.MaxValue,
        SupportsMarkdown: true,
        SupportsHtml: true,
        SupportsImages: true,
        SupportsScheduling: false,
        SupportsThreads: false,
        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif"]
    );

    private async Task<string?> GetUserIdAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Medium /v1/me failed: {Status} {Body}", response.StatusCode, body);
            throw new HttpRequestException($"Medium user lookup failed ({response.StatusCode})");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var userData = JsonSerializer.Deserialize<MediumResponse<MediumUser>>(json, JsonOptions);
        return userData?.Data?.Id;
    }

    private async Task<string> GetDecryptedTokenAsync(CancellationToken ct)
    {
        var credential = await db.PlatformCredentials
            .FirstOrDefaultAsync(c => c.Platform == Platform.Medium && c.IsActive, ct)
            ?? throw new InvalidOperationException("No active Medium credential found");

        return encryptor.Decrypt(credential.EncryptedIntegrationToken
            ?? throw new InvalidOperationException("Medium credential has no integration token"));
    }

    internal record MediumResponse<T>(T? Data);
    internal record MediumUser(string Id, string? Username, string? Name, string? Url);
    internal record MediumPost(string Id, string? Title, string? Url, string? CanonicalUrl, string? PublishStatus);
}
