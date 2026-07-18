using System.Security;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class OAuthEndpoints
{
    private static readonly HashSet<Platform> OAuthPlatforms =
        [Platform.LinkedIn, Platform.Twitter, Platform.YouTube, Platform.Instagram, Platform.TikTok];

    // Analytics credentials are only meaningful for platforms with an analytics provider. Restricting the
    // Analytics purpose to these keeps every publishing-side Platform-only credential lookup unambiguous
    // (a publishing platform can only ever have a Publishing credential).
    private static readonly HashSet<Platform> AnalyticsPlatforms =
        [Platform.YouTube, Platform.Instagram, Platform.TikTok];

    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("OAuth");

        group.MapGet("/{platform}/authorize", async (
            string platform,
            string? purpose,
            IOAuthService oauthService,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
                return Results.BadRequest("Invalid platform");

            if (!OAuthPlatforms.Contains(p))
                return Results.BadRequest($"{p} does not support OAuth. Use credential storage instead.");

            if (!TryParsePurpose(purpose, out var credentialPurpose))
                return Results.BadRequest($"Invalid purpose '{purpose}'. Use 'publishing' or 'analytics'.");

            if (credentialPurpose == CredentialPurpose.Analytics && !AnalyticsPlatforms.Contains(p))
                return Results.BadRequest($"{p} does not support analytics OAuth.");

            var authUrl = await oauthService.GetAuthorizationUrlAsync(p, credentialPurpose, ct);
            return Results.Redirect(authUrl);
        });

        group.MapGet("/{platform}/callback", async (
            string platform,
            string code,
            string state,
            IOAuthService oauthService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
                return Results.BadRequest("Invalid platform");

            var logger = loggerFactory.CreateLogger("OAuthEndpoints");

            try
            {
                await oauthService.ExchangeCodeAsync(p, code, state, ct);
                return Results.Redirect($"/settings/platforms?connected={p}");
            }
            catch (SecurityException)
            {
                return Results.Forbid();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OAuth callback failed for {Platform}", p);
                return Results.Redirect($"/settings/platforms?error=auth_failed");
            }
        });

        group.MapGet("/{platform}/status", async (
            string platform,
            IAppDbContext db,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
                return Results.BadRequest("Invalid platform");

            var credential = await db.PlatformCredentials
                .FirstOrDefaultAsync(c => c.Platform == p && c.IsActive, ct);

            if (credential is null)
                return Results.Ok(new { status = "NotConfigured" });

            if (credential.AccessTokenExpiresAt.HasValue &&
                credential.AccessTokenExpiresAt.Value < DateTimeOffset.UtcNow)
                return Results.Ok(new { status = "Expired" });

            return Results.Ok(new
            {
                status = "Connected",
                expiresAt = credential.AccessTokenExpiresAt
            });
        });

        group.MapDelete("/{platform}", async (
            string platform,
            IAppDbContext db,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
                return Results.BadRequest("Invalid platform");

            var credential = await db.PlatformCredentials
                .FirstOrDefaultAsync(c => c.Platform == p, ct);

            if (credential is null)
                return Results.NotFound();

            db.PlatformCredentials.Remove(credential);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    // Absent/empty -> Publishing (the pre-analytics default). Only the literal "publishing"/"analytics"
    // (any case) are accepted; anything else — including numeric strings — is rejected so a typo never
    // silently stores a Publishing credential.
    private static bool TryParsePurpose(string? purpose, out CredentialPurpose result)
    {
        result = CredentialPurpose.Publishing;
        if (string.IsNullOrWhiteSpace(purpose))
            return true;

        switch (purpose.ToLowerInvariant())
        {
            case "publishing":
                result = CredentialPurpose.Publishing;
                return true;
            case "analytics":
                result = CredentialPurpose.Analytics;
                return true;
            default:
                return false;
        }
    }
}
