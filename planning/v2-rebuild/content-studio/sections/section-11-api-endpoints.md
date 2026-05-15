# Section 11: API Endpoints

## Overview

This section creates the `ContentEndpoints` static class with all 16 routes for the Content Studio, registers it in `Program.cs`, and adds integration tests using `WebApplicationFactory`. The endpoints follow the exact same Minimal API + MediatR pattern established by `IdeaEndpoints` in Step 2: thin route handlers that map HTTP requests to MediatR commands/queries, then convert `Result<T>` to HTTP responses via `ToApiResult()`.

## Dependencies

This section depends on:
- **Section 01** (Schema Updates) -- Content entity with `HangfireJobId`, `IsDeleted`, `Children` properties
- **Section 03** (DTOs and Validators) -- All request/response DTOs
- **Section 04** (Query Handlers) -- `ListContent.Query`, `GetContent.Query`, `CheckVoice.Query`
- **Section 05** (Core Commands) -- `CreateContent.Command`, `UpdateContent.Command`, `DeleteContent.Command`, `DraftContent.Command`, `GenerateCrossPost.Command`
- **Section 06** (Status Commands) -- `ApproveContent.Command`, `SubmitForReviewContent.Command`, `RequestChangesContent.Command`, `UnpublishContent.Command`, `RestoreContent.Command`
- **Section 10** (Hangfire Scheduling) -- `ScheduleContent.Command`, `UnscheduleContent.Command`, `PublishContent.Command`

This section blocks:
- **Section 12** (Angular Models and Services) -- the Angular HTTP client maps to these endpoints

## Infrastructure Prerequisite: ResultFailureType.Conflict

The `UpdateContent` command handler returns a `Result<T>.Fail(...)` when optimistic concurrency fails (stale `LastUpdatedAt`). The current `ResultFailureType` enum does NOT include a `Conflict` variant. Before implementing endpoints, extend both:

**File:** `src/PBA.Domain/Common/Result.cs`

Add `Conflict` to the `ResultFailureType` enum and add factory methods:

```csharp
// In ResultFailureType enum:
Conflict

// On Result class:
public static Result Conflict(string reason) => new(false, [reason], ResultFailureType.Conflict);

// On Result<T> class:
public new static Result<T> Conflict(string reason) => new(false, errors: [reason], failureType: ResultFailureType.Conflict);
```

**File:** `src/PBA.Api/Extensions/ResultExtensions.cs`

Add the mapping in `MapFailure`:

```csharp
ResultFailureType.Conflict => Results.Conflict(result.Errors.FirstOrDefault()),
```

---

## Tests FIRST

All integration tests live in `tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs`.

### Test File

**File:** `tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs`

```csharp
namespace PBA.Api.Tests.Endpoints;

public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    // Test: POST /api/content creates content and returns 201 with new GUID
    // Test: POST /api/content returns 400 for invalid input (empty title)
    // Test: GET /api/content returns 200 with paginated list
    // Test: GET /api/content respects query filters (status, platform, contentType, search)
    // Test: GET /api/content/{id} returns 200 with ContentDetailDto
    // Test: GET /api/content/{id} returns 404 for nonexistent ID
    // Test: PUT /api/content/{id} auto-saves body and returns 200
    // Test: PUT /api/content/{id} rejects stale LastUpdatedAt with 409
    // Test: PUT /api/content/{id} rejects save when status is Published (returns 400/error)
    // Test: DELETE /api/content/{id} soft-deletes (archives) and returns 200
    // Test: POST /api/content/{id}/draft calls sidecar and returns updated ContentDetailDto
    // Test: PUT /api/content/{id}/approve transitions Draft->Approved
    // Test: PUT /api/content/{id}/submit-review transitions Draft->Review
    // Test: PUT /api/content/{id}/request-changes transitions Review->Draft
    // Test: PUT /api/content/{id}/schedule sets ScheduledAt and creates Hangfire job
    // Test: PUT /api/content/{id}/unschedule cancels job and transitions Scheduled->Approved
    // Test: POST /api/content/{id}/publish transitions to Published
    // Test: PUT /api/content/{id}/unpublish transitions Published->Draft
    // Test: PUT /api/content/{id}/restore transitions Archived->Draft
    // Test: GET /api/content/{id}/voice-check returns VoiceCheckDto with score and feedback
    // Test: POST /api/content/{id}/cross-post creates child content and returns 201
    // Test: Full flow -- create -> draft -> approve -> publish -> verify published state
}
```

