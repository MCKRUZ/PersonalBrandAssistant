Now I have all the context needed. Let me generate the section content.

# Section 10: API Endpoints -- Automation Dashboard Support

## Overview

This section adds the `AutomationEndpoints` minimal API endpoint group that exposes the content automation pipeline to the dashboard UI. It provides five endpoints under `/api/automation` for listing runs, viewing run details, manually triggering a pipeline execution, and reading/updating automation configuration.

These endpoints follow the same patterns used throughout the project: static extension methods for endpoint registration, `IEndpointRouteBuilder.MapGroup` for grouping, `WithTags` for Swagger, and the `ResultExtensions.ToHttpResult()` helper for consistent error responses.

---

## Dependencies

- **section-01-foundation** (must be complete): Provides `AutomationRun` entity, `AutomationRunStatus` enum, `ContentAutomationOptions`, `AutomationRunResult`, `IDailyContentOrchestrator` interface, `DbSet<AutomationRun>` on `IApplicationDbContext`.
- **section-08-orchestrator** (must be complete): Provides the real `DailyContentOrchestrator` implementation so `POST /trigger` can invoke it. Without this, the trigger endpoint would hit the `NotImplementedException` stub.

**Blocks:** Nothing. This is a leaf section.

---

## Tests First

All tests go in `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AutomationEndpointsTests.cs`. These are integration tests using `WebApplicationFactory<Program>`, following the same pattern as `ContentEngineEndpointsTests`.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AutomationEndpointsTests.cs`

The test class uses `IClassFixture` with an inner `TestFactory` that:
- Removes all hosted services (prevents background jobs from running during tests)
- Sets a test API key
- Uses a dummy connection string (tests mock the DB context and orchestrator -- no real DB needed for these endpoint tests)
- Registers mock implementations of `IDailyContentOrchestrator` and `IApplicationDbContext` via `ConfigureTestServices`

Each test creates an `HttpClient` with the `X-Api-Key` header set (authenticated) or without it (unauthenticated) by calling `WithWebHostBuilder` and injecting mock services per-test, exactly as `ContentEngineEndpointsTests` does.

#### Test: GET /api/automation/runs returns list of recent runs

- Mock `IApplicationDbContext.AutomationRuns` to return a queryable with two `AutomationRun` entities (one Completed, one Failed).
- Send `GET /api/automation/runs`.
- Assert `200 OK` and response body contains a JSON array.

#### Test: GET /api/automation/runs/{id} returns run details for known ID

- Mock `IApplicationDbContext.AutomationRuns` to return an entity with a known `Id`.
- Send `GET /api/automation/runs/{knownId}`.
- Assert `200 OK` and response body contains the run ID and status.

#### Test: GET /api/automation/runs/{id} returns 404 for unknown ID

- Mock `IApplicationDbContext.AutomationRuns` as empty or without the requested ID.
- Send `GET /api/automation/runs/{unknownId}`.
- Assert `404 Not Found`.

#### Test: POST /api/automation/trigger returns 429 if run already in progress

- Mock `IApplicationDbContext.AutomationRuns` to contain an `AutomationRun` with `Status == Running` and `TriggeredAt` within today.
- Send `POST /api/automation/trigger`.
- Assert `429 Too Many Requests` (or `409 Conflict` depending on convention -- see implementation notes).

#### Test: POST /api/automation/trigger returns 429 if triggered within 15 minutes

- Mock `IApplicationDbContext.AutomationRuns` to contain a `Completed` run where `CompletedAt` was 10 minutes ago.
- Send `POST /api/automation/trigger`.
- Assert `429 Too Many Requests`.

#### Test: POST /api/automation/trigger starts orchestrator and returns run ID

- Mock `IApplicationDbContext.AutomationRuns` as empty (no recent runs).
- Mock `IDailyContentOrchestrator.ExecuteAsync` to return a successful `AutomationRunResult`.
- Send `POST /api/automation/trigger`.
- Assert `202 Accepted` with a body containing the `RunId`.
- Verify `IDailyContentOrchestrator.ExecuteAsync` was called exactly once.

#### Test: GET /api/automation/config returns current settings

- No special mocking needed (reads from `IOptions<ContentAutomationOptions>`).
- Send `GET /api/automation/config`.
- Assert `200 OK` and body contains the expected default config values.

#### Test: PUT /api/automation/config updates settings

- Send `PUT /api/automation/config` with a JSON body like `{ "enabled": false, "cronExpression": "0 10 * * 1-5" }`.
- Assert `200 OK` or `204 No Content`.
- Send `GET /api/automation/config` again.
- Assert the updated values are reflected.

#### Test: POST /api/automation/trigger without API key returns 401

- Send `POST /api/automation/trigger` with no `X-Api-Key` header.
- Assert `401 Unauthorized`.

### Inner TestFactory Class

Follows the exact pattern from `ContentEngineEndpointsTests.TestFactory`:

```csharp
public class TestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ApiKey", TestApiKey);
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Host=localhost;Database=test_automation;Username=test;Password=test");

        builder.ConfigureTestServices(services =>
        {
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var svc in hostedServices)
                services.Remove(svc);
        });
    }
}
```

Per-test mock injection happens in `WithWebHostBuilder` calls, not in the factory -- this keeps test isolation clean so mock setups from one test do not bleed into another.

---

## Implementation Details

### 1. Endpoint Registration

**New file:** `src/PersonalBrandAssistant.Api/Endpoints/AutomationEndpoints.cs`

This file contains a single static class `AutomationEndpoints` with the extension method `MapAutomationEndpoints`. The pattern mirrors every other endpoint file in the project (e.g., `ContentPipelineEndpoints`, `SchedulingEndpoints`).

The endpoint group is:
```csharp
var group = app.MapGroup("/api/automation").WithTags("Automation");
```

Five route registrations:
```csharp
group.MapGet("/runs", ListRuns);
group.MapGet("/runs/{id:guid}", GetRun);
group.MapPost("/trigger", TriggerRun);
group.MapGet("/config", GetConfig);
group.MapPut("/config", UpdateConfig);
```

### 2. ListRuns Endpoint

**Route:** `GET /api/automation/runs`

**Query parameters:**
- `int limit = 20` -- clamped to `[1, 100]`

**Implementation:**
- Inject `IApplicationDbContext db`.
- Query `db.AutomationRuns` ordered by `TriggeredAt` descending, take `limit`.
- Project to an anonymous type with all relevant fields: `Id`, `TriggeredAt`, `Status` (as string), `PrimaryContentId`, `ImageFileId`, `DurationMs`, `PlatformVersionCount`, `CompletedAt`, `ErrorDetails`.
- Return `Results.Ok(runs)`.

No `Result<T>` wrapping needed here -- this is a direct query with no business logic failure mode. An empty list is a valid success response.

### 3. GetRun Endpoint

**Route:** `GET /api/automation/runs/{id:guid}`

**Implementation:**
- Inject `IApplicationDbContext db`.
- `FindAsync(id)` on `db.AutomationRuns`.
- If null, return `Results.NotFound()`.
- Otherwise project to an anonymous type including all fields: `Id`, `TriggeredAt`, `Status`, `SelectedSuggestionId`, `PrimaryContentId`, `ImageFileId`, `ImagePrompt`, `SelectionReasoning`, `ErrorDetails`, `CompletedAt`, `DurationMs`, `PlatformVersionCount`.
- Return `Results.Ok(run)`.

### 4. TriggerRun Endpoint

**Route:** `POST /api/automation/trigger`

This is the most complex endpoint. It must enforce rate limiting and concurrency guards before invoking the orchestrator.

**Implementation:**
- Inject `IApplicationDbContext db`, `IDailyContentOrchestrator orchestrator`, `IOptions<ContentAutomationOptions> options`.
- **Guard 1 -- Already running:** Query `db.AutomationRuns` for any run with `Status == AutomationRunStatus.Running`. If found, return `Results.Problem` with status `429` and detail "A pipeline run is already in progress."
- **Guard 2 -- Rate limit (15 min cooldown):** Query `db.AutomationRuns` for the most recent completed run. If `CompletedAt` is within the last 15 minutes (`DateTimeOffset.UtcNow - CompletedAt < TimeSpan.FromMinutes(15)`), return `Results.Problem` with status `429` and detail "A pipeline run was completed recently. Please wait before triggering another."
- **Execute:** Call `orchestrator.ExecuteAsync(options.Value, CancellationToken)`.
- Return `Results.Accepted($"/api/automation/runs/{result.RunId}", new { result.RunId, result.Success })`.

**Important design note:** The orchestrator `ExecuteAsync` is synchronous (it runs the full pipeline inline). For a dashboard "trigger" action, this means the HTTP request will block until the pipeline completes (could be 2+ minutes with image generation). Two options:

**Option A (simpler, recommended for MVP):** Run inline. The 202 response is slightly misleading since it waits, but the automation run ID is always returned. The dashboard can show a spinner.

**Option B (better UX, more complex):** Fire-and-forget via `Task.Run` or a channel/queue. Return 202 immediately with a run ID. The dashboard polls `GET /runs/{id}` for status. This requires the orchestrator to create the `AutomationRun` record with `Running` status before returning, then update it asynchronously.

Use **Option A** for now. If the blocking becomes a UX issue, the trigger endpoint can be refactored to fire-and-forget without changing the API contract (the 202 + run ID response stays the same).

### 5. GetConfig Endpoint

**Route:** `GET /api/automation/config`

**Implementation:**
- Inject `IOptions<ContentAutomationOptions> options`.
- Return `Results.Ok(new { ... })` with all config values projected to a JSON-friendly shape:
  - `cronExpression`, `timeZone`, `enabled`, `autonomyLevel`, `topTrendsToConsider`, `targetPlatforms`
  - Nested `imageGeneration`: `enabled`, `comfyUiBaseUrl`, `timeoutSeconds`, `defaultWidth`, `defaultHeight`, `modelCheckpoint`, `circuitBreakerThreshold`
  - Nested `platformPrompts`: `linkedIn`, `twitterX`, `personalBlog`

Property names should use camelCase in the JSON output, which is the default for `System.Text.Json` in ASP.NET Core.

### 6. UpdateConfig Endpoint

**Route:** `PUT /api/automation/config`

**Request body record:**

```csharp
public record UpdateAutomationConfigRequest(
    bool? Enabled,
    string? CronExpression,
    string? TimeZone,
    string? AutonomyLevel,
    int? TopTrendsToConsider,
    string[]? TargetPlatforms,
    UpdateImageGenerationConfigRequest? ImageGeneration);

