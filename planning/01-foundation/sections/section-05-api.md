# Section 05 -- API Layer

## Overview

This section implements the ASP.NET Minimal API layer: `Program.cs` service registration and middleware pipeline, API key authentication middleware, Content CRUD endpoints, a `Result<T>` to HTTP response mapper, ProblemDetails error responses, health endpoints, a global exception handler, Swagger/OpenAPI configuration, and CORS setup.

**Project:** `src/PersonalBrandAssistant.Api/`

**Dependencies:** This section depends on:
- **Section 01 (Scaffolding)** -- Solution structure and project files must exist
- **Section 03 (Application)** -- `Result<T>`, MediatR handlers, FluentValidation validators, interfaces, Content CRUD commands/queries
- **Section 04 (Infrastructure)** -- `AddInfrastructure` DI extension, `ApplicationDbContext`, encryption service, health checks, interceptors, seed data

---

## Tests First

All API layer tests live in `tests/PersonalBrandAssistant.Infrastructure.Tests/` (integration tests using `WebApplicationFactory<Program>`) since they require the full middleware pipeline and real HTTP requests.

### API Key Middleware Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ApiKeyMiddlewareTests.cs`

```csharp
/// Uses WebApplicationFactory<Program> with a known test API key configured in test appsettings.
/// Each test sends an HTTP request through the real pipeline.

// Test: request with valid X-Api-Key header passes through (200 on /health/ready)
// Test: request with invalid X-Api-Key returns 401 with ProblemDetails body
// Test: request with missing X-Api-Key header returns 401 with ProblemDetails body
// Test: GET /health (liveness) returns 200 without any API key (exempt endpoint)
// Test: GET /health/ready returns 401 without API key (not exempt)
```

### Content Endpoints Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEndpointsTests.cs`

```csharp
/// Integration tests using WebApplicationFactory<Program> + Testcontainers PostgreSQL.
/// All requests include valid X-Api-Key header.

// Test: POST /api/content with valid body returns 201 with created content ID
// Test: POST /api/content with invalid body (missing Body field) returns 400 ProblemDetails with validation errors
// Test: GET /api/content/{id} with existing ID returns 200 with full content JSON
// Test: GET /api/content/{id} with non-existent GUID returns 404 ProblemDetails
// Test: GET /api/content with no query params returns 200 with paginated list
// Test: GET /api/content?contentType=BlogPost returns 200 with filtered results
// Test: PUT /api/content/{id} with valid body returns 200 with updated content
// Test: PUT /api/content/{id} with stale version returns 409 ProblemDetails
// Test: DELETE /api/content/{id} returns 200 (soft delete to Archived)
```

### Result-to-HTTP Mapper Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ResultToHttpMapperTests.cs`

```csharp
/// Unit tests for the ToHttpResult<T>() extension method.
/// These can be pure unit tests -- no WebApplicationFactory needed.

// Test: Result.Success(value) maps to 200 OK with JSON body
// Test: Result.ValidationFailure(errors) maps to 400 with application/problem+json content type
// Test: Result.NotFound(message) maps to 404 ProblemDetails
// Test: Result.Conflict(message) maps to 409 ProblemDetails
// Test: Result.Failure(Unauthorized, msg) maps to 401 ProblemDetails
// Test: Result.Failure(InternalError, msg) maps to 500 ProblemDetails
// Test: all error responses include "type", "title", "status", and "detail" fields per RFC 9457
```

### Global Exception Handler Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/GlobalExceptionHandlerTests.cs`

```csharp
/// Uses WebApplicationFactory with a test endpoint that throws an exception.

// Test: unhandled exception returns 500 ProblemDetails with no stack trace in Production
// Test: unhandled exception is logged via Serilog (verify ILogger mock or log output)
```

### Health Endpoint Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/HealthEndpointTests.cs`

```csharp
// Test: GET /health returns 200 when API is running
// Test: GET /health/ready returns 200 when DB is connected (with valid API key)
// Test: GET /health/ready returns 503 when DB is unreachable
```

### Swagger Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SwaggerTests.cs`

```csharp
// Test: /swagger endpoint accessible when ASPNETCORE_ENVIRONMENT=Development
// Test: /swagger returns 404 when ASPNETCORE_ENVIRONMENT=Production
```

