# Section 05: Feed API Endpoints

## Overview

This section creates the `FeedEndpoints.cs` file that registers all Feed HTTP routes and wires them to the MediatR queries and commands built in sections 03 and 04. It also defines the `ListFeedQueryParams` record used for query string binding on the list endpoint, and registers the endpoint group in `Program.cs`.

## Dependencies

- **Section 03 (Feed Queries):** `ListFeedItems.Query`, `GetFeedSummary.Query`, `GetTrendingTopics.Query` must exist
- **Section 04 (Feed Commands):** `MarkFeedItemRead.Command`, `ActOnFeedItem.Command`, `BatchMarkRead.Command`, `BatchDismiss.Command`, `BatchAct.Command` must exist
- **Section 02 (DTOs/Validators):** `ActOnFeedItemRequest`, `BatchReadRequest`, `BatchDismissRequest`, `BatchActRequest` request DTOs must exist

## Tests

Per the TDD plan, this section has no dedicated endpoint tests. The endpoint layer is thin -- it delegates entirely to MediatR and maps results via `ToApiResult()`. Endpoint registration correctness is verified by backend integration tests in section 15.

## Route Table

| Method | Route | Handler | Body / Params |
|--------|-------|---------|---------------|
| GET | `/api/feed` | `ListFeedItems.Query` | Query string via `[AsParameters] ListFeedQueryParams` |
| GET | `/api/feed/summary` | `GetFeedSummary.Query` | None |
| GET | `/api/feed/trending` | `GetTrendingTopics.Query` | None |
| PUT | `/api/feed/{id:guid}/read` | `MarkFeedItemRead.Command` | Route `id` only |
| PUT | `/api/feed/{id:guid}/act` | `ActOnFeedItem.Command` | `ActOnFeedItemRequest` body |
| PUT | `/api/feed/batch/read` | `BatchMarkRead.Command` | `BatchReadRequest` body |
| PUT | `/api/feed/batch/dismiss` | `BatchDismiss.Command` | `BatchDismissRequest` body |
| PUT | `/api/feed/batch/act` | `BatchAct.Command` | `BatchActRequest` body |

## Implementation

### File: `src/PBA.Api/Endpoints/FeedEndpoints.cs`

Create a static class `FeedEndpoints` with a single extension method `MapFeedEndpoints(this IEndpointRouteBuilder app)`. Follow the exact pattern established in `ContentEndpoints.cs` and `IdeaEndpoints.cs`:

- Create a route group: `app.MapGroup("/api/feed").WithTags("Feed")`
- Each endpoint is a lambda that injects `ISender` and `CancellationToken`
- All results pass through `result.ToApiResult()` from `PBA.Api.Extensions`
- Use `[AsParameters]` binding for the GET list endpoint's query string record

#### Query String Record

Define `ListFeedQueryParams` in the same file (bottom of file, outside the class -- matches existing pattern in `ContentEndpoints.cs` and `IdeaEndpoints.cs`):

```csharp
public record ListFeedQueryParams
{
    public FeedItemType? Type { get; init; }
    public FeedItemPriority? Priority { get; init; }
    public bool? IsRead { get; init; }
    public bool IncludeExpired { get; init; } = false;
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
```

#### GET /api/feed (List)

Bind `[AsParameters] ListFeedQueryParams p`. Map to `ListFeedItems.Query` with:
- `Page = p.Page ?? 1`
- `PageSize = Math.Clamp(p.PageSize ?? 20, 1, 100)` -- clamping prevents abuse and matches the Content/Idea endpoint pattern
- All other fields passed through directly
- `SortBy = p.SortBy ?? "CreatedAt"`, `SortDirection = p.SortDirection ?? "desc"` -- defaults applied here at the endpoint layer, same as `IdeaEndpoints.cs` line 33-34

#### GET /api/feed/summary

No parameters. Send `new GetFeedSummary.Query()`, return `result.ToApiResult()`.

#### GET /api/feed/trending

No parameters. Send `new GetTrendingTopics.Query()`, return `result.ToApiResult()`.

#### PUT /api/feed/{id:guid}/read

Route parameter `Guid id`. Send `new MarkFeedItemRead.Command(id)`, return `result.ToApiResult()`.

#### PUT /api/feed/{id:guid}/act

Route parameter `Guid id`, body `ActOnFeedItemRequest body`. Map to `ActOnFeedItem.Command` with the id and `body.Action` (plus `body.AdditionalData` if the command accepts it). Return `result.ToApiResult()`.

#### PUT /api/feed/batch/read

