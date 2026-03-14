using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class MediaEndpoints
{
    private static readonly Dictionary<string, string> ExtensionToContentType = new()
    {
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".mp4"] = "video/mp4",
    };

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/media").WithTags("Media");
        group.MapGet("/{fileId}", ServeMedia).AllowAnonymous();
    }

    private static async Task<IResult> ServeMedia(
        string fileId,
        string token,
        long expires,
        IMediaStorage mediaStorage,
        IOptions<MediaStorageOptions> options)
    {
        if (DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(expires))
            return Results.StatusCode(403);

        var signingKey = options.Value.SigningKey;
        if (string.IsNullOrEmpty(signingKey))
            return Results.StatusCode(500);

        var expectedToken = ComputeHmac(fileId, expires, signingKey);
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);

        if (!CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes))
            return Results.StatusCode(403);

        try
        {
            var stream = await mediaStorage.GetStreamAsync(fileId, CancellationToken.None);
            var extension = Path.GetExtension(fileId);
            var contentType = ExtensionToContentType.GetValueOrDefault(extension, "application/octet-stream");

            var remaining = DateTimeOffset.FromUnixTimeSeconds(expires) - DateTimeOffset.UtcNow;
            return Results.Stream(stream, contentType, enableRangeProcessing: false);
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static string ComputeHmac(string fileId, long expiryEpoch, string signingKey)
    {
        var key = Encoding.UTF8.GetBytes(signingKey);
        var message = Encoding.UTF8.GetBytes($"{fileId}:{expiryEpoch}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
