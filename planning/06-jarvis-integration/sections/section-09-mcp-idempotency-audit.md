# Section 09: MCP Idempotency and Audit Trail

## Overview

Cross-cutting concerns applied to all MCP write tools across the four tool classes (ContentPipelineTools, CalendarTools, AnalyticsTools, SocialEngagementTools). This section adds two capabilities:

1. **Idempotency**: All write MCP tools accept an optional `clientRequestId` parameter. If provided, the tool checks a short-lived in-memory cache (5-minute TTL) for a matching request. Duplicate requests return the cached result instead of re-executing. This prevents voice command retries and LLM tool retries from creating duplicate actions.

2. **Audit Trail**: All write MCP tool invocations log to PBA's existing audit trail with the actor `jarvis/openclaw`, the tool name, parameters (sensitive values redacted), outcome, and correlation ID.

The five write tools that require both idempotency and audit trail are:
- `pba_create_content` (section 05)
- `pba_publish_content` (section 05)
- `pba_schedule_content` (section 06)
- `pba_reschedule_content` (section 06)
- `pba_respond_to_opportunity` (section 08)

Read-only tools (`pba_get_pipeline_status`, `pba_list_drafts`, `pba_get_calendar`, `pba_get_trends`, `pba_get_engagement_stats`, `pba_get_content_performance`, `pba_get_opportunities`, `pba_get_inbox`) do not need idempotency or audit logging.

## Dependencies

- **section-05-mcp-content-pipeline-tools**: Write tools `pba_create_content`, `pba_publish_content` must exist.
- **section-06-mcp-calendar-tools**: Write tools `pba_schedule_content`, `pba_reschedule_content` must exist.
- **section-07-mcp-analytics-tools**: No write tools -- no changes needed in this tool class.
- **section-08-mcp-social-tools**: Write tool `pba_respond_to_opportunity` must exist.

## Tests (Write First)

### Idempotency Tests

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/McpIdempotencyTests.cs`

Use xUnit + Moq. Mock `IMemoryCache` or use `Microsoft.Extensions.Caching.Memory.MemoryCache` with a test-scoped instance.

```csharp
// Test: write tool with clientRequestId caches result
//   Call pba_create_content with clientRequestId = "req-001"
//   Assert the result is returned and the cache contains an entry for "req-001"

// Test: duplicate clientRequestId returns cached result without re-executing
//   Call pba_create_content with clientRequestId = "req-002"
//   Call pba_create_content again with the same clientRequestId = "req-002"
//   Assert IContentPipeline.CreateFromTopicAsync was called exactly once
//   Assert both calls return identical result JSON

// Test: different clientRequestId executes independently
//   Call pba_create_content with clientRequestId = "req-003"
//   Call pba_create_content with clientRequestId = "req-004"
//   Assert IContentPipeline.CreateFromTopicAsync was called twice
//   Assert the two results contain different content IDs

// Test: cached result expires after 5 minutes
//   Call pba_create_content with clientRequestId = "req-005"
//   Advance the test clock past 5 minutes (use IMemoryCache with expiration)
//   Call pba_create_content with the same clientRequestId = "req-005"
//   Assert IContentPipeline.CreateFromTopicAsync was called twice
//   (cache entry expired, so second call executes fresh)

// Test: write tool without clientRequestId always executes
//   Call pba_create_content twice with clientRequestId = null
//   Assert IContentPipeline.CreateFromTopicAsync was called twice
//   Assert the results contain different content IDs
```

### Audit Trail Tests

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/McpAuditTrailTests.cs`

Use xUnit + Moq. Mock the audit service or verify audit entries written to the database.

```csharp
// Test: write tool creates audit log entry with actor "jarvis/openclaw"
//   Call pba_create_content
//   Assert an audit log entry was created
//   Assert the entry's Actor field is "jarvis/openclaw"

// Test: audit log includes tool name and redacted parameters
//   Call pba_schedule_content with a contentId and dateTime
//   Assert audit entry has ToolName = "pba_schedule_content"
//   Assert audit entry parameters are recorded
//   Assert sensitive values (if any) are redacted (e.g., response text is truncated)

// Test: audit log includes outcome (success/failure/queued)
//   Call pba_publish_content successfully -> assert outcome = "success"
//   Call pba_publish_content for non-approved content -> assert outcome = "failure"
//   Call pba_respond_to_opportunity with Manual autonomy -> assert outcome = "queued-for-approval"

// Test: read-only tools do not create audit entries
//   Call pba_get_pipeline_status
//   Assert no audit log entries were created

// Test: audit log includes correlation ID when present
//   Set up an OpenClaw correlation ID in the request context
//   Call pba_create_content
//   Assert the audit entry's CorrelationId matches the OpenClaw request context
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Api/McpTools/Infrastructure/IdempotencyHandler.cs` -- Idempotency cache wrapper.
- `src/PersonalBrandAssistant.Api/McpTools/Infrastructure/McpAuditLogger.cs` -- Audit trail logger for MCP write operations.
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/McpIdempotencyTests.cs` -- Idempotency tests.
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/McpAuditTrailTests.cs` -- Audit trail tests.

### Modified Files

- `src/PersonalBrandAssistant.Api/McpTools/ContentPipelineTools.cs` -- Add `clientRequestId` parameter to `pba_create_content` and `pba_publish_content`. Integrate idempotency check and audit logging.
- `src/PersonalBrandAssistant.Api/McpTools/CalendarTools.cs` -- Add `clientRequestId` parameter to `pba_schedule_content` and `pba_reschedule_content`. Integrate idempotency check and audit logging.
- `src/PersonalBrandAssistant.Api/McpTools/SocialEngagementTools.cs` -- Add `clientRequestId` parameter to `pba_respond_to_opportunity`. Integrate idempotency check and audit logging.
- `src/PersonalBrandAssistant.Api/Program.cs` -- Register `IMemoryCache` and `McpAuditLogger` in the DI container.

## Idempotency Design

### IdempotencyHandler

A thin wrapper around `IMemoryCache` that encapsulates the idempotency check-and-cache pattern. Each write tool calls this handler at the start and end of execution.

```csharp
/// Checks the cache for an existing result. If found, returns the cached JSON.
/// If not found, returns null (indicating the tool should execute).
/// After execution, the tool calls CacheResult to store the outcome.
public class IdempotencyHandler
{
    public string? TryGetCachedResult(string clientRequestId);
    public void CacheResult(string clientRequestId, string resultJson);
}
```

Configuration:
- Cache key format: `mcp:idempotency:{clientRequestId}`
- TTL: 5 minutes (`MemoryCacheEntryOptions` with `AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)`)
- Storage: In-process `IMemoryCache` (adequate for single-instance deployment on Synology NAS)

Usage pattern in each write tool:

```csharp
// At the start of each write tool method:
var idempotency = scope.ServiceProvider.GetRequiredService<IdempotencyHandler>();
if (clientRequestId is not null)
{
    var cached = idempotency.TryGetCachedResult(clientRequestId);
    if (cached is not null) return cached;
}

// ... execute the actual operation ...

// Before returning:
if (clientRequestId is not null)
{
    idempotency.CacheResult(clientRequestId, resultJson);
}
return resultJson;
```

### Parameter Addition

Each write tool gains an optional `clientRequestId` parameter:

```csharp
[Description("Optional client-generated request ID for idempotency. If provided, duplicate calls with the same ID return the cached result instead of re-executing. Use a UUID or similar unique value.")] string? clientRequestId
```

This parameter is added as the last parameter before `CancellationToken` on all five write tools.

## Audit Trail Design

### McpAuditLogger

A service that writes audit entries for MCP write tool invocations. Uses PBA's existing audit infrastructure (audit log table in the database).

```csharp
/// Logs an MCP tool invocation to the audit trail.
public class McpAuditLogger
{
    public async Task LogAsync(McpAuditEntry entry, CancellationToken ct);
}

public record McpAuditEntry(
    string ToolName,
    Dictionary<string, string> Parameters,
    string Outcome,       // "success", "failure", "queued-for-approval"
    string? CorrelationId,
    string? ErrorMessage);
```

Audit entry fields written to the database:
- `Actor`: Always `"jarvis/openclaw"` (distinguishes MCP-originated actions from direct API or UI actions).
- `ToolName`: The MCP tool name (e.g., `pba_create_content`).
- `Parameters`: A JSON-serialized dictionary of parameter names to values. Sensitive values are redacted:
  - `responseText` is truncated to 50 characters followed by "[redacted]"
  - Any parameter named `*key*`, `*token*`, `*secret*` is fully redacted to `"[redacted]"`
- `Outcome`: One of `"success"`, `"failure"`, or `"queued-for-approval"`.
- `ErrorMessage`: Present only when outcome is `"failure"`, containing the error description.
- `CorrelationId`: From the OpenClaw request context if available, otherwise null.
- `Timestamp`: UTC timestamp of the invocation.

### Integration Pattern

Each write tool calls the audit logger after execution, regardless of outcome:

```csharp
// After execution (success or failure):
var auditLogger = scope.ServiceProvider.GetRequiredService<McpAuditLogger>();
await auditLogger.LogAsync(new McpAuditEntry(
    ToolName: "pba_create_content",
    Parameters: new Dictionary<string, string>
    {
        ["topic"] = topic,
        ["platform"] = platform,
        ["contentType"] = contentType
    },
    Outcome: success ? "success" : "failure",
    CorrelationId: correlationId,
    ErrorMessage: errorMessage), ct);
```

### Correlation ID

The correlation ID links the MCP tool invocation back to the originating OpenClaw request. When OpenClaw spawns the MCP process and sends a tool call, the request context may include a correlation ID. This is extracted from the MCP request metadata (if available) or generated as a new GUID.

## Parameter Redaction Rules

The audit logger applies these redaction rules to parameter values before persisting:

1. Parameters named `responseText` (from `pba_respond_to_opportunity`): Truncate to first 50 characters and append `"[redacted]"`.
2. Parameters with names containing `key`, `token`, or `secret` (case-insensitive): Replace entire value with `"[redacted]"`.
3. All other parameters: Store the full value.

This prevents sensitive user-authored content from being stored verbatim in the audit log while still preserving enough context for debugging and compliance.

## DI Registration

In `Program.cs`, register the idempotency and audit services for both HTTP and MCP modes (the services are needed when MCP mode is active):

```csharp
// Add memory cache for idempotency (may already be registered for other uses)
builder.Services.AddMemoryCache();

// Register idempotency handler and audit logger
builder.Services.AddSingleton<IdempotencyHandler>();
builder.Services.AddScoped<McpAuditLogger>();
```

The `IdempotencyHandler` is registered as singleton because it wraps a singleton `IMemoryCache`. The `McpAuditLogger` is scoped because it writes to the database via scoped `IApplicationDbContext`.

## Implementation Notes

- The idempotency cache is in-process memory only. This works for the single-instance deployment on the Synology NAS. If PBA scales to multiple instances, the cache would need to move to Redis or a shared store.
- The 5-minute TTL is a balance between preventing duplicate actions from quick retries and not holding stale cache entries indefinitely. Voice command retries typically happen within seconds, and LLM tool retries within a minute.
- The `clientRequestId` parameter is optional on all write tools. When omitted, the tool always executes -- there is no idempotency protection. This is the expected behavior for direct invocations where the caller does not need retry safety.
- Audit logging is fire-and-forget in the sense that a failure to write an audit entry should not cause the tool to fail. Wrap the audit call in a try-catch and log the audit failure to structured logs but still return the tool result.
- Read-only tools do not create audit entries and do not accept `clientRequestId`. This keeps the read path lightweight.
- The audit log uses PBA's existing audit infrastructure. If no audit table exists yet, this section creates the `AuditLogEntry` entity and adds it to the `IApplicationDbContext` DbSet. The entity should be added as an EF Core migration.
