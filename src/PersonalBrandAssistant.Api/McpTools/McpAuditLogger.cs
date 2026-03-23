using System.Text.Json;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Api.McpTools;

public sealed class McpAuditLogger
{
    private static readonly HashSet<string> ExcludedParams =
        new(StringComparer.OrdinalIgnoreCase) { "clientRequestId" };

    private static readonly HashSet<string> TruncatedParams =
        new(StringComparer.OrdinalIgnoreCase) { "responseText" };

    private const int TruncateLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IApplicationDbContext _dbContext;

    public McpAuditLogger(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogToolInvocationAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        string outcome,
        Guid? entityId = null,
        string? correlationId = null,
        string? resultSummary = null,
        CancellationToken ct = default)
    {
        var redactedParams = RedactParameters(parameters);

        var details = JsonSerializer.Serialize(new
        {
            actor = "jarvis/openclaw",
            outcome,
            correlationId,
            resultSummary
        }, JsonOptions);

        if (details.Length > 2000)
            details = details[..2000];

        var entry = new AuditLogEntry
        {
            EntityType = "McpToolInvocation",
            EntityId = entityId ?? Guid.Empty,
            Action = toolName,
            NewValue = JsonSerializer.Serialize(redactedParams, JsonOptions),
            Timestamp = DateTimeOffset.UtcNow,
            Details = details
        };

        _dbContext.AuditLogEntries.Add(entry);
        await _dbContext.SaveChangesAsync(ct);
    }

    private static Dictionary<string, object?> RedactParameters(Dictionary<string, object?> parameters)
    {
        var redacted = new Dictionary<string, object?>();

        foreach (var (key, value) in parameters)
        {
            if (ExcludedParams.Contains(key))
                continue;

            if (TruncatedParams.Contains(key) && value is string s && s.Length > TruncateLength)
            {
                redacted[key] = s[..TruncateLength] + "...";
                continue;
            }

            redacted[key] = value;
        }

        return redacted;
    }
}
