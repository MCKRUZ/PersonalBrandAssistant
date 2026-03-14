# Section 08 -- API Endpoints

## Overview

This section covers all new Minimal API endpoint groups for the Workflow & Approval Engine, the SignalR hub mapping, and the required changes to `Program.cs` and Infrastructure DI registration. By the end of this section, every service built in sections 04 through 06 will be exposed via HTTP endpoints, and all new services will be wired into the dependency injection container.

## Dependencies

- **Section 04 (Approval Service):** `IApprovalService` interface and implementation must exist.
- **Section 05 (Content Scheduler):** `IContentScheduler` interface and implementation must exist.
- **Section 06 (Notification System):** `INotificationService` interface and implementation, `NotificationHub`, and `WorkflowChannelService` must exist.
- **Section 03 (Workflow Engine):** `IWorkflowEngine` interface and implementation must exist.
- **Section 01 (Domain Entities):** All new entities, enums, and domain events must exist.
- **Section 02 (Autonomy Configuration):** `AutonomyConfiguration` entity and resolution logic must exist.
- **Section 07 (Background Processors):** All `BackgroundService` processors and `IPublishingPipeline` stub must exist for DI registration.

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Api/Endpoints/WorkflowEndpoints.cs` | Workflow and autonomy configuration endpoints |
| `src/PersonalBrandAssistant.Api/Endpoints/ApprovalEndpoints.cs` | Approval workflow endpoints |
| `src/PersonalBrandAssistant.Api/Endpoints/SchedulingEndpoints.cs` | Content scheduling endpoints |
| `src/PersonalBrandAssistant.Api/Endpoints/NotificationEndpoints.cs` | Notification management endpoints |

## Files to Modify

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Api/Program.cs` | Add SignalR, map new endpoint groups, map SignalR hub |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Register all new services and hosted services |
| `src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs` | No changes needed -- SignalR auth handled in hub `OnConnectedAsync`, not middleware exemption |

## Tests First

All tests live in `tests/PersonalBrandAssistant.Infrastructure.Tests/` using the existing `CustomWebApplicationFactory` pattern. Each test class sends real HTTP requests through the full middleware pipeline. The test API key `"test-api-key-12345"` is set in `CustomWebApplicationFactory` and must be sent via the `X-Api-Key` header using `CreateAuthenticatedClient()`.

### WorkflowEndpoints Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/WorkflowEndpointsTests.cs`

Test stubs:

- **PUT /api/workflow/autonomy updates configuration and returns 200** -- Send a PUT with a body containing `GlobalLevel`, optional `ContentTypeOverrides`, `PlatformOverrides`, `ContentTypePlatformOverrides`. Assert 200 and that the returned config reflects the update.
- **GET /api/workflow/autonomy returns current configuration** -- Seed a configuration, GET and assert the response matches.
- **GET /api/workflow/autonomy/resolve returns resolved level** -- Send query params `contentType` and `platform`, assert the resolved `AutonomyLevel` is returned.
- **POST /api/workflow/{id}/transition performs transition and returns 200** -- Create content in Draft status, POST a transition to Review, assert 200.
- **POST /api/workflow/{id}/transition returns 400 for invalid transition** -- Create content in Draft status, POST a transition to Published (invalid), assert 400 with ProblemDetails.
- **GET /api/workflow/{id}/transitions returns allowed transitions** -- Create content in a known status, GET allowed transitions, assert the array matches expected transitions.
- **GET /api/workflow/audit returns filtered audit entries** -- Perform some transitions, then GET audit with `contentId` filter, assert entries are returned in descending timestamp order.
- **All endpoints require API key** -- Send any request without `X-Api-Key` header, assert 401.

### ApprovalEndpoints Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ApprovalEndpointsTests.cs`

Test stubs:

