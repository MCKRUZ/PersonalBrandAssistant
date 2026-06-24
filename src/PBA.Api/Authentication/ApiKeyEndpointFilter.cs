using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PBA.Api.Authentication;

/// <summary>
/// Endpoint filter guarding the /api/external surface. Validates the X-Api-Key header
/// against the configured ExternalApi:ApiKey using a constant-time comparison.
/// Fails closed: if no key is configured, every request is rejected rather than allowed.
/// </summary>
public sealed class ApiKeyEndpointFilter(
    IOptionsMonitor<ExternalApiOptions> options,
    ILogger<ApiKeyEndpointFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuredKey = options.CurrentValue.ApiKey;

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            logger.LogWarning(
                "External API request to {Path} rejected: ExternalApi:ApiKey is not configured.",
                context.HttpContext.Request.Path);
            return Results.Unauthorized();
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!IsValidKey(provided, configuredKey))
            return Results.Unauthorized();

        return await next(context);
    }

    private static bool IsValidKey(string provided, string configured)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        // FixedTimeEquals returns false on length mismatch and compares equal-length
        // inputs in constant time, avoiding a byte-by-byte timing side channel.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(configured));
    }
}
