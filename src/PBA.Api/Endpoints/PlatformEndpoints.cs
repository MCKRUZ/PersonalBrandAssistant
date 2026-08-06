using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Dtos;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class PlatformEndpoints
{
    private static readonly Platform[] SupportedPlatforms =
        [Platform.Blog, Platform.Medium, Platform.Substack, Platform.LinkedIn, Platform.Twitter];

    // Recorded on the seeded row for the operator's benefit — Meta does not report a token's scopes
    // back, and "why can't it post" is unanswerable from a row that says nothing about what it is for.
    private const string InstagramPublishingScopes =
        "instagram_business_basic instagram_business_content_publish";

    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platforms").WithTags("Platforms");

        group.MapGet("/", async (
            IAppDbContext db,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var credentials = await db.PlatformCredentials
                .Where(c => c.IsActive)
                .ToListAsync(ct);

            var lastPublishDates = await db.ContentPlatformPublishes
                .Where(p => p.Status == PublishStatus.Published)
                .GroupBy(p => p.Platform)
                .Select(g => new { Platform = g.Key, LastPublish = g.Max(p => p.PublishedAt) })
                .ToDictionaryAsync(x => x.Platform, x => x.LastPublish, ct);

            var result = new List<PlatformStatusDto>();
            foreach (var platform in SupportedPlatforms)
            {
                var cred = credentials.FirstOrDefault(c => c.Platform == platform);
                var connector = sp.GetKeyedService<IPlatformConnector>(platform);

                var status = "NotConfigured";
                if (cred is not null)
                {
                    status = cred.AccessTokenExpiresAt.HasValue &&
                             cred.AccessTokenExpiresAt.Value < DateTimeOffset.UtcNow
                        ? "Expired"
                        : "Connected";
                }
                else if (platform == Platform.Blog && connector is not null &&
                         await connector.ValidateCredentialsAsync(ct))
                {
                    // Blog has no OAuth credential row; readiness is the website repo being present.
                    status = "Connected";
                }

                result.Add(new PlatformStatusDto
                {
                    Platform = platform,
                    IsConnected = status == "Connected",
                    Status = status,
                    ExpiresAt = cred?.AccessTokenExpiresAt,
                    LastPublishDate = lastPublishDates.GetValueOrDefault(platform),
                    Capabilities = connector?.GetCapabilities()
                });
            }

            return Results.Ok(result);
        });

        group.MapPost("/{platform}/credentials", async (
            string platform,
            StoreCredentialsRequest body,
            IAppDbContext db,
            ITokenEncryptor encryptor,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
                return Results.BadRequest("Invalid platform");

            switch (p)
            {
                case Platform.Blog:
                    return Results.BadRequest("Blog does not require credentials.");
                case Platform.Medium:
                case Platform.Instagram:
                    if (string.IsNullOrWhiteSpace(body.Token))
                        return Results.BadRequest($"Token is required for {p}");
                    break;
                case Platform.Substack:
                    return Results.BadRequest("Substack credential storage via API is not yet supported. Use browser login.");
                case Platform.LinkedIn:
                case Platform.Twitter:
                    return Results.BadRequest($"{p} uses OAuth. Use /api/auth/{p}/authorize instead.");
                default:
                    return Results.BadRequest($"Unsupported platform: {p}");
            }

            // Scoped to the PURPOSE, not just the platform. Instagram holds two separate credentials
            // — an insights-scoped Analytics one and a content-publish-scoped Publishing one — and
            // replacing "the Instagram credential" wholesale would silently delete channel analytics
            // while seeding a publisher.
            var existing = await db.PlatformCredentials
                .FirstOrDefaultAsync(c => c.Platform == p && c.Purpose == CredentialPurpose.Publishing, ct);

            if (existing is not null)
                db.PlatformCredentials.Remove(existing);

            var credential = new Domain.Entities.PlatformCredential
            {
                Platform = p,
                Purpose = CredentialPurpose.Publishing,
                IsActive = true,
                EncryptedAccessToken = string.Empty
            };

            if (p == Platform.Medium)
            {
                credential.EncryptedIntegrationToken = encryptor.Encrypt(body.Token!);
            }
            else if (p == Platform.Instagram)
            {
                credential.EncryptedAccessToken = encryptor.Encrypt(body.Token!);
                credential.Scopes = InstagramPublishingScopes;
                // Instagram long-lived tokens last 60 days and are extended in place by
                // InstagramOAuthProvider's ig_refresh_token call, which NeedsRefresh triggers ahead
                // of expiry. Recording the horizon is what arms that refresh — leaving it null means
                // nothing ever refreshes and the lane dies silently about two months from now.
                credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(60);
            }

            db.PlatformCredentials.Add(credential);
            await db.SaveChangesAsync(ct);

            // Instagram only. This is a hand-seeded long-lived token rather than one minted by an
            // OAuth round-trip, so nothing has proved it works or carries the publish scope — and a
            // dud left sitting active reports connected while failing every post. Ask Meta, and
            // deactivate the row if it says no.
            //
            // Deliberately NOT applied to the other platforms here: their storage contract predates
            // this and returns 200 without a liveness check. Extending the check to them is probably
            // right, but it changes behaviour they already depend on and is not this change's job.
            if (p != Platform.Instagram)
                return Results.Ok(new { stored = true, ready = (bool?)null, reason = (string?)null });

            var connector = sp.GetKeyedService<IPlatformConnector>(p);
            if (connector is not null && !await connector.ValidateCredentialsAsync(ct))
            {
                credential.IsActive = false;
                credential.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new
                {
                    stored = false,
                    reason = "Instagram rejected the credential, so it was stored inactive rather " +
                             "than left looking connected. Check the token and that it carries " +
                             "instagram_business_content_publish."
                });
            }

            return Results.Ok(new { stored = true, ready = true, reason = (string?)null });
        });
    }
}
