using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace PersonalBrandAssistant.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly HashSet<string> SensitivePatterns = ["Token", "Password", "Secret", "Key"];
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName} with {RequestData}",
            requestName, SanitizeRequest(request));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();

            _logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Failed handling {RequestName} in {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    private static readonly Lazy<System.Reflection.PropertyInfo[]> CachedProperties =
        new(() => typeof(TRequest).GetProperties());

    private static Dictionary<string, object?> SanitizeRequest(TRequest request)
    {
        var properties = CachedProperties.Value;
        var sanitized = new Dictionary<string, object?>();

        foreach (var prop in properties)
        {
            if (SensitivePatterns.Any(p => prop.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                sanitized[prop.Name] = "[REDACTED]";
            }
            else
            {
                sanitized[prop.Name] = prop.GetValue(request);
            }
        }

        return sanitized;
    }
}
