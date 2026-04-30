using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/hub-token", IssueHubToken);
    }

    private static IResult IssueHubToken(IConfiguration config)
    {
        var secret = config["Jwt:HubSecret"];
        if (string.IsNullOrEmpty(secret))
            return Results.Problem("Hub JWT secret is not configured", statusCode: 500);

        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        var payload = JsonSerializer.Serialize(new { purpose = "signalr", exp = expiry.ToUnixTimeSeconds() });
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var payloadBase64 = Convert.ToBase64String(payloadBytes);

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var signature = HMACSHA256.HashData(keyBytes, payloadBytes);
        var signatureBase64 = Convert.ToBase64String(signature);

        var token = $"{payloadBase64}.{signatureBase64}";

        return Results.Ok(new { token, expiresAt = expiry });
    }
}
