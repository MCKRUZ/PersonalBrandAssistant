# Implementation Plan — 01 Foundation & Infrastructure

## 1. Project Overview

### What We're Building
The foundation layer for the **Personal Brand Assistant** — an AI-powered agent system that manages personal branding across Twitter/X, LinkedIn, Instagram, YouTube, and a git-deployed blog (matthewkruczek.ai). This foundation split establishes the .NET 10 solution structure, domain models, PostgreSQL database, Docker environment, API skeleton, and Angular shell that 5 subsequent splits build upon.

### Why This Architecture
- **Clean Architecture** — Strict layer separation (Domain → Application → Infrastructure → API) ensures each subsequent split can add features without breaking existing contracts. The inner layers (Domain, Application) are stable; outer layers (Infrastructure, API) can evolve.
- **Minimal APIs + MediatR** — Thin endpoint delegates dispatching to MediatR handlers give us structured command/query separation without ceremony. Pipeline behaviors handle cross-cutting concerns (validation, logging) once.
- **Single-user model with API key auth** — The user is the sole operator. No multi-tenancy overhead. A simple API key middleware protects all endpoints since the Synology NAS is LAN-accessible. If multi-tenant is needed later, adding a UserId filter is straightforward.
- **TPH for Content** — Single table with discriminator enables polymorphic queries across all content types (essential for workflow engine, calendar, and analytics). The nullable columns are a small tradeoff.

### Key Technology Choices
| Choice | Technology | Rationale |
|--------|-----------|-----------|
| Runtime | .NET 10 (LTS, v10.0.4) | Stable, C# 14 features, LTS until Nov 2028 |
| ORM | EF Core 10 + Npgsql 10.0.1 | Complex types with ToJson() for jsonb, LeftJoin, named query filters |
| Database | PostgreSQL 17 | jsonb support, GIN indexes, Docker-friendly, 20x faster vacuum |
| Frontend | Angular 19 + PrimeNG | Standalone components, NgRx signals, 80+ ready UI components |
| API Pattern | Minimal APIs + MediatR | Thin endpoints, structured handlers, pipeline behaviors |
| Validation | FluentValidation | Integrates with MediatR pipeline behavior |
| Logging | Serilog | Structured logging, console sink primary, file sink dev-only |
| Encryption | ASP.NET Data Protection API | Built-in, key rotation, filesystem key storage for self-hosted |
| Auth | API Key middleware | Simple single-user auth, protects LAN-exposed API |
| Testing | xUnit + Moq + Testcontainers | Standard .NET testing with real PostgreSQL in integration tests |
| Containerization | Docker Compose | Multi-stage builds, Ubuntu Chiseled runtime (~110MB) |
| Deployment | Synology NAS (Intel x86, 8GB+ RAM) | Self-hosted Docker, no cloud dependency |

---

## 2. Solution Structure

### Project Layout

```
PersonalBrandAssistant/
├── src/
│   ├── PersonalBrandAssistant.Domain/          # Entities, enums, value objects, events
│   ├── PersonalBrandAssistant.Application/     # MediatR handlers, interfaces, DTOs, validation
│   ├── PersonalBrandAssistant.Infrastructure/  # EF Core, PostgreSQL, Data Protection, services
│   ├── PersonalBrandAssistant.Api/             # Minimal API endpoints, middleware, Program.cs
│   └── PersonalBrandAssistant.Web/             # Angular 19 app
├── tests/
│   ├── PersonalBrandAssistant.Domain.Tests/
│   ├── PersonalBrandAssistant.Application.Tests/
│   └── PersonalBrandAssistant.Infrastructure.Tests/
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
├── Directory.Build.props
└── PersonalBrandAssistant.sln
```

### Dependency Direction
```
API → Application → Domain
Infrastructure → Application → Domain
```
Infrastructure and API reference Application. Application references only Domain. Domain references nothing.

