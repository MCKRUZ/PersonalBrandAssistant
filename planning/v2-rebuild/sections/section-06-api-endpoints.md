# Section 06: API Endpoints

## Overview

Wire up Minimal API endpoint groups for Ideas and IdeaSources that route HTTP requests to MediatR handlers and translate `Result<T>` responses to HTTP status codes. Remove the existing inline stubs from `Program.cs` and replace with organized endpoint classes.

## Dependencies

- **Section 04** -- Query handlers (ListIdeas, GetIdea, GetIdeaConnections, ListIdeaSources)
- **Section 05** -- Command handlers (CreateIdea, SaveIdea, DismissIdea, IdeaSource CRUD, RefreshIdeaSources)
- **Section 01** -- `ToApiResult` extension method

## What Gets Built

| Component | Project | Path |
|-----------|---------|------|
| `IdeaEndpoints` | PBA.Api | `src/PBA.Api/Endpoints/IdeaEndpoints.cs` |
| `IdeaSourceEndpoints` | PBA.Api | `src/PBA.Api/Endpoints/IdeaSourceEndpoints.cs` |
| Program.cs updates | PBA.Api | `src/PBA.Api/Program.cs` (remove stubs, add endpoint registration) |
| Integration tests | Tests | `tests/PBA.Api.Tests/Endpoints/IdeaEndpointsTests.cs` |
| Integration tests | Tests | `tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs` |

---

## Background: Current Program.cs

The existing `Program.cs` has two inline endpoint stubs that must be removed:

```csharp
// REMOVE these:
app.MapGet("/api/ideas", async (ApplicationDbContext db) => { ... });
app.MapGet("/api/feed", async (ApplicationDbContext db) => { ... });
```

These are replaced by the endpoint groups that use MediatR.

The health endpoint (`/api/health`) stays as-is.

---

## Tests First

### IdeaEndpoints Integration Tests

**File:** `tests/PBA.Api.Tests/Endpoints/IdeaEndpointsTests.cs`

Integration tests use `WebApplicationFactory<Program>` with an in-memory or test database. The factory replaces the PostgreSQL connection with an in-memory provider or Testcontainers PostgreSQL.

```csharp
namespace PBA.Api.Tests.Endpoints;

public class IdeaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public IdeaEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace DbContext with InMemory or test PostgreSQL
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetIdeas_Returns200_WithPaginatedList()
    // GET /api/ideas => 200, body has items/totalCount/page/pageSize

    [Fact]
    public async Task GetIdeas_WithStatusFilter_FiltersCorrectly()
    // GET /api/ideas?status=Saved => only saved ideas

    [Fact]
    public async Task GetIdea_Returns200_WithDetailDto()
    // GET /api/ideas/{id} => 200, full IdeaDetailDto

    [Fact]
    public async Task GetIdea_Returns404_ForNonExistentId()
    // GET /api/ideas/{randomGuid} => 404

    [Fact]
    public async Task PostIdea_Returns201_WithNewId()
    // POST /api/ideas with valid body => 201

    [Fact]
    public async Task PostIdea_Returns400_ForInvalidInput()
    // POST /api/ideas with empty title => 400 with validation errors

    [Fact]
    public async Task PutIdeaSave_Returns200()
    // PUT /api/ideas/{id}/save with body => 200

    [Fact]
    public async Task PutIdeaDismiss_Returns200()
    // PUT /api/ideas/{id}/dismiss => 200

    [Fact]
    public async Task PostIdeaCreateContent_Returns201()
    // POST /api/ideas/{id}/create-content with body => 201

    [Fact]
    public async Task GetIdeaConnections_Returns200()
    // GET /api/ideas/connections => 200
}
```

### IdeaSourceEndpoints Integration Tests

**File:** `tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs`