### TestWebApplicationFactory Updates

**File:** `tests/PBA.Api.Tests/TestWebApplicationFactory.cs`

Add mock registrations for:
- `ISidecarClient` -- mock `SendPromptAsync` to return a fixed AI response, mock `StreamPromptAsync` to yield test tokens
- `IBlogConnector` -- mock `PublishAsync` to return a fake URL
- `IBackgroundJobClient` (Hangfire) -- mock `Create`/`Delete` so scheduling tests don't need a real Hangfire server
- `IContentPublisher` -- mock if registered

### Key Test Patterns

Each test follows the established Idea Bank pattern:
1. **Arrange:** Create prerequisite entities via POST
2. **Act:** Call the endpoint under test via `HttpClient`
3. **Assert:** Verify HTTP status code and response body

For optimistic concurrency (409):
1. Create content (POST)
2. Get the content to capture `UpdatedAt` (GET)
3. Update content with correct `LastUpdatedAt` (PUT) -- 200
4. Update again with the OLD `LastUpdatedAt` -- 409

---

## Implementation

### ContentEndpoints

**File:** `src/PBA.Api/Endpoints/ContentEndpoints.cs`

Static class with `MapContentEndpoints` extension method. Route group: `/api/content`, tag: `"Content"`.

**All 16 routes:**

| HTTP Method | Route | MediatR Type | Request Body | Notes |
|---|---|---|---|---|
| GET | `/` | `ListContent.Query` | `[AsParameters] ListContentQueryParams` | Page defaults to 1, PageSize clamped 1-100 |
| GET | `/{id:guid}` | `GetContent.Query` | none | Returns `ContentDetailDto` |
| POST | `/` | `CreateContent.Command` | `CreateContentRequest` | Returns 201 with Location header |
| PUT | `/{id:guid}` | `UpdateContent.Command` | `UpdateContentRequest` | Maps `id` from route into command |
| DELETE | `/{id:guid}` | `DeleteContent.Command` | none | Soft delete (archives) |
| POST | `/{id:guid}/draft` | `DraftContent.Command` | `DraftContentRequest` | Returns updated `ContentDetailDto` |
| POST | `/{id:guid}/cross-post` | `GenerateCrossPost.Command` | `CrossPostRequest` | Returns 201 with child content ID |
| PUT | `/{id:guid}/approve` | `ApproveContent.Command` | none | Status transition |
| PUT | `/{id:guid}/submit-review` | `SubmitForReviewContent.Command` | none | Status transition |
| PUT | `/{id:guid}/request-changes` | `RequestChangesContent.Command` | none | Status transition |
| PUT | `/{id:guid}/schedule` | `ScheduleContent.Command` | `ScheduleContentRequest` | Schedules Hangfire job |
| PUT | `/{id:guid}/unschedule` | `UnscheduleContent.Command` | none | Cancels Hangfire job |
| POST | `/{id:guid}/publish` | `PublishContent.Command` | none | Immediate publish |
| PUT | `/{id:guid}/unpublish` | `UnpublishContent.Command` | none | Published->Draft |
| PUT | `/{id:guid}/restore` | `RestoreContent.Command` | none | Archived->Draft |
| GET | `/{id:guid}/voice-check` | `CheckVoice.Query` | none | Returns `VoiceCheckDto` |

**Endpoint handler pattern** (same as `IdeaEndpoints`):