### Directory.Build.props
Central configuration for all projects: target framework (net10.0), nullable enable, implicit usings, common package versions via `<ManagePackageVersionsCentrally>`.

---

## 3. Domain Layer

### Entity Base Class
All entities inherit from a base class providing:
- `Id` (Guid) — Generated via `Guid.CreateVersion7()` (.NET 10 native UUIDv7 support). Provides temporal locality for index performance.
- `CreatedAt`, `UpdatedAt` (DateTimeOffset) — Set automatically by the auditable interceptor.

### Entities

#### Content (TPH — discriminator: ContentType)
The central domain entity. All content types share one table.

Fields:
- `Id` (Guid) — Primary key, UUIDv7 via `Guid.CreateVersion7()`
- `ContentType` (enum) — BlogPost, SocialPost, Thread, VideoDescription
- `Title` (string?) — Optional (social posts may not have titles)
- `Body` (string) — Content text or HTML
- `Status` (ContentStatus enum) — Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived
- `Metadata` (ContentMetadata) — Complex type mapped to jsonb
- `ParentContentId` (Guid?) — Self-referential FK for content relationships (blog → thread → posts)
- `TargetPlatforms` (PlatformType[]) — PostgreSQL array type with GIN index, mapped via Npgsql enum array support
- `ScheduledAt` (DateTimeOffset?) — When to publish (always UTC)
- `PublishedAt` (DateTimeOffset?) — When actually published (always UTC)
- `Version` (uint) — Optimistic concurrency token mapped to PostgreSQL `xmin`
- `CreatedAt`, `UpdatedAt` (DateTimeOffset) — Audit timestamps (always UTC)

**Status State Machine:**
Content enforces valid transitions via a `TransitionTo(ContentStatus newStatus)` domain method. Invalid transitions throw `InvalidOperationException`.

| From | Allowed To |
|------|-----------|
| Draft | Review, Archived |
| Review | Draft, Approved, Archived |
| Approved | Scheduled, Draft, Archived |
| Scheduled | Publishing, Draft, Archived |
| Publishing | Published, Failed |
| Published | Archived |
| Failed | Draft, Archived |
| Archived | Draft |

The `TransitionTo` method also raises a `ContentStateChangedEvent` domain event on successful transitions.

#### ContentMetadata (complex type → jsonb)
Flexible per-content-type data stored as PostgreSQL jsonb.

Fields:
- `Tags` (List<string>)
- `SeoKeywords` (List<string>)
- `PlatformSpecificData` (Dictionary<string, string>)
- `AiGenerationContext` (string?) — Prompt/context that generated this content
- `TokensUsed` (int?) — LLM tokens consumed
- `EstimatedCost` (decimal?) — LLM cost

#### Platform
One record per social platform connection.

Fields:
- `Id` (Guid)
- `Type` (PlatformType enum) — TwitterX, LinkedIn, Instagram, YouTube
- `DisplayName` (string)
- `IsConnected` (bool)
- `EncryptedAccessToken` (byte[]?) — Encrypted via Data Protection API. Decrypted only by `IEncryptionService` when needed for API calls. Never auto-decrypted by EF.
- `EncryptedRefreshToken` (byte[]?) — Same encryption approach.
- `TokenExpiresAt` (DateTimeOffset?)
- `RateLimitState` (PlatformRateLimitState) — Complex type → jsonb
- `LastSyncAt` (DateTimeOffset?)
- `Settings` (PlatformSettings) — Complex type → jsonb
- `Version` (uint) — Optimistic concurrency via `xmin`

#### BrandProfile
Configurable brand voice that gets injected into all AI prompts.

Fields:
- `Id` (Guid)
- `Name` (string) — Profile name
- `ToneDescriptors` (List<string>) — e.g., ["professional", "authoritative"]
- `StyleGuidelines` (string) — Prose description
- `VocabularyPreferences` (VocabularyConfig) — Complex type → jsonb (preferred terms, avoid terms)
- `Topics` (List<string>) — Focus areas
- `PersonaDescription` (string) — Who the brand represents
- `ExampleContent` (List<string>) — Few-shot examples for AI
- `IsActive` (bool)
- `Version` (uint) — Optimistic concurrency via `xmin`