```csharp
namespace PBA.Api.Tests.Endpoints;

public class IdeaSourceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetSources_Returns200_WithSourceList()
    // GET /api/idea-sources => 200

    [Fact]
    public async Task PostSource_Returns201()
    // POST /api/idea-sources with valid body => 201

    [Fact]
    public async Task PutSource_Returns200()
    // PUT /api/idea-sources/{id} with body => 200

    [Fact]
    public async Task DeleteSource_Returns204()
    // DELETE /api/idea-sources/{id} => 204

    [Fact]
    public async Task PostRefresh_Returns200_WithCount()
    // POST /api/idea-sources/refresh => 200 with count
}
```

### WebApplicationFactory Setup

The test project needs a custom `WebApplicationFactory` to replace the database. A minimal setup:

```csharp
// tests/PBA.Api.Tests/TestWebApplicationFactory.cs
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            // Add InMemory DbContext
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));
        });
    }
}
```

Note: For `Program` to be accessible from the test project, `PBA.Api` needs either:
- A `public partial class Program { }` declaration at the bottom of `Program.cs`, or
- An `InternalsVisibleTo` attribute in the csproj

Add to `src/PBA.Api/Program.cs` at the very end:
```csharp
public partial class Program { }
```

---

## Implementation Details

### IdeaEndpoints

**File:** `src/PBA.Api/Endpoints/IdeaEndpoints.cs`

```csharp
namespace PBA.Api.Endpoints;

public static class IdeaEndpoints
{
    public static void MapIdeaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ideas").WithTags("Ideas");

        // GET /api/ideas — list with filters
        group.MapGet("/", async (
            [AsParameters] ListIdeasQueryParams queryParams,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new ListIdeas.Query { /* map from queryParams */ };
            var result = await sender.Send(query, ct);
            return result.ToApiResult();
        });

        // GET /api/ideas/connections — AI connections (before {id} to avoid route conflict)
        group.MapGet("/connections", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetIdeaConnections.Query(), ct);
            return result.ToApiResult();
        });

        // GET /api/ideas/{id} — single idea detail
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetIdea.Query(id), ct);
            return result.ToApiResult();
        });

        // POST /api/ideas — create manual idea
        group.MapPost("/", async (CreateIdeaRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateIdea.Command { /* map from body */ }, ct);
            return result.ToApiResult(); // or ToCreatedApiResult for 201
        });

        // PUT /api/ideas/{id}/save — save idea with notes/tags
        group.MapPut("/{id:guid}/save", async (Guid id, SaveIdeaRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new SaveIdea.Command { IdeaId = id, Notes = body.Notes, Tags = body.Tags };
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        // PUT /api/ideas/{id}/dismiss — dismiss idea
        group.MapPut("/{id:guid}/dismiss", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DismissIdea.Command(id), ct);
            return result.ToApiResult();
        });

        // POST /api/ideas/{id}/create-content — convert to content draft
        group.MapPost("/{id:guid}/create-content", async (Guid id, CreateContentFromIdeaRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateContentFromIdea.Command { IdeaId = id, ContentType = body.ContentType, PrimaryPlatform = body.PrimaryPlatform };
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });
    }
}
```

**Route ordering matters:** The `/connections` route must be registered before `/{id:guid}` to prevent `"connections"` from being parsed as a GUID (which would fail, but it's cleaner to avoid the ambiguity).

**Query parameter binding:** For the `GET /api/ideas` endpoint, use `[AsParameters]` with a record that mirrors the `ListIdeas.Query` fields. ASP.NET Core binds query string parameters to the record properties automatically.

```csharp
public record ListIdeasQueryParams
{
    public IdeaStatus? Status { get; init; }
    public Guid? IdeaSourceId { get; init; }
    public string? Category { get; init; }
    [FromQuery(Name = "tags")] public string[]? Tags { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? SearchText { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string SortBy { get; init; } = "detectedAt";
    public string SortDirection { get; init; } = "desc";
}
```

This params record can live in the same file or in a separate file under `src/PBA.Api/Endpoints/`.

### IdeaSourceEndpoints

**File:** `src/PBA.Api/Endpoints/IdeaSourceEndpoints.cs`

```csharp
namespace PBA.Api.Endpoints;

public static class IdeaSourceEndpoints
{
    public static void MapIdeaSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/idea-sources").WithTags("IdeaSources");

        // GET /api/idea-sources — list all sources
        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListIdeaSources.Query(), ct);
            return result.ToApiResult();
        });

        // POST /api/idea-sources — create source
        group.MapPost("/", async (IdeaSourceRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateIdeaSource.Command { /* map from body */ };
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        // PUT /api/idea-sources/{id} — update source
        group.MapPut("/{id:guid}", async (Guid id, IdeaSourceRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateIdeaSource.Command { Id = id, /* map from body */ };
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        // DELETE /api/idea-sources/{id} — delete source
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteIdeaSource.Command(id), ct);
            return result.ToApiResult();
        });

        // POST /api/idea-sources/refresh — force refresh all sources
        group.MapPost("/refresh", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RefreshIdeaSources.Command(), ct);
            return result.ToApiResult();
        });
    }
}
```

### Program.cs Updates

**File:** `src/PBA.Api/Program.cs`

Changes to make:

1. **Remove** the two inline endpoint stubs (`/api/ideas` and `/api/feed`)
2. **Add** endpoint group registration calls
3. **Add** the `public partial class Program` declaration for test project access
4. **Add** MediatR's `ISender` using directive if needed

Updated Program.cs structure:

```csharp
using PBA.Api.Endpoints;
// ... existing usings ...

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationDependencies();
builder.Services.AddInfrastructureDependencies(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

// Endpoint groups
app.MapIdeaEndpoints();
app.MapIdeaSourceEndpoints();

app.Run();

public partial class Program { }
```

### MediatR ISender Injection

The endpoint handlers inject `ISender` (the MediatR interface for sending requests). This is automatically registered by `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly))` in `PBA.Application.DependencyInjection`. ASP.NET Core Minimal APIs resolve `ISender` from DI automatically when it appears as a parameter.

### Result-to-HTTP Response Patterns

Each endpoint follows the same pattern:

```csharp
var result = await sender.Send(command, ct);
return result.ToApiResult();
```

The `ToApiResult()` extension (from section-01) handles all the mapping. For create operations that should return 201 instead of 200, you have two options:

1. Modify `ToApiResult` to accept an optional `createdPath` parameter
2. Handle the create case inline: `return result.IsSuccess ? Results.Created($"/api/ideas/{result.Value}", result.Value) : result.ToApiResult()`

The second option is simpler and avoids overcomplicating the extension method.

### JSON Serialization

Ensure enums serialize as strings (not integers) in API responses. Add to `Program.cs` if not already configured:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

This affects all Minimal API JSON serialization.

---

## Endpoint Summary Table

| Method | Path | Handler | Response |
|--------|------|---------|----------|
| GET | `/api/ideas` | ListIdeas.Query | 200 + PagedResult\<IdeaDto\> |
| GET | `/api/ideas/connections` | GetIdeaConnections.Query | 200 + IdeaConnectionDto[] |
| GET | `/api/ideas/{id}` | GetIdea.Query | 200 + IdeaDetailDto, 404 |
| POST | `/api/ideas` | CreateIdea.Command | 201 + Guid, 400 |
| PUT | `/api/ideas/{id}/save` | SaveIdea.Command | 200, 404 |
| PUT | `/api/ideas/{id}/dismiss` | DismissIdea.Command | 200, 404 |
| POST | `/api/ideas/{id}/create-content` | CreateContentFromIdea.Command | 201 + Guid, 404 |
| GET | `/api/idea-sources` | ListIdeaSources.Query | 200 + IdeaSourceDto[] |
| POST | `/api/idea-sources` | CreateIdeaSource.Command | 201 + Guid, 400 |
| PUT | `/api/idea-sources/{id}` | UpdateIdeaSource.Command | 200, 404 |
| DELETE | `/api/idea-sources/{id}` | DeleteIdeaSource.Command | 204, 404 |
| POST | `/api/idea-sources/refresh` | RefreshIdeaSources.Command | 200 + int (count) |

---

## Test Project Setup

If `tests/PBA.Api.Tests/` does not already exist (from section-01), create it:

- **csproj** referencing: `PBA.Api`, `Microsoft.AspNetCore.Mvc.Testing`, xUnit, Moq
- **WebApplicationFactory** setup class (see above)
- Add to `PBA.slnx`

The integration tests exercise the full HTTP pipeline: routing, parameter binding, MediatR dispatch, handler execution, Result-to-HTTP mapping. They validate that the endpoint wiring is correct even if the individual handlers were already tested in sections 04/05.

---

## Verification Checklist

- [x] Both endpoint files compile in `PBA.Api`
- [x] Inline stubs removed from `Program.cs`
- [x] Endpoint groups registered in `Program.cs`
- [x] `public partial class Program { }` added for test access
- [x] 11 endpoints reachable (12th — create-content — deferred to section-16)
- [x] Query string parameter binding works for `GET /api/ideas` (all nullable params)
- [x] Route parameter binding works for `{id:guid}` endpoints
- [x] `GET /api/ideas/connections` registered before `/{id:guid}` — no conflict
- [x] JSON enum serialization configured (JsonStringEnumConverter)
- [x] Integration tests pass with `WebApplicationFactory` (22 tests)
- [x] Validation errors return 400 with error details
- [x] NotFound results return 404
- [x] `dotnet build` and `dotnet test` pass (96 total: 74 Application + 22 Api)

## Actual Implementation Notes

### Deviations from Plan
- `POST /api/ideas/{id}/create-content` endpoint deferred to section-16 (handler not yet implemented)
- `ListIdeasQueryParams` uses all nullable properties (`int?`, `string?`) to avoid ASP.NET required parameter binding — defaults applied at endpoint mapping level
- `PageSize` capped via `Math.Clamp(1, 100)` at endpoint level (code review fix — prevents DoS)
- `SortBy` default aligned to lowercase `"detectedat"` to match `ApplySort` switch (code review fix)
- `IdeaSourceRequest` DTO extended with `IsEnabled` property for update operations (code review fix)
- `TestWebApplicationFactory` aggressively removes ALL EF Core + NpgsQL service descriptors before registering InMemory provider — simple `SingleOrDefault` removal was insufficient
- `ResultExtensions` refactored: extracted shared `MapFailure` method, added non-generic `ToApiResult()` overload for `Result` (non-generic commands like DismissIdea)
- `Infrastructure.DependencyInjection` added `IAppDbContext` scoped registration

### Files Created/Modified
- `src/PBA.Api/Endpoints/IdeaEndpoints.cs` (new — 103 lines)
- `src/PBA.Api/Endpoints/IdeaSourceEndpoints.cs` (new — 66 lines)
- `src/PBA.Api/Extensions/ResultExtensions.cs` (modified — added non-generic overload)
- `src/PBA.Api/Program.cs` (modified — stubs removed, endpoints registered, JSON enum converter)
- `src/PBA.Infrastructure/DependencyInjection.cs` (modified — IAppDbContext registration)
- `src/PBA.Application/Features/Ideas/Dtos/IdeaSourceRequest.cs` (modified — added IsEnabled)
- `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` (new — 45 lines)
- `tests/PBA.Api.Tests/Endpoints/IdeaEndpointsTests.cs` (new — 7 tests)
- `tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs` (new — 6 tests + validation test)
- `tests/PBA.Api.Tests/PBA.Api.Tests.csproj` (modified — added InMemory + project references)