Each handler:
1. Extract route parameters (`id`) and body (deserialized DTO)
2. Construct MediatR command/query, mapping route `id` to `ContentId`
3. Call `sender.Send(command, ct)`
4. Convert result to HTTP response via `result.ToApiResult()`

For POST endpoints that create resources:
```csharp
return result.IsSuccess
    ? Results.Created($"/api/content/{result.Value}", result.Value)
    : result.ToApiResult();
```

For DELETE, return `Results.NoContent()` on success.

### Program.cs Registration

**File:** `src/PBA.Api/Program.cs`

Add alongside existing endpoint registrations:

```csharp
app.MapContentEndpoints();
```

### CORS Note for SignalR

Section 08 (SignalR Hub) requires `.AllowCredentials()` on the CORS policy. Not required for ContentEndpoints alone, but called out since this section touches `Program.cs`.

---

## Actual File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/PBA.Api/Endpoints/ContentEndpoints.cs` | **Create** | All 16 content routes + `ListContentQueryParams` record |
| `src/PBA.Api/Program.cs` | **Modify** | Add `app.MapContentEndpoints()` |
| `src/PBA.Domain/Common/Result.cs` | **Modify** | Add `ResultFailureType.Conflict` and factory methods |
| `src/PBA.Api/Extensions/ResultExtensions.cs` | **Modify** | Add `Conflict => Results.Conflict(...)` mapping |
| `src/PBA.Application/Features/Content/Commands/PublishContent.cs` | **Create** | MediatR command for immediate publish (Approved → Published via PublishNow trigger) |
| `src/PBA.Application/Features/Content/Commands/UpdateContent.cs` | **Modify** | Changed concurrency failure from `Result.Fail` to `Result.Conflict` |
| `src/PBA.Application/Features/Content/Validators/CreateContentCommandValidator.cs` | **Create** | FluentValidation for CreateContent.Command (MediatR pipeline target) |
| `src/PBA.Application/Features/Content/Validators/CreateContentRequestValidator.cs` | **Delete** | Dead code — DTO validator never triggered by MediatR pipeline |
| `tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs` | **Create** | 21 integration tests covering all endpoints + full flow |
| `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` | **Modify** | Add mocks for ISidecarClient (JSON response), IBlogConnector, IContentPublisher |
| `tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs` | **Modify** | Retargeted to test CreateContentCommandValidator |

---

## Deviations from Plan

1. **PublishContent.Command created** — Plan listed it as dependency from section-10, but section-10 only created `ContentPublisher` (Infrastructure, for Hangfire). Created MediatR command using `ContentTrigger.PublishNow` (Approved → Published), distinct from `ContentTrigger.Publish` (Scheduled → Published).

2. **Dead DTO validator removed** — `CreateContentRequestValidator` validated the DTO type, but MediatR pipeline validates Command types. Replaced with `CreateContentCommandValidator`. Existing tests retargeted.

3. **UpdateContent.Command concurrency fix** — Changed `Result.Fail(...)` to `Result.Conflict(...)` for stale `LastUpdatedAt`, enabling proper 409 HTTP response.

4. **BlogConnector error handling** — Added try/catch in PublishContent.Command per code review (returns structured `Result.Fail` instead of 500).

5. **Tags query param** — `ListContentQueryParams` omits `[FromQuery(Name = "tags")]` on Tags since the current ListContent.Query doesn't support tag filtering.

## Implementation Notes

1. **Endpoints are thin.** Each route handler is 3-8 lines. All business logic lives in MediatR handlers.

2. **PageSize clamping** (`Math.Clamp(p.PageSize ?? 20, 1, 100)`) prevents abuse.

3. **JSON enum serialization** configured globally via `JsonStringEnumConverter` in Program.cs. Test client needs its own `JsonSerializerOptions` with the same converter.

4. **FluentValidation** runs via MediatR pipeline behavior on Command types, not DTO types.

5. **Test count:** 21 integration tests (all passing), 345 total across solution.