#### ContentCalendarSlot
Scheduled content slots for the content calendar.

Fields:
- `Id` (Guid)
- `ScheduledDate` (DateOnly)
- `ScheduledTime` (TimeOnly?)
- `TimeZoneId` (string) — IANA timezone identifier (e.g., "America/New_York"). All recurrence evaluation uses this timezone.
- `Theme` (string?)
- `ContentType` (ContentType)
- `TargetPlatform` (PlatformType)
- `ContentId` (Guid?) — FK to Content, if assigned
- `IsRecurring` (bool)
- `RecurrencePattern` (string?) — Standard 5-field cron (NCrontab library). `ScheduledDate`/`ScheduledTime` = first occurrence. Cron generates subsequent slots. Evaluated in `TimeZoneId`.

#### AuditLogEntry
Tracks all state transitions and significant actions.

Fields:
- `Id` (Guid)
- `EntityType` (string)
- `EntityId` (Guid)
- `Action` (string)
- `OldValue` (string?) — Structured JSON. Never contains encrypted fields (tokens, secrets). Max 4KB per field.
- `NewValue` (string?) — Same constraints as OldValue.
- `Timestamp` (DateTimeOffset)
- `Details` (string?)

**Retention policy:** 90-day retention. A background cleanup job (or EF interceptor) purges entries older than 90 days. Implemented as a simple hosted service.

#### User
Simple single-user entity.

Fields:
- `Id` (Guid)
- `Email` (string)
- `DisplayName` (string)
- `TimeZoneId` (string) — IANA timezone. Used as default for calendar slots and UI display.
- `Settings` (UserSettings) — Complex type → jsonb
- `CreatedAt` (DateTimeOffset)

### Enums
- `ContentType` — BlogPost, SocialPost, Thread, VideoDescription
- `ContentStatus` — Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived
- `PlatformType` — TwitterX, LinkedIn, Instagram, YouTube
- `AutonomyLevel` — Manual, Assisted, SemiAuto, Autonomous

### Domain Events
- `ContentStateChangedEvent` — Raised when content transitions between states. Dispatched via MediatR notifications (in-process). Used by workflow engine (split 02) for audit logging and notifications.

**Limitation:** In-process domain events are not crash-resilient. If the process terminates mid-handler, the event is lost. This is acceptable for foundation — an outbox pattern can be added in split 02 if autonomous workflows require guaranteed delivery.

---

## 4. Application Layer

### Common Infrastructure

#### Result<T> Pattern
A discriminated union for operation results. Every handler returns `Result<T>` — never throws for expected failures.

Structure:
- `Value` (T?) — The successful result value
- `IsSuccess` (bool) — Whether the operation succeeded
- `ErrorCode` (ErrorCode enum) — Categorized error type: None, ValidationFailed, NotFound, Conflict, Unauthorized, InternalError
- `Errors` (IReadOnlyList<string>) — One or more error messages (validation can produce multiple)

Static factory methods: `Result.Success(value)`, `Result.Failure(errorCode, errors)`, `Result.NotFound(message)`, `Result.ValidationFailure(errors)`, `Result.Conflict(message)`.

#### API Error Mapping
Results map to HTTP responses via a consistent mapper:

| ErrorCode | HTTP Status | Response Format |
|-----------|------------|----------------|
| None (success) | 200/201 | JSON body |
| ValidationFailed | 400 | `application/problem+json` with `errors` array |
| NotFound | 404 | `application/problem+json` |
| Conflict | 409 | `application/problem+json` (concurrency conflicts) |
| Unauthorized | 401 | `application/problem+json` |
| InternalError | 500 | `application/problem+json` (no details in production) |

