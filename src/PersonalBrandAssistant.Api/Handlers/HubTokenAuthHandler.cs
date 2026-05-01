using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PersonalBrandAssistant.Api.Handlers;

public sealed class HubTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "HubToken";

    public HubTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Context.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            var authHeader = Context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                token = authHeader["Bearer ".Length..];
        }

        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var secret = Context.RequestServices.GetRequiredService<IConfiguration>()["Jwt:HubSecret"];
        if (string.IsNullOrEmpty(secret))
            return Task.FromResult(AuthenticateResult.Fail("Hub secret not configured"));

        var parts = token.Split('.');
        if (parts.Length != 2)
            return Task.FromResult(AuthenticateResult.Fail("Invalid token format"));

        try
        {
            var payloadBytes = Convert.FromBase64String(parts[0]);
            var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payloadBytes);
            var actualSignature = Convert.FromBase64String(parts[1]);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
                return Task.FromResult(AuthenticateResult.Fail("Invalid token signature"));

            var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
            if (payload.TryGetProperty("exp", out var expElement))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                if (exp < DateTimeOffset.UtcNow)
                    return Task.FromResult(AuthenticateResult.Fail("Token expired"));
            }

            var claims = new[] { new Claim(ClaimTypes.Name, "hub-client") };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Token validation failed"));
        }
    }
}