public record UpdateImageGenerationConfigRequest(
    bool? Enabled,
    string? ComfyUiBaseUrl,
    int? TimeoutSeconds);
```

**Implementation:**
- Inject `IOptionsMonitor<ContentAutomationOptions> optionsMonitor` (monitor, not snapshot -- allows runtime updates).
- Validate input: if `CronExpression` is provided, attempt to parse it with `Cronos.CronExpression.Parse()`. On failure, return `Results.BadRequest("Invalid cron expression.")`.
- If `TimeZone` is provided, validate with `TimeZoneInfo.FindSystemTimeZoneById()`. On failure, return `Results.BadRequest("Invalid timezone.")`.
- If `TopTrendsToConsider` is provided, clamp to `[1, 20]`.

**Runtime config update approach:** The simplest approach is to write updates to a well-known file (e.g., `appsettings.overrides.json`) that the configuration system watches via `reloadOnChange: true`. This avoids database storage for settings while still supporting runtime changes.

Alternatively, store the overrides in the database (add a `AutomationSettings` table or use the existing `TrendSettings` pattern). For MVP, the file-based approach is sufficient.

Return `Results.NoContent()` on success.

### 7. Request/Response Records

Define these at the bottom of `AutomationEndpoints.cs` (following the pattern in `TrendEndpoints.cs` where `CreateSourceRequest`, `AddKeywordRequest`, etc. are defined in the same file):

```csharp
public record UpdateAutomationConfigRequest(
    bool? Enabled,
    string? CronExpression,
    string? TimeZone,
    string? AutonomyLevel,
    int? TopTrendsToConsider,
    string[]? TargetPlatforms,
    UpdateImageGenerationConfigRequest? ImageGeneration);