All error responses use RFC 9457 Problem Details format (`application/problem+json`).

#### Interfaces
- `IApplicationDbContext` — Defines DbSet properties for each entity. Implemented by Infrastructure's `ApplicationDbContext`.
- `IEncryptionService` — `Encrypt(string) → byte[]` / `Decrypt(byte[]) → string`. Implemented by Infrastructure using Data Protection API. Called explicitly, never via EF value converters.
- `IDateTimeProvider` — Wraps `DateTimeOffset.UtcNow` for testability.

#### MediatR Pipeline Behaviors
1. **ValidationBehavior<TRequest, TResponse>** — Runs FluentValidation validators before handler execution. Returns `Result.ValidationFailure(errors)` with structured error list instead of throwing.
2. **LoggingBehavior<TRequest, TResponse>** — Logs request type, duration, and success/failure via Serilog. Excludes sensitive fields via a destructuring policy (never logs tokens, passwords, or encrypted data).

### Feature Organization
Features are organized in `Features/{FeatureName}/Commands/` and `Features/{FeatureName}/Queries/` following CQRS convention. Each command/query has its own folder with Command, Handler, and Validator.

### Foundation Features (Content CRUD)
Implement basic CRUD as a proof of the architecture:

**Commands:**
- `CreateContentCommand` — Creates a new content draft. Validates ContentType, Body (required). Returns created Content ID.
- `UpdateContentCommand` — Updates content fields. Validates content exists and is in editable state (Draft or Review). Handles concurrency via `xmin` — returns `Result.Conflict` on version mismatch.
- `DeleteContentCommand` — Soft deletes (sets status to Archived via `TransitionTo`). Does not physically delete.

**Queries:**
- `GetContentQuery` — Returns single content by ID.
- `ListContentQuery` — Returns paginated content list with filtering by ContentType, Status. Uses **keyset pagination** with cursor `(CreatedAt, Id)`. Default sort: `CreatedAt` descending. Max page size: 50.

---

## 5. Infrastructure Layer

### EF Core Configuration

#### ApplicationDbContext
Implements `IApplicationDbContext`. Configures all entity mappings.

Key configuration patterns:
- **TPH for Content:** Configure discriminator on `ContentType` enum. Map each subtype to the same table.
- **Complex types with ToJson():** All jsonb columns use EF 10's `ComplexProperty().ToJson()` pattern.
- **No value converters for tokens:** `EncryptedAccessToken` and `EncryptedRefreshToken` stored as `byte[]` columns directly. Encryption/decryption handled by `IEncryptionService` in application code, never by EF.
- **TargetPlatforms as array:** Use Npgsql native enum array mapping for `PlatformType[]` with GIN index.
- **Optimistic concurrency:** Content, Platform, and BrandProfile use `xmin` system column as concurrency token via `UseXminAsConcurrencyToken()`.
- **Global query filter:** Content has a default filter excluding `Status == Archived`. Use `.IgnoreQueryFilters()` for analytics/history views.
- **Targeted indexes:** Add GIN index only on `Content.Metadata → Tags` path (expression index). Defer other jsonb indexes until query patterns emerge. Add composite index on ContentCalendarSlot `(ScheduledDate, TargetPlatform)`.
- **Unique constraints:** Platform.Type is unique (one connection per platform).

#### Auditable Entity Interceptor
An EF Core `SaveChangesInterceptor` that automatically sets `CreatedAt` on insert and `UpdatedAt` on update for all entities implementing an `IAuditable` interface.

#### Audit Log Interceptor
A separate `SaveChangesInterceptor` that automatically creates `AuditLogEntry` records for tracked entity changes. Excludes encrypted fields from `OldValue`/`NewValue` serialization. Enforces 4KB size limit per field.

#### Seed Data
Runtime seed via a `IHostedService` that runs on startup. Conditional — only seeds when tables are empty. This approach is more flexible than `HasData` and environment-aware.