- **GET /api/approval/pending returns Review-status content** -- Create content in Review status, GET pending, assert the content appears in results.
- **POST /api/approval/{id}/approve transitions and returns 200** -- Create content in Review status, POST approve, assert 200 and content is now Approved.
- **POST /api/approval/{id}/reject with feedback transitions to Draft** -- Create content in Review, POST reject with `{ "feedback": "Needs work" }`, assert 200 and content returns to Draft.
- **POST /api/approval/batch-approve approves multiple and returns count** -- Create multiple Review-status content items, POST batch-approve with their IDs, assert the returned count matches.

### SchedulingEndpoints Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SchedulingEndpointsTests.cs`

Test stubs:

- **POST /api/scheduling/{id}/schedule sets schedule and returns 200** -- Create Approved content, POST schedule with a future `scheduledAt`, assert 200.
- **PUT /api/scheduling/{id}/reschedule updates timing** -- Schedule content, PUT reschedule with a new time, assert 200.
- **DELETE /api/scheduling/{id} cancels schedule and returns to Approved** -- Schedule content, DELETE, assert content returns to Approved status with `ScheduledAt` cleared.

### NotificationEndpoints Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/NotificationEndpointsTests.cs`

Test stubs:

- **GET /api/notifications returns paginated list** -- Create several notifications, GET with `pageSize`, assert correct pagination with cursor.
- **GET /api/notifications?isRead=false returns only unread** -- Create mix of read/unread notifications, GET with `isRead=false`, assert only unread returned.
- **POST /api/notifications/{id}/read marks as read** -- Create notification, POST read, assert `IsRead` becomes true.
- **POST /api/notifications/read-all marks all as read** -- Create multiple unread notifications, POST read-all, assert all are marked read.

### SignalR Auth Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SignalRAuthTests.cs`

Test stubs:

- **SignalR negotiation requires API key in query string** -- Attempt to connect to `/hubs/notifications?apiKey={TestApiKey}`, assert connection succeeds.
- **SignalR rejects connection without valid API key** -- Attempt to connect to `/hubs/notifications` without the query parameter, assert connection is rejected.

## Implementation Details

### Existing Patterns to Follow

All endpoints follow the Minimal API pattern established in `ContentEndpoints.cs`:

1. A static class with a `Map*Endpoints` extension method on `IEndpointRouteBuilder`.
2. Endpoints grouped under a route prefix with `.WithTags()` for Swagger.
3. Each handler is a private static async method that receives `ISender` (MediatR) plus route/query/body params.
4. Results are converted via `result.ToHttpResult()` or `result.ToCreatedHttpResult()` extension methods from `ResultExtensions.cs`.
5. The `Result<T>` pattern maps `ErrorCode.ValidationFailed` to 400, `ErrorCode.NotFound` to 404, `ErrorCode.Conflict` to 409, `ErrorCode.Unauthorized` to 401, and all others to 500.

### WorkflowEndpoints

File: `src/PersonalBrandAssistant.Api/Endpoints/WorkflowEndpoints.cs`

Route group: `/api/workflow` with tag `"Workflow"`.

Endpoints:

- `PUT /autonomy` -- Receives `ConfigureAutonomyCommand` from body, sends via MediatR.
- `GET /autonomy` -- Sends `GetAutonomyConfigurationQuery`, returns the configuration DTO.
- `GET /autonomy/resolve` -- Receives `contentType` (required) and `platform` (optional) as query params, builds `ResolveAutonomyLevelQuery`, returns the resolved level.
- `POST /{id:guid}/transition` -- Receives a body with `TargetStatus` and optional `Reason`. Builds `TransitionContentCommand(id, targetStatus, reason)`, sends via MediatR.
- `GET /{id:guid}/transitions` -- Sends `GetAllowedTransitionsQuery(id)` (or calls `IWorkflowEngine.GetAllowedTransitionsAsync` via a query handler), returns array of `ContentStatus`.
- `GET /audit` -- Receives optional query params `contentId`, `dateFrom`, `dateTo`, `pageSize`, `cursor`. Queries `WorkflowTransitionLog` entries. Returns paginated results.