public record UpdateImageGenerationConfigRequest(
    bool? Enabled,
    string? ComfyUiBaseUrl,
    int? TimeoutSeconds);
```

### 8. Program.cs Registration

**File to modify:** `src/PersonalBrandAssistant.Api/Program.cs`

Add a single line after the existing endpoint registrations (around line 101):

```csharp
app.MapAutomationEndpoints();
```

---

## Key Patterns from the Existing Codebase

These patterns must be followed for consistency:

1. **Static extension method** on `IEndpointRouteBuilder` for the `Map*Endpoints()` entry point.
2. **Private static async handler methods** within the same static class (e.g., `ListRuns`, `GetRun`, `TriggerRun`).
3. **DI injection via method parameters** -- ASP.NET Minimal APIs resolve services from the parameter list automatically.
4. **No MediatR for simple queries** -- endpoints like `ListRuns` and `GetRun` that are simple DB queries use `IApplicationDbContext` directly (same as `TrendEndpoints.GetSources`, `TrendEndpoints.GetSavedItems`). MediatR is used for commands with validation (like `CreateContent`).
5. **Anonymous types for JSON responses** -- project to `new { ... }` rather than creating dedicated response DTOs (consistent with `TrendEndpoints` projections).
6. **`Results.Problem` for error responses** -- use `Results.Problem(statusCode: 429, detail: "...")` for rate limit violations. This integrates with the standard `ProblemDetails` middleware.
7. **API key authentication** -- all endpoints are automatically protected by the `ApiKeyMiddleware` that checks the `X-Api-Key` header. No per-endpoint auth configuration needed.

---

## File Summary

### New Files

| File | Layer | Purpose |
|------|-------|---------|
| `src/PersonalBrandAssistant.Api/Endpoints/AutomationEndpoints.cs` | API | Five automation endpoints + request records |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AutomationEndpointsTests.cs` | Tests | Integration tests for all endpoints |

### Modified Files

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Api/Program.cs` | Add `app.MapAutomationEndpoints();` after line ~101 |

---

## Verification

After implementation, run:

```bash
cd C:/Users/kruz7/OneDrive/Documents/Code\ Repos/MCKRUZ/personal-brand-assistant
dotnet build
dotnet test --filter "FullyQualifiedName~AutomationEndpoints"
```

All tests should pass. Manually verify via Swagger UI that the five endpoints appear under the "Automation" tag at `https://localhost:{port}/swagger`.