Seeds:
- Default `BrandProfile` with placeholder values
- Four `Platform` records (one per PlatformType) with `IsConnected = false`
- A default `User` record with configurable email/timezone from environment

#### Encryption Service
Implements `IEncryptionService` using ASP.NET Data Protection API.
- `IDataProtector` with purpose string "PersonalBrandAssistant.Secrets"
- Keys stored on filesystem with explicit application name ("PersonalBrandAssistant")
- In Docker, mount a persistent volume for the key directory at `/data-protection-keys/`
- Key lifetime explicitly set (90 days default, with auto-rotation)
- **Critical:** Data Protection keys are critical state. Loss = inability to decrypt tokens. Volume must be backed up.

#### Audit Log Cleanup Service
A `BackgroundService` that runs daily and purges `AuditLogEntry` records older than 90 days. Configurable retention period via `appsettings.json`.

### Dependency Injection
`DependencyInjection.cs` in Infrastructure registers all infrastructure services. API's `Program.cs` calls only `AddInfrastructure(configuration)` — no duplicate registrations.

Registers:
- `ApplicationDbContext` with Npgsql connection string from configuration
- `IEncryptionService` → `EncryptionService`
- `IDateTimeProvider` → `DateTimeProvider`
- Data Protection services with filesystem persistence, explicit application name
- `DataSeeder` hosted service
- `AuditLogCleanupService` background service
- Health check for PostgreSQL connectivity

---

## 6. API Layer

### Program.cs Setup
The application entry point configures all services and middleware.

**Service registration order:**
1. MediatR (scan Application assembly)
2. FluentValidation (scan Application assembly)
3. Infrastructure services (via `AddInfrastructure` extension — includes Data Protection, health checks)
4. Serilog configuration
5. API key authentication
6. CORS (allow Angular dev server at localhost:4200)
7. Swagger/OpenAPI

**Middleware pipeline order:**
1. Global exception handler
2. Serilog request logging
3. CORS
4. API key authentication middleware
5. Swagger (dev only)
6. Endpoint routing
7. Map endpoints

### API Key Authentication
Simple middleware that validates an `X-Api-Key` header against a configured key.

- Key stored in User Secrets (dev) or environment variable `API_KEY` (Docker/production)
- Exempt endpoints: `GET /health` (liveness only — readiness endpoint requires auth since it reveals DB state)
- Returns 401 with ProblemDetails on invalid/missing key
- Angular sends the key via an HTTP interceptor (stored in environment config)

### Endpoint Organization
Each feature area has an `*Endpoints.cs` static class with a `MapEndpoints(IEndpointRouteBuilder)` extension method.