### ApprovalEndpoints

File: `src/PersonalBrandAssistant.Api/Endpoints/ApprovalEndpoints.cs`

Route group: `/api/approval` with tag `"Approval"`.

Endpoints:

- `GET /pending` -- Receives optional `contentType`, `platform`, `pageSize`, `cursor` as query params. Sends `ListPendingContentQuery`. Returns paginated content in Review status.
- `POST /{id:guid}/approve` -- Sends `ApproveContentCommand(id)`. Returns 200 on success.
- `POST /{id:guid}/reject` -- Receives body with `Feedback` (required string). Sends `RejectContentCommand(id, feedback)`. Returns 200 on success.
- `POST /batch-approve` -- Receives body with `ContentIds` (Guid array). Sends `BatchApproveContentCommand(contentIds)`. Returns 200 with the count of successfully approved items.

### SchedulingEndpoints

File: `src/PersonalBrandAssistant.Api/Endpoints/SchedulingEndpoints.cs`

Route group: `/api/scheduling` with tag `"Scheduling"`.

Endpoints:

- `POST /{id:guid}/schedule` -- Receives body with `ScheduledAt` (DateTimeOffset). Sends `ScheduleContentCommand(id, scheduledAt)`. Returns 200.
- `PUT /{id:guid}/reschedule` -- Receives body with `ScheduledAt` (DateTimeOffset). Sends `RescheduleContentCommand(id, scheduledAt)`. Returns 200.
- `DELETE /{id:guid}` -- Sends `CancelScheduleCommand(id)`. Returns 200 (or 204).

### NotificationEndpoints

File: `src/PersonalBrandAssistant.Api/Endpoints/NotificationEndpoints.cs`

Route group: `/api/notifications` with tag `"Notifications"`.

Endpoints:

- `GET /` -- Receives optional `isRead` (bool), `pageSize` (int, default 20), `cursor` (string) as query params. Sends `ListNotificationsQuery(isRead, pageSize, cursor)`. Returns paginated notification list.
- `POST /{id:guid}/read` -- Sends `MarkNotificationReadCommand(id)`. Returns 200.
- `POST /read-all` -- Sends `MarkAllNotificationsReadCommand`. Returns 200.

### SignalR Hub Mapping

The `NotificationHub` (created in section 06) is mapped after the endpoint registrations in `Program.cs`:

```csharp
app.MapHub<NotificationHub>("/hubs/notifications");
```

The hub is NOT exempted from the `ApiKeyMiddleware`. Instead, the hub validates the API key from the query string in its `OnConnectedAsync` override. The middleware will see the `/hubs/notifications` request and check for the `X-Api-Key` header. Since SignalR negotiation is an HTTP request, the client must either pass the key as a header or the middleware needs to also check query string for hub paths. The recommended approach: modify the `ApiKeyMiddleware` to also check for `apiKey` query parameter when the `X-Api-Key` header is absent, but only for paths starting with `/hubs/`. This keeps header-based auth for REST and allows query-string auth for SignalR (where custom headers are not supported during WebSocket upgrade).

Specifically, update `ApiKeyMiddleware.InvokeAsync` to add this fallback logic:

```csharp
// In ApiKeyMiddleware, after checking header:
if (!hasHeaderKey && context.Request.Path.StartsWithSegments("/hubs"))
{
    var queryKey = context.Request.Query["apiKey"].FirstOrDefault();
    if (queryKey is not null && IsValidKey(queryKey))
    {
        await _next(context);
        return;
    }
}
```

This is a small, focused change to `ApiKeyMiddleware` -- the only modification to an existing file beyond `Program.cs` and `DependencyInjection.cs`.

### Program.cs Changes

File: `src/PersonalBrandAssistant.Api/Program.cs`

Add the following changes in order:

1. **Add SignalR services** -- After `builder.Services.AddInfrastructure(...)`:
   ```csharp
   builder.Services.AddSignalR();
   ```

2. **Update CORS policy** -- The existing `AllowAngularDev` CORS policy needs `.AllowCredentials()` for SignalR (SignalR requires credentials for WebSocket connections). This replaces the current `AllowAnyMethod()` chain:
   ```csharp
   policy.WithOrigins("http://localhost:4200")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials();
   ```

3. **Map new endpoint groups** -- After the existing `app.MapContentEndpoints()`:
   ```csharp
   app.MapWorkflowEndpoints();
   app.MapApprovalEndpoints();
   app.MapSchedulingEndpoints();
   app.MapNotificationEndpoints();
   ```

4. **Map SignalR hub** -- After all endpoint mappings:
   ```csharp
   app.MapHub<NotificationHub>("/hubs/notifications");
   ```

### Infrastructure DependencyInjection Changes

File: `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Add the following registrations inside `AddInfrastructure()`, after the existing service registrations:

```csharp
// Workflow & Approval Engine services (scoped)
services.AddScoped<IWorkflowEngine, WorkflowEngine>();
services.AddScoped<IApprovalService, ApprovalService>();
services.AddScoped<IContentScheduler, ContentScheduler>();
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<IPublishingPipeline, PublishingPipelineStub>();

// Channel service (singleton -- shared across scopes)
services.AddSingleton<WorkflowChannelService>();

// Background processors (hosted services)
services.AddHostedService<ScheduledPublishProcessor>();
services.AddHostedService<RetryFailedProcessor>();
services.AddHostedService<WorkflowRehydrator>();
```

This requires adding the appropriate `using` statements for all new service interfaces and implementations.

### CustomWebApplicationFactory Updates

The `CustomWebApplicationFactory` in tests will need to remove the new background services to prevent them from running during API tests (same pattern as the existing `DataSeeder` and `AuditLogCleanupService` removals):

```csharp
RemoveService<ScheduledPublishProcessor>(services);
RemoveService<RetryFailedProcessor>(services);
RemoveService<WorkflowRehydrator>(services);
```

This change is needed for API endpoint tests to run cleanly without background processors interfering.

### Request/Response DTOs

Each endpoint that accepts a request body should have a minimal record DTO. These can be defined as nested types in the endpoint class or as separate files in a `Contracts` folder. Following the lightweight Minimal API pattern, define them inline as records:

- `TransitionRequest(ContentStatus TargetStatus, string? Reason)` -- for POST /api/workflow/{id}/transition
- `RejectRequest(string Feedback)` -- for POST /api/approval/{id}/reject
- `BatchApproveRequest(Guid[] ContentIds)` -- for POST /api/approval/batch-approve
- `ScheduleRequest(DateTimeOffset ScheduledAt)` -- for POST and PUT scheduling endpoints

These request records are distinct from MediatR commands. The endpoint handler maps the request DTO + route params into the MediatR command. This keeps the API contract decoupled from the application layer.

## Implementation Checklist

1. Create `WorkflowEndpoints.cs` with all six endpoints following the `ContentEndpoints` pattern.
2. Create `ApprovalEndpoints.cs` with all four endpoints.
3. Create `SchedulingEndpoints.cs` with all three endpoints.
4. Create `NotificationEndpoints.cs` with all three endpoints.
5. Update `ApiKeyMiddleware.cs` to support query-string API key for `/hubs/` paths.
6. Update `DependencyInjection.cs` to register all new services, channels, and hosted services.
7. Update `Program.cs` to add SignalR, map new endpoints, map hub, update CORS.
8. Update `CustomWebApplicationFactory` to remove new background services.
9. Write all API endpoint tests using `CustomWebApplicationFactory.CreateAuthenticatedClient()`.
10. Write SignalR auth tests using the `Microsoft.AspNetCore.SignalR.Client` test client.