using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class PlatformEndpoints
{
    private const int MaxPostIdLength = 256;

    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platforms").WithTags("Platforms");

        group.MapGet("/", ListPlatforms);
        group.MapGet("/{type}/auth-url", GetAuthUrl);
        group.MapPost("/{type}/callback", HandleCallback);
        group.MapDelete("/{type}/disconnect", Disconnect);
        group.MapGet("/{type}/status", GetStatus);
        group.MapPost("/{type}/test-post", TestPost);
        group.MapGet("/{type}/engagement/{postId}", GetEngagement);
    }

    private static async Task<IResult> ListPlatforms(IApplicationDbContext db, CancellationToken ct)
    {
        var platforms = await db.Platforms
            .Select(p => new
            {
                p.Type,
                p.IsConnected,
                p.DisplayName,
                p.LastSyncAt,
            })
            .ToListAsync(ct);

        return Results.Ok(platforms);
    }

    private static async Task<IResult> GetAuthUrl(
        string type, IOAuthManager oauthManager, CancellationToken ct)
    {
        if (!TryParsePlatform(type, out var platformType))
            return InvalidPlatformResult(type);

        var result = await oauthManager.GenerateAuthUrlAsync(platformType, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> HandleCallback(
        string type, OAuthCallbackRequest body, IOAuthManager oauthManager, CancellationToken ct)
    {
        if (!TryParsePlatform(type, out var platformType))
            return InvalidPlatformResult(type);

        var result = await oauthManager.ExchangeCodeAsync(
            platformType, body.Code, body.State, body.CodeVerifier, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Disconnect(
        string type, IOAuthManager oauthManager, CancellationToken ct)
    {
        if (!TryParsePlatform(type, out var platformType))
            return InvalidPlatformResult(type);

        var result = await oauthManager.RevokeTokenAsync(platformType, ct);
        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> GetStatus(
        string type, IApplicationDbContext db, CancellationToken ct)
    {
        if (!TryParsePlatform(type, out var platformType))
            return InvalidPlatformResult(type);

        var platform = await db.Platforms
            .FirstOrDefaultAsync(p => p.Type == platformType, ct);

        if (platform is null)
            return Results.NotFound($"Platform '{type}' not configured");

        return Results.Ok(new
        {
            platform.IsConnected,
            platform.DisplayName,
            platform.Type,
            platform.TokenExpiresAt,
            platform.LastSyncAt,
            platform.GrantedScopes,
        });
    }

    private static async Task<IResult> TestPost(
        string type, TestPostRequest body, IEnumerable<ISocialPlatform> adapters, CancellationToken ct)
    {
        if (!body.Confirm)
            return Results.BadRequest("Set confirm=true to publish a test post.");

        if (!TryParsePlatform(type, out var platformType))
            return InvalidPlatformResult(type);

        var adapter = adapters.FirstOrDefault(a => a.Type == platformType);
        if (adapter is null)
            return Results.NotFound($"No adapter for platform '{type}'");

        var message = string.IsNullOrWhiteSpace(body.Message)
            ? $"Test post from Personal Brand Assistant - {DateTime.UtcNow:O}"
            : body.Message;

        var content = new PlatformContent(
            message, null, ContentType.SocialPost, [], ImmutableDictionary<string, string>.Empty);

        var result = await adapter.PublishAsync(content, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetEngagement(
        string type, string postId, IEnumerable<ISocialPlatform> adapters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId) || postId.Length > MaxPostIdLength)
            return Results.BadRequest("Invalid post ID.");

        if (!TryParsePlatform(type, out var platformType))
            return InvalidPlatformResult(type);

        var adapter = adapters.FirstOrDefault(a => a.Type == platformType);
        if (adapter is null)
            return Results.NotFound($"No adapter for platform '{type}'");

        var result = await adapter.GetEngagementAsync(postId, ct);
        return result.ToHttpResult();
    }

    private static bool TryParsePlatform(string type, out PlatformType platformType) =>
        Enum.TryParse(type, ignoreCase: true, out platformType);

    private static IResult InvalidPlatformResult(string type) =>
        Results.BadRequest($"Invalid platform type: {type}. Valid values: {string.Join(", ", Enum.GetNames<PlatformType>())}");
}

public record OAuthCallbackRequest(string Code, string? CodeVerifier, string State);
public record TestPostRequest(bool Confirm, string? Message = null);