---

## Implementation Details

### File: `src/PersonalBrandAssistant.Api/Program.cs`

The application entry point. Configures all services and the middleware pipeline.

**Service registration order:**
1. **Serilog** -- Configure structured JSON console sink (primary), rolling file sink (dev only). Enrich with RequestId, MachineName, ThreadId. Minimum level Information, override Microsoft.* to Warning. Add destructuring policy to exclude properties matching `*Token*`, `*Password*`, `*Secret*`, `*Key*` patterns.
2. **MediatR** -- Scan the Application assembly. Register `ValidationBehavior` and `LoggingBehavior` pipeline behaviors.
3. **FluentValidation** -- Scan the Application assembly for all `AbstractValidator<T>` implementations.
4. **Infrastructure services** -- Call `builder.Services.AddInfrastructure(builder.Configuration)` (defined in section 04). This registers DbContext, encryption, Data Protection, health checks, hosted services.
5. **API key authentication** -- Read the expected key from configuration (`ApiKey` setting). In development, source from User Secrets. In Docker/production, source from environment variable `API_KEY`.
6. **CORS** -- Named policy allowing `http://localhost:4200` (Angular dev server) with any header and any method. In production, same-origin applies (API and Angular behind same nginx).
7. **Swagger/OpenAPI** -- Configure OpenAPI document with title "Personal Brand Assistant API" and version "v1". Enable XML documentation comments. Add security definition for `X-Api-Key` header parameter.

**Middleware pipeline order:**
1. Global exception handler (`app.UseExceptionHandler()`)
2. Serilog request logging (`app.UseSerilogRequestLogging()`)
3. CORS (`app.UseCors(policyName)`)
4. API key authentication middleware
5. Swagger UI (conditional: Development environment only, at `/swagger`)
6. Map health endpoints
7. Map feature endpoints (Content)

### File: `src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs`

A custom middleware class that validates the `X-Api-Key` request header.

**Behavior:**
- Read the expected API key from `IConfiguration` (key path: `"ApiKey"`)
- Exempt paths: `GET /health` (exact match, liveness only)
- If the header is missing or does not match the configured key, short-circuit with 401 and a ProblemDetails JSON response body
- If valid, call `next(context)` to continue the pipeline
- Register in `Program.cs` via `app.UseMiddleware<ApiKeyMiddleware>()`

### File: `src/PersonalBrandAssistant.Api/Endpoints/ContentEndpoints.cs`

A static class with a `MapContentEndpoints` extension method on `IEndpointRouteBuilder`.

```csharp
public static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content").WithTags("Content");

        group.MapGet("/", ListContent);
        group.MapGet("/{id:guid}", GetContent);
        group.MapPost("/", CreateContent);
        group.MapPut("/{id:guid}", UpdateContent);
        group.MapDelete("/{id:guid}", DeleteContent);
    }

    // Each handler method:
    //   1. Accept ISender (MediatR) via DI injection
    //   2. Parse route/query/body parameters
    //   3. Create the appropriate command or query
    //   4. Send via ISender
    //   5. Convert Result<T> to IResult via ToHttpResult()
}
```

**Endpoint details:**

- **POST /api/content** -- Accepts a JSON body with `ContentType`, `Title` (optional), `Body`, `TargetPlatforms` (optional array). Creates a `CreateContentCommand`, sends via MediatR. On success, returns `201 Created` with the new content ID and a `Location` header.
- **GET /api/content/{id:guid}** -- Creates a `GetContentQuery`, returns 200 with content JSON or 404 ProblemDetails.
- **GET /api/content** -- Accepts query parameters: `contentType` (optional filter), `status` (optional filter), `pageSize` (default 20, max 50), `cursor` (optional keyset pagination cursor as a base64-encoded `(CreatedAt, Id)` tuple). Creates a `ListContentQuery`, returns 200 with a response containing `items` array and `nextCursor` (null if no more pages).
- **PUT /api/content/{id:guid}** -- Accepts JSON body with updatable fields and a `version` field (for concurrency). Creates `UpdateContentCommand`, returns 200 with updated content or 409 on version conflict.
- **DELETE /api/content/{id:guid}** -- Creates `DeleteContentCommand` (soft delete to Archived), returns 200.

