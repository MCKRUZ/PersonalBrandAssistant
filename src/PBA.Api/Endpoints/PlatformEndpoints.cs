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

            var result = SupportedPlatforms.Select(platform =>
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

                return new PlatformStatusDto
                {
                    Platform = platform,
                    IsConnected = cred is not null && status == "Connected",
                    Status = status,
                    ExpiresAt = cred?.AccessTokenExpiresAt,
                    LastPublishDate = lastPublishDates.GetValueOrDefault(platform),
                    Capabilities = connector?.GetCapabilities()
                };
            }).ToList();

            return Results.Ok(result);
        });

        group.MapPost("/{platform}/credentials", async (
            string platform,
            StoreCredentialsRequest body,
            IAppDbContext db,
            ITokenEncryptor encryptor,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
                return Results.BadRequest("Invalid platform");

            switch (p)
            {
                case Platform.Blog:
                    return Results.BadRequest("Blog does not require credentials.");
                case Platform.Medium:
                    if (string.IsNullOrWhiteSpace(body.Token))
                        return Results.BadRequest("Token is required for Medium");
                    break;
                case Platform.Substack:
                    return Results.BadRequest("Substack credential storage via API is not yet supported. Use browser login.");
                case Platform.LinkedIn:
                case Platform.Twitter:
                    return Results.BadRequest($"{p} uses OAuth. Use /api/auth/{p}/authorize instead.");
                default:
                    return Results.BadRequest($"Unsupported platform: {p}");
            }

            var existing = await db.PlatformCredentials
                .FirstOrDefaultAsync(c => c.Platform == p, ct);

            if (existing is not null)
                db.PlatformCredentials.Remove(existing);

            var credential = new Domain.Entities.PlatformCredential
            {
                Platform = p,
                IsActive = true,
                EncryptedAccessToken = string.Empty
            };

            if (p == Platform.Medium)
            {
                credential.EncryptedIntegrationToken = encryptor.Encrypt(body.Token!);
            }

            db.PlatformCredentials.Add(credential);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}