Example pattern for Content:
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
    // Each method: inject ISender, parse request, send command/query, map Result<T> to HTTP response
}
```

### Result-to-HTTP Mapper
A shared extension method `ToHttpResult<T>()` that converts `Result<T>` to the appropriate `IResult` (TypedResults). Handles all ErrorCode → HTTP status mappings consistently. All error responses use ProblemDetails.

### Health Endpoints
- `GET /health` — Basic liveness check (returns 200 if API is running). No auth required.
- `GET /health/ready` — Readiness check (verifies PostgreSQL connectivity). Requires API key.

### Global Exception Handler
Implements `IExceptionHandler`. Catches unhandled exceptions, logs via Serilog (with sensitive data redaction), returns ProblemDetails (no stack traces or internal details in production).

### Swagger Configuration
- OpenAPI document with API title and version
- XML documentation comments enabled
- Available at `/swagger` in Development environment only
- API key header parameter documented in Swagger UI

---

## 7. Docker Environment

### docker-compose.yml (Production)

Three services:
1. **api** — .NET 10 API
   - Multi-stage Dockerfile: SDK 10.0 build stage → Ubuntu Chiseled runtime (~110MB)
   - Port 8080 (internal) mapped to 5000 (host)
   - Depends on db (service_healthy condition)
   - Environment: connection string, API key, Data Protection key path
   - Volumes: `dpkeys` for Data Protection keys, `logs` for log files

2. **db** — PostgreSQL 17 Alpine
   - Port 5432
   - Health check: `pg_isready -U postgres` every 5s
   - Named volume `pgdata` for data persistence
   - Credentials from `.env` file

3. **web** — Nginx serving Angular static build
   - Port 4200 (host) → 80 (container)
   - Serves pre-built Angular assets from a multi-stage build
   - Nginx config with SPA fallback (try_files), security headers, gzip
   - Depends on api

### docker-compose.override.yml (Dev Overrides)
- **api:** Build from source, volume mount for hot reload with `dotnet watch`, `ASPNETCORE_ENVIRONMENT=Development`, additional debug port
- **web:** Replaced by Angular dev server (`ng serve --host 0.0.0.0`) with source code volume mount for hot reload, anonymous volume for `node_modules`

### .env.example
Template for required environment variables:
- `DB_PASSWORD` — PostgreSQL password
- `API_KEY` — API authentication key
- `ASPNETCORE_ENVIRONMENT` — Development/Production
- `DPKEYS_PATH` — Data Protection key storage path

### API Dockerfile
Multi-stage build:
1. **build** stage — `mcr.microsoft.com/dotnet/sdk:10.0-alpine`, restore, build, publish
2. **runtime** stage — `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, copy published output
3. Layer caching: copy .csproj files first for restore cache

### Angular Dockerfile (Production)
Multi-stage build:
1. **build** stage — `node:22-alpine`, `npm ci`, `ng build --configuration production`
2. **runtime** stage — `nginx:alpine`, copy built assets + nginx.conf

### Angular Dockerfile.dev (Development)
- `node:22-alpine` base
- `npm install` with anonymous volume mount to preserve node_modules
- `ng serve --host 0.0.0.0` for network access within Docker

---

## 8. Angular Foundation

### Project Creation
Create Angular 19 project with:
- Standalone components (no NgModules)
- SCSS for styling
- SSR disabled (SPA only)
- Routing enabled

### App Shell Layout
A responsive layout with:
- **Sidebar** — PrimeNG `Sidebar` or custom component with navigation menu. Collapsible.
- **Header** — Top bar with app name, user info, notification bell (placeholder)
- **Main content** — Router outlet for feature views

Navigation items (placeholders, implemented in split 06):
- Dashboard, Content, Calendar, Analytics, Platforms, Settings

### Routing
Lazy-loaded feature routes. Each feature area is a standalone route with its own folder:

```
/dashboard  → DashboardComponent (eager, default route)
/content    → lazy loaded feature
/calendar   → lazy loaded feature
/analytics  → lazy loaded feature
/platforms  → lazy loaded feature
/settings   → lazy loaded feature
```

### PrimeNG Setup
- Install PrimeNG, PrimeIcons, PrimeFlex
- Configure a theme (Lara or Aura)
- Create SCSS variables file for brand colors and spacing
- Import PrimeNG styles in styles.scss

### Shared Components
Create reusable components in `shared/components/`:
- **PageHeaderComponent** — Consistent page title + optional action buttons
- **StatusBadgeComponent** — Color-coded content status badges (Draft=gray, Published=green, Failed=red)
- **LoadingSpinnerComponent** — PrimeNG ProgressSpinner wrapper
- **EmptyStateComponent** — Placeholder for empty feature pages ("Coming soon" or "No content yet")

### NgRx Signals Store
Set up the app-wide store using NgRx signals:
- Initial store slices: `ui` (sidebar state, theme) and `auth` (user info)
- Feature stores will be added by split 06

### API Service
Central `ApiService` using HttpClient:
- Base URL from environment configuration
- Methods for GET, POST, PUT, DELETE with generic typing
- HTTP interceptor for API key header injection (`X-Api-Key`)
- HTTP interceptor for global error handling (toast notifications via PrimeNG)

