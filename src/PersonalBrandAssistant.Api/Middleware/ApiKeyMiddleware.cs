using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace PersonalBrandAssistant.Api.Middleware;

public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private static readonly HashSet<string> ExemptPaths =
        new(["/health"], StringComparer.OrdinalIgnoreCase);

    private readonly RequestDelegate _next;
    private readonly byte[] _apiKeyHash;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var apiKey = configuration["ApiKey"]
            ?? throw new InvalidOperationException("ApiKey configuration is required.");
        _apiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path) || context.Request.Method == HttpMethods.Options)
        {
            await _next(context);
            return;
        }

        var hasHeaderKey = context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
                           && IsValidKey(providedKey.ToString());

        if (!hasHeaderKey && context.Request.Path.StartsWithSegments("/hubs"))
        {
            var queryKey = context.Request.Query["apiKey"].FirstOrDefault();
            if (queryKey is not null && IsValidKey(queryKey))
            {
                await _next(context);
                return;
            }
        }

        if (!hasHeaderKey)
        {
            var problemDetails = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Invalid or missing API key.",
            };

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problemDetails);
            return;
        }

        await _next(context);
    }

    private bool IsValidKey(string providedKey)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        return CryptographicOperations.FixedTimeEquals(providedHash, _apiKeyHash);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPaths.Contains(path.Value ?? string.Empty);
}