### File: `src/PersonalBrandAssistant.Api/Extensions/ResultExtensions.cs`

Contains the `ToHttpResult<T>()` extension method that maps `Result<T>` to `IResult`.

```csharp
public static class ResultExtensions
{
    /// <summary>
    /// Maps a Result<T> to the appropriate IResult (TypedResults).
    /// All error responses use RFC 9457 ProblemDetails format.
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        // Success: return 200 OK with JSON body
        // ErrorCode.ValidationFailed: return 400 with ProblemDetails including errors array
        // ErrorCode.NotFound: return 404 with ProblemDetails
        // ErrorCode.Conflict: return 409 with ProblemDetails
        // ErrorCode.Unauthorized: return 401 with ProblemDetails
        // ErrorCode.InternalError: return 500 with ProblemDetails (no internal details)
    }
}
```

Use `TypedResults.Problem()` to generate ProblemDetails responses. Set `ContentType` to `application/problem+json`. Include `type`, `title`, `status`, and `detail` fields. For validation errors, include the `errors` dictionary in the extensions.

**Overload for 201 Created:**

```csharp
public static IResult ToCreatedHttpResult<T>(this Result<T> result, string routePrefix)
{
    // Success: return 201 with Location header
    // Error: delegate to ToHttpResult
}
```

### File: `src/PersonalBrandAssistant.Api/Handlers/GlobalExceptionHandler.cs`

Implements `IExceptionHandler` (ASP.NET 8+ pattern).

```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// Catches all unhandled exceptions. Logs the full exception via Serilog
    /// (with sensitive data redaction via the destructuring policy).
    /// Returns a 500 ProblemDetails response with no stack trace or internal details.
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Log the exception (Serilog handles redaction)
        // Write ProblemDetails with status 500, title "Internal Server Error",
        // detail "An unexpected error occurred" (never expose exception message)
        // Return true to indicate the exception was handled
    }
}
```

Register in `Program.cs`:
```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
```

### File: `src/PersonalBrandAssistant.Api/Endpoints/HealthEndpoints.cs`

```csharp
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /health -- liveness probe, always returns 200 if the API process is running.
        //   No auth required (exempt in ApiKeyMiddleware).
        //   Returns: { "status": "Healthy" }

        // GET /health/ready -- readiness probe, checks PostgreSQL connectivity.
        //   Requires API key (not exempt).
        //   Uses ASP.NET Health Checks (registered by Infrastructure's AddInfrastructure).
        //   Returns 200 if healthy, 503 if DB unreachable.
    }
}
```

### Swagger Configuration

In `Program.cs`, configure Swagger conditionally:

```csharp
// Only in Development
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}
```

Configure the OpenAPI document to include the `X-Api-Key` header as a security scheme so developers can test authenticated endpoints from the Swagger UI.

### CORS Configuration

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
```

In production, CORS is not needed because Angular and API are served from the same origin behind nginx in Docker.

---

## Configuration Files

### File: `src/PersonalBrandAssistant.Api/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "ApiKey": "",
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information"
      }
    },
    "WriteTo": [
      { "Name": "Console", "Args": { "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact" } }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  },
  "AuditLogRetentionDays": 90,
  "AllowedHosts": "*"
}
```

### File: `src/PersonalBrandAssistant.Api/appsettings.Development.json`

Development overrides: add file sink for Serilog, lower minimum levels for debugging. Connection string points to local or Docker PostgreSQL. API key set via User Secrets (not in this file).

---

## NuGet Packages Required (Api project)

- `Serilog.AspNetCore` -- Serilog integration with ASP.NET
- `Serilog.Formatting.Compact` -- Compact JSON formatter for structured logging
- `Serilog.Enrichers.Environment` -- MachineName enricher
- `Serilog.Enrichers.Thread` -- ThreadId enricher
- `Serilog.Sinks.File` -- Rolling file sink (dev only)
- `Swashbuckle.AspNetCore` or `Microsoft.AspNetCore.OpenApi` -- Swagger/OpenAPI (use whichever is standard for .NET 10; `Microsoft.AspNetCore.OpenApi` is built-in starting .NET 9)

The Api project also has project references to:
- `PersonalBrandAssistant.Application`
- `PersonalBrandAssistant.Infrastructure`

---

## Test Configuration

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/appsettings.Testing.json`

```json
{
  "ApiKey": "test-api-key-12345",
  "AuditLogRetentionDays": 90
}
```

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/CustomWebApplicationFactory.cs`

A custom `WebApplicationFactory<Program>` that:
- Replaces the PostgreSQL connection string with one pointing to the Testcontainers instance
- Sets the `ApiKey` configuration to a known test value (`"test-api-key-12345"`)
- Sets environment to Development for Swagger tests (or Production for negative Swagger tests)
- Provides a helper method `CreateAuthenticatedClient()` that returns an `HttpClient` with the `X-Api-Key` header pre-set

---

## Implementation Checklist

1. Create `GlobalExceptionHandler` implementing `IExceptionHandler`
2. Create `ApiKeyMiddleware` with path exemptions
3. Create `ResultExtensions` with `ToHttpResult<T>()` and `ToCreatedHttpResult<T>()`
4. Create `HealthEndpoints` with liveness and readiness probes
5. Create `ContentEndpoints` mapping all five CRUD operations
6. Wire everything together in `Program.cs` (services, middleware pipeline, endpoint mapping)
7. Add `appsettings.json` and `appsettings.Development.json`
8. Write all tests listed above, verify they pass

---

## Actual Implementation Notes

### Deviations from Plan
1. **Swagger config simplified**: Swashbuckle 10.x moved `Microsoft.OpenApi.Models` namespace. Used `AddSwaggerGen()` without custom security scheme config rather than fighting the API change.
2. **`appsettings.Development.json` not modified**: Kept existing file. File sink not added.
3. **Serilog enrichers**: Only `FromLogContext` configured — `MachineName` and `ThreadId` enrichers not added to avoid extra NuGet dependencies.
4. **Tests in Infrastructure.Tests project**: Per plan, API integration tests live in `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/` since they need Testcontainers.
5. **Swagger tests omitted**: Environment-conditional Swagger testing requires separate factory configs. Deferred to testing section.
6. **`EnsureCreatedAsync` instead of migrations**: No migrations generated yet. Tests use `EnsureCreatedAsync` to create schema.

### Code Review Fixes Applied
- API key comparison uses SHA256 + `CryptographicOperations.FixedTimeEquals` (timing-safe)
- DELETE endpoint returns 204 No Content instead of 200
- pageSize clamped with `Math.Clamp(pageSize, 1, 50)`
- OPTIONS preflight requests exempted from API key check
- HashSet uses `StringComparer.OrdinalIgnoreCase` with `Contains` instead of LINQ

### Test Summary
- 22 API tests total
- ResultToHttpMapper: 9 unit tests (all error codes + RFC compliance + created)
- ApiKeyMiddleware: 4 integration tests (valid/invalid/missing key, exempt path)
- ContentEndpoints: 6 integration tests (CRUD + validation + 404)
- HealthEndpoints: 2 integration tests (liveness + readiness)
- GlobalExceptionHandler: 1 integration test (500 ProblemDetails format)

### Key Files (Actual)
| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Api/Program.cs` | Service registration, middleware pipeline, endpoint mapping |
| `src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs` | X-Api-Key validation with timing-safe comparison |
| `src/PersonalBrandAssistant.Api/Endpoints/ContentEndpoints.cs` | Content CRUD via MediatR |
| `src/PersonalBrandAssistant.Api/Endpoints/HealthEndpoints.cs` | Liveness + readiness probes |
| `src/PersonalBrandAssistant.Api/Extensions/ResultExtensions.cs` | Result<T> to IResult/ProblemDetails mapping |
| `src/PersonalBrandAssistant.Api/Handlers/GlobalExceptionHandler.cs` | IExceptionHandler returning 500 ProblemDetails |
| `tests/.../Api/CustomWebApplicationFactory.cs` | WebApplicationFactory with Testcontainers PostgreSQL |
| `tests/.../Api/` | 5 test files, 22 tests total |