### Environment Configuration
- `environment.ts` — `apiUrl: 'http://localhost:5000/api'`, `apiKey: ''` (loaded from local config or dev default)
- `environment.prod.ts` — Production API URL and key injection

---

## 9. Testing Infrastructure

### Domain Tests
- Entity creation with UUIDv7 verification
- Content status state machine transitions (valid + invalid)
- Value object equality
- Enum coverage

### Application Tests
- MediatR handler unit tests using Moq for `IApplicationDbContext`
- Validator tests for all FluentValidation validators
- Pipeline behavior tests (validation catches invalid requests, logging records correctly)
- Result<T> mapping tests (all ErrorCode variants)

### Infrastructure Tests (Integration)
- Use `Testcontainers` to spin up real PostgreSQL in Docker
- `WebApplicationFactory<Program>` for end-to-end API tests
- Test database migration applies cleanly
- Test CRUD operations against real database
- Test encryption/decryption round-trip
- Test optimistic concurrency conflict detection
- Test global query filter (archived content excluded)
- Test API key authentication (valid key, invalid key, missing key, exempt endpoints)

### Test Utilities
- `TestFixture` base class with common setup (WebApplicationFactory, test database, cleanup)
- Factory methods for creating test entities with sensible defaults
- Test API key configured in test appsettings

---

## 10. Cross-Cutting Concerns

### Serilog Configuration
- **Console sink** (primary) — Structured JSON output for Docker log aggregation via `docker logs`
- **File sink** (dev only) — Rolling file in `/logs/` directory (daily rotation, 30-day retention). Only enabled when `ASPNETCORE_ENVIRONMENT=Development` or bind mount provided.
- **Enrich with:** RequestId, MachineName, ThreadId
- **Minimum level:** Information (override to Warning for Microsoft.*)
- **Destructuring policy:** Exclude properties named `*Token*`, `*Password*`, `*Secret*`, `*Key*` from log output

### Configuration
- `appsettings.json` — Default configuration, no secrets
- `appsettings.Development.json` — Dev-specific overrides, gitignored
- User Secrets — For local development secrets (connection string passwords, API key)
- Environment variables — For Docker/production (override appsettings via `__` convention)

### Timezone Strategy
- All `DateTimeOffset` values persisted in UTC
- `User.TimeZoneId` stores the user's IANA timezone
- `ContentCalendarSlot.TimeZoneId` stores per-slot timezone (defaults to user's timezone)
- UI converts UTC timestamps to user's local timezone for display
- Recurrence patterns (cron) evaluated in the slot's timezone using NCrontab

### CORS
Allow `http://localhost:4200` in development for Angular dev server.
Production: same-origin (API and Angular served from same nginx host via Docker).

---

## 11. Implementation Order

The sections should be implemented in this order due to dependencies:

1. **Solution scaffolding** — Create .sln, projects, Directory.Build.props, NuGet packages
2. **Domain layer** — Entity base class, entities, enums, value objects, status state machine, domain events
3. **Application layer** — Interfaces, Result<T> with ErrorCode, MediatR setup, behaviors, Content CRUD handlers, keyset pagination
4. **Infrastructure layer** — DbContext, entity configurations (TPH, jsonb, xmin, query filters), migrations, encryption service, seed data hosted service, audit log cleanup service
5. **API layer** — Program.cs, API key middleware, endpoints with Result-to-HTTP mapper, ProblemDetails, Swagger, health checks
6. **Docker environment** — Dockerfiles (API multi-stage, Angular prod + dev, nginx), docker-compose (prod + dev override), .env
7. **Angular foundation** — Project creation, PrimeNG, app shell, routing, shared components, NgRx store, API service with key interceptor
8. **Testing** — Domain tests (state machine), application tests (handlers, validators, Result mapping), infrastructure integration tests (Testcontainers, auth, concurrency)
9. **Verification** — End-to-end validation that everything works together in Docker Compose
