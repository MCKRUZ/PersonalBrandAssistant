# Section 10 -- API Endpoints

## Overview

This section implements the Minimal API endpoints that expose the agent orchestration system to HTTP clients. Six endpoints grouped under `/api/agents`: streaming SSE, non-streaming execution, status/history queries, and usage/budget reporting.

**Files created:**
- `src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs` (180 lines, 6 endpoints)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs` (12 tests)

**Files modified:**
- `src/PersonalBrandAssistant.Api/Program.cs` — added `app.MapAgentEndpoints()`

**Deviations from plan:**
- SSE error messages sanitized: only ValidationFailed errors show raw message, internal errors get generic message (code review fix)
- Tests use per-test `WithWebHostBuilder` mocks instead of static shared mocks (code review fix — thread safety)
- Input validation deferred to section-11 (FluentValidation wiring needed first)
- Pagination for ListExecutions deferred (requires IAgentOrchestrator interface change)

## Dependencies

- Section 03: `IAgentOrchestrator`, `AgentTask`, `AgentExecutionResult`, `ITokenTracker`
- Section 09: `AgentOrchestrator` implementation
- Section 01: `AgentExecution` entity
- Section 02: `AgentCapabilityType`, `AgentExecutionStatus` enums

## Tests First

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs

// Test: POST /api/agents/stream returns text/event-stream content type
// Test: POST /api/agents/stream sets Cache-Control: no-store
// Test: POST /api/agents/stream emits token events during generation
// Test: POST /api/agents/stream emits complete event with executionId
// Test: POST /api/agents/stream emits error event on failure
// Test: POST /api/agents/execute returns 202 Accepted with executionId
// Test: POST /api/agents/execute?wait=true returns full result inline
// Test: GET /api/agents/executions/{id} returns execution status
// Test: GET /api/agents/executions/{id} returns 404 for unknown ID
// Test: GET /api/agents/executions?contentId={id} returns filtered list
// Test: GET /api/agents/usage returns token usage summary for date range
// Test: GET /api/agents/budget returns current budget status
```

Tests use `CustomWebApplicationFactory` with mock `IAgentOrchestrator` and `ITokenTracker` registered via `ConfigureTestServices`.

## Implementation Details

### Endpoint Structure

Follow existing convention: static class with `Map*Endpoints` extension method, route group with tags.

```csharp
namespace PersonalBrandAssistant.Api.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents").WithTags("Agents");

        group.MapPost("/stream", StreamExecution);
        group.MapPost("/execute", ExecuteAgent);
        group.MapGet("/executions/{id:guid}", GetExecution);
        group.MapGet("/executions", ListExecutions);
        group.MapGet("/usage", GetUsage);
        group.MapGet("/budget", GetBudget);
    }
}
```

### Request DTO

```csharp
record AgentExecuteRequest(AgentCapabilityType Type, Guid? ContentId, Dictionary<string, string>? Parameters);
```

### POST /api/agents/stream (SSE Streaming)

1. Set headers: `Content-Type: text/event-stream`, `Cache-Control: no-store`, `X-Accel-Buffering: no`
2. Call `IAgentOrchestrator.ExecuteAsync()` and write SSE events as data arrives
3. SSE format: `data: {"type":"token","text":"..."}\n\n`
4. Event types: `token`, `status`, `usage`, `complete`, `error`
5. Flush after each event
6. Use `HttpContext.RequestAborted` for disconnect detection
7. `try/finally` ensures execution status updated on disconnect

Handler uses `HttpContext` directly for low-level response control.

### POST /api/agents/execute (Non-Streaming)

Two modes via `wait` query param:
- **Default:** Return `202 Accepted` with `{ executionId }` after starting execution
- **`?wait=true`:** Block until complete, return `200 OK` with full `AgentExecutionResult`

### GET /api/agents/executions/{id}

Delegates to `IAgentOrchestrator.GetExecutionStatusAsync(id)`. Uses `result.ToHttpResult()`.

### GET /api/agents/executions

Optional `contentId` query filter. Delegates to `ListExecutionsAsync(contentId)`.

### GET /api/agents/usage

Accepts `from` and `to` query params. Delegates to `ITokenTracker.GetCostForPeriodAsync()`. Returns usage summary.

### GET /api/agents/budget

Delegates to `ITokenTracker.GetBudgetRemainingAsync()` and `IsOverBudgetAsync()`. Returns:

```json
{
    "dailyRemaining": 7.50,
    "monthlyRemaining": 85.00,
    "isOverBudget": false
}
```

### SSE Event Format

```
data: {"type":"token","text":"Hello"}

data: {"type":"status","status":"running"}

data: {"type":"usage","inputTokens":150,"outputTokens":42}

data: {"type":"complete","executionId":"..."}

data: {"type":"error","message":"Budget exceeded"}
```

### Program.cs

Add `app.MapAgentEndpoints();` after existing endpoint mappings.

### Error Handling

- Invalid request: FluentValidation returns 400
- Budget exceeded: 400 via `Result.ValidationFailed`
- Not found: 404 via `Result.NotFound`
- Streaming errors: emit SSE error event, close stream
- All endpoints require API key (existing `ApiKeyMiddleware`)
