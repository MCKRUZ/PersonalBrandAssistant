using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace PersonalBrandAssistant.Api.Middleware;

public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private static readonly HashSet<string> ExemptPaths =
        new(["/health"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> WriteMethods =
        new([HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete],
            StringComparer.OrdinalIgnoreCase);

    private readonly RequestDelegate _next;
    private readonly byte[] _readonlyKeyHash;
    private readonly byte[] _writeKeyHash;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        var readonlyKey = configuration["ApiKeys:ReadonlyKey"];
        var writeKey = configuration["ApiKeys:WriteKey"];

        if (readonlyKey is not null && writeKey is not null)
        {
            _readonlyKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(readonlyKey));
            _writeKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(writeKey));
        }
        else
        {
            var legacyKey = configuration["ApiKey"]
                ?? throw new InvalidOperationException("ApiKey or ApiKeys configuration is required.");
            _writeKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(legacyKey));
            _readonlyKeyHash = _writeKeyHash;
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path) || context.Request.Method == HttpMethods.Options)
        {
            await _next(context);
            return;
        }

        var providedKeyString = ExtractKey(context);
        if (providedKeyString is null)
        {
            await WriteUnauthorized(context);
            return;
        }

        var resolvedScope = ResolveScope(providedKeyString);
        if (resolvedScope is null)
        {
            await WriteUnauthorized(context);
            return;
        }

        var requiredScope = WriteMethods.Contains(context.Request.Method)
            ? ApiKeyScope.Write
            : ApiKeyScope.Readonly;

        if (requiredScope == ApiKeyScope.Write && resolvedScope == ApiKeyScope.Readonly)
        {
            await WriteForbidden(context);
            return;
        }

        context.Items["ApiKeyScope"] = resolvedScope.Value;
        await _next(context);
    }

    private ApiKeyScope? ResolveScope(string providedKey)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));

        var matchesWrite = CryptographicOperations.FixedTimeEquals(providedHash, _writeKeyHash);
        var matchesReadonly = CryptographicOperations.FixedTimeEquals(providedHash, _readonlyKeyHash);

        if (matchesWrite) return ApiKeyScope.Write;
        if (matchesReadonly) return ApiKeyScope.Readonly;
        return null;
    }

    private static string? ExtractKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerKey)
            && !string.IsNullOrEmpty(headerKey.ToString()))
        {
            return headerKey.ToString();
        }

        if (context.Request.Path.StartsWithSegments("/hubs"))
        {
            var queryKey = context.Request.Query["apiKey"].FirstOrDefault();
            if (!string.IsNullOrEmpty(queryKey))
            {
                return queryKey;
            }
        }

        return null;
    }

    private static async Task WriteUnauthorized(HttpContext context)
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
    }

    private static async Task WriteForbidden(HttpContext context)
    {
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            Title = "Forbidden",
            Status = StatusCodes.Status403Forbidden,
            Detail = "API key does not have sufficient scope for this operation.",
        };

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPaths.Contains(path.Value ?? string.Empty);
}