Body `BatchReadRequest body`. Map to `BatchMarkRead.Command` with `body.Type` and `body.IsRead`. Return `result.ToApiResult()`.

#### PUT /api/feed/batch/dismiss

Body `BatchDismissRequest body`. Map to `BatchDismiss.Command` with `body.Type`. Return `result.ToApiResult()`.

#### PUT /api/feed/batch/act

Body `BatchActRequest body`. Map to `BatchAct.Command` with `body.Ids` and `body.Action`. Return `result.ToApiResult()`.

### File: `src/PBA.Api/Program.cs`

Add one line after the existing endpoint registrations (after `app.MapContentEndpoints();`):

```csharp
app.MapFeedEndpoints();
```

This follows the existing pattern where each feature's endpoints are registered as a single method call. The using directive `using PBA.Api.Endpoints;` is already present in Program.cs (line 4).

### Required Using Directives for FeedEndpoints.cs

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PBA.Api.Extensions;
using PBA.Application.Features.Feed.Commands;
using PBA.Application.Features.Feed.Dtos;
using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Enums;
```

## Conventions to Follow

These conventions are established by the existing endpoint files and must be preserved:

1. **Static class, extension method pattern** -- `public static class FeedEndpoints` with `public static void MapFeedEndpoints(this IEndpointRouteBuilder app)`
2. **Route group with tag** -- `app.MapGroup("/api/feed").WithTags("Feed")`
3. **CancellationToken on every handler** -- always the last parameter
4. **ISender not IMediator** -- use `ISender sender` for dispatching, consistent with existing endpoints
5. **ToApiResult() for all responses** -- never manually construct Results.Ok/BadRequest/NotFound in the endpoint lambda. The `ResultExtensions` class handles all failure type mapping centrally
6. **Query param record at file bottom** -- `ListFeedQueryParams` goes after the class, outside the namespace is NOT the pattern (existing files keep records inside the namespace)
7. **No try/catch in endpoints** -- error handling is done by the MediatR pipeline and Result pattern. The endpoint layer is purely a routing/mapping concern
8. **Nullable query params with defaults applied in mapping** -- `Page` and `PageSize` are nullable in the record, with defaults applied when constructing the query (`p.Page ?? 1`, `Math.Clamp(p.PageSize ?? 20, 1, 100)`)

## Batch Endpoint Ordering Note

The batch routes (`/api/feed/batch/read`, `/api/feed/batch/dismiss`, `/api/feed/batch/act`) must be registered BEFORE the parameterized routes (`/api/feed/{id:guid}/read`, `/api/feed/{id:guid}/act`). ASP.NET Core minimal API route matching is order-independent for typed constraints like `{id:guid}` (the string "batch" won't match `guid`), so this is not strictly required for correctness -- but ordering batch routes first improves readability and makes the intent clear. Either ordering will work; the `guid` constraint prevents ambiguity.

## Verification

After implementation, the following should hold:

1. `dotnet build` succeeds with no warnings in `PBA.Api`
2. All 8 routes are registered and reachable (verified manually or via integration tests in section 15)
3. `Program.cs` has `app.MapFeedEndpoints();` called
4. The endpoint file is under 100 lines (the layer is intentionally thin)

## Implementation Notes

### Files Created/Modified
- `src/PBA.Api/Endpoints/FeedEndpoints.cs` (new, 91 lines) -- all 8 routes + ListFeedQueryParams record
- `src/PBA.Api/Program.cs` (line 46) -- added `app.MapFeedEndpoints();`
- `src/PBA.Application/Features/Feed/Validators/BatchActRequestValidator.cs` -- added max 100 items limit with null guard
- `tests/PBA.Application.Tests/Features/Feed/Validators/ActOnFeedItemRequestValidatorTests.cs` -- removed stale "edit"/"schedule" test data

### Deviations from Plan
- **BatchReadRequest.IsRead not mapped:** Plan said to map `body.IsRead` but `BatchMarkRead.Command` only accepts `FeedItemType? Type`. Field kept on DTO for future batch-unread feature.
- **ActOnFeedItemRequest.AdditionalData not mapped:** Command signature is `(Guid Id, string Action)` only. Field kept on DTO for future action context.
- **BatchActRequest.Ids count limit added:** Code review identified DoS vector. Added `.Must(ids => ids is null || ids.Count <= 100)` to validator.
- **Pre-existing test failures fixed:** Section-04 review trimmed KnownActions but tests still expected "edit" and "schedule" as valid actions.

### Test Results
All 404 tests passing (280 Application + 76 Infrastructure + 48 Api).
