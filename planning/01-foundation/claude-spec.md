# Complete Specification — 01 Foundation & Infrastructure

## Context

This is the foundation split for the **Personal Brand Assistant** — an AI-powered agent that manages personal branding across social media (Twitter/X, LinkedIn, Instagram, YouTube), blog writing (matthewkruczek.ai), content scheduling, and analytics. Built on .NET 10 + Angular 19 + PostgreSQL, deployed self-hosted on Synology NAS via Docker.

This split establishes the solution structure, domain models, database, Docker environment, and API skeleton that all 5 subsequent splits build upon:
- 02-workflow-engine (content state machine, approval flows, scheduling)
- 03-agent-orchestration (Claude API, hybrid agent framework)
- 04-platform-integrations (OAuth + API adapters for 4 platforms)
- 05-content-engine (content creation, repurposing, calendar, brand voice)
- 06-angular-dashboard (full workspace UI)

## Architecture Decisions

### Clean Architecture
The solution follows Clean Architecture with strict dependency direction:
- **Domain** — Entities, value objects, enums, domain events. Zero dependencies.
- **Application** — Use cases (MediatR handlers), interfaces, DTOs, validation. Depends only on Domain.
- **Infrastructure** — EF Core, PostgreSQL, external services, Data Protection. Implements Application interfaces.
- **API** — Minimal API endpoints dispatching to MediatR handlers. CORS, Swagger, middleware.
- **Tests** — xUnit + Moq, organized by layer.

### Minimal APIs + MediatR
API endpoints are thin Minimal API delegates that dispatch to MediatR handlers:
```
POST /api/content → CreateContentCommand → CreateContentHandler
GET /api/content/{id} → GetContentQuery → GetContentHandler
```
Pipeline behaviors for cross-cutting: validation (FluentValidation), logging, exception handling.

### Single-User Model
No multi-tenancy. Simple data model without TenantId. Can evolve later if needed.

## Stack & Versions

| Component | Version | Package |
|-----------|---------|---------|
| .NET | 10.0 (LTS) | SDK 10.0 |
| C# | 14 | — |
| EF Core | 10.0.4 | Microsoft.EntityFrameworkCore |
| Npgsql EF | 10.0.1 | Npgsql.EntityFrameworkCore.PostgreSQL |
| PostgreSQL | 17 | Docker image: postgres:17-alpine |
| Angular | 19 | @angular/cli |
| PrimeNG | Latest | primeng |
| MediatR | Latest | MediatR |
| FluentValidation | Latest | FluentValidation.DependencyInjection |
| Serilog | Latest | Serilog.AspNetCore |
| xUnit | Latest | xunit |

## Solution Structure

```
PersonalBrandAssistant/
├── src/
│   ├── PersonalBrandAssistant.Domain/
│   │   ├── Entities/
│   │   │   ├── Content.cs
│   │   │   ├── Platform.cs
│   │   │   ├── BrandProfile.cs
│   │   │   ├── ContentCalendarSlot.cs
│   │   │   ├── AuditLogEntry.cs
│   │   │   └── User.cs
│   │   ├── Enums/
│   │   │   ├── ContentType.cs
│   │   │   ├── ContentStatus.cs
│   │   │   ├── PlatformType.cs
│   │   │   └── AutonomyLevel.cs
│   │   ├── ValueObjects/
│   │   │   ├── ContentMetadata.cs
│   │   │   └── PlatformCredentials.cs
│   │   └── Events/
│   │       └── ContentStateChangedEvent.cs
│   │
│   ├── PersonalBrandAssistant.Application/
│   │   ├── Common/
│   │   │   ├── Interfaces/
│   │   │   │   ├── IApplicationDbContext.cs
│   │   │   │   ├── IEncryptionService.cs
│   │   │   │   └── IDateTimeProvider.cs
│   │   │   ├── Behaviors/
│   │   │   │   ├── ValidationBehavior.cs
│   │   │   │   └── LoggingBehavior.cs
│   │   │   └── Models/
│   │   │       ├── Result.cs
│   │   │       └── PaginatedList.cs
│   │   └── Features/
│   │       └── Content/
│   │           ├── Commands/
│   │           │   └── CreateContent/
│   │           │       ├── CreateContentCommand.cs
│   │           │       ├── CreateContentHandler.cs
│   │           │       └── CreateContentValidator.cs
│   │           └── Queries/
│   │               └── GetContent/
│   │                   ├── GetContentQuery.cs
│   │                   └── GetContentHandler.cs
│   │
│   ├── PersonalBrandAssistant.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   ├── Configurations/
│   │   │   │   ├── ContentConfiguration.cs
│   │   │   │   ├── PlatformConfiguration.cs
│   │   │   │   └── BrandProfileConfiguration.cs
│   │   │   ├── Migrations/
│   │   │   ├── Interceptors/
│   │   │   │   └── AuditableEntityInterceptor.cs
│   │   │   └── Seeds/
│   │   │       └── SeedData.cs
│   │   ├── Services/
│   │   │   ├── EncryptionService.cs
│   │   │   └── DateTimeProvider.cs
│   │   └── DependencyInjection.cs
│   │
│   ├── PersonalBrandAssistant.Api/
│   │   ├── Program.cs
│   │   ├── Endpoints/
│   │   │   ├── ContentEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   └── EndpointExtensions.cs
│   │   ├── Middleware/
│   │   │   └── GlobalExceptionHandler.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   └── Dockerfile
│   │
│   └── PersonalBrandAssistant.Web/
│       ├── src/
│       │   ├── app/
│       │   │   ├── core/
│       │   │   │   ├── layout/
│       │   │   │   │   ├── app-layout.component.ts
│       │   │   │   │   ├── sidebar.component.ts
│       │   │   │   │   └── header.component.ts
│       │   │   │   └── services/
│       │   │   │       └── api.service.ts
│       │   │   ├── shared/
│       │   │   │   ├── components/
│       │   │   │   ├── directives/
│       │   │   │   └── pipes/
│       │   │   ├── features/
│       │   │   │   ├── dashboard/
│       │   │   │   ├── content/
│       │   │   │   ├── calendar/
│       │   │   │   ├── analytics/
│       │   │   │   ├── platforms/
│       │   │   │   └── settings/
│       │   │   ├── store/
│       │   │   │   └── app.store.ts
│       │   │   ├── app.component.ts
│       │   │   ├── app.routes.ts
│       │   │   └── app.config.ts
│       │   ├── styles/
│       │   │   ├── _variables.scss
│       │   │   ├── _theme.scss
│       │   │   └── styles.scss
│       │   └── environments/
│       ├── angular.json
│       ├── package.json
│       ├── Dockerfile
│       └── Dockerfile.dev
│
├── tests/
│   ├── PersonalBrandAssistant.Domain.Tests/
│   ├── PersonalBrandAssistant.Application.Tests/
│   └── PersonalBrandAssistant.Infrastructure.Tests/
│
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
├── PersonalBrandAssistant.sln
└── Directory.Build.props
```

## Domain Models

### Content (TPH — Single Table with Discriminator)
The central entity. Uses Table-Per-Hierarchy with a `ContentType` discriminator.

**Properties:**
- `Id` (Guid, PK)
- `ContentType` (enum: BlogPost, SocialPost, Thread, VideoDescription)
- `Title` (string, nullable — social posts may not have titles)
- `Body` (string — the content text/HTML)
- `Status` (enum: Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived)
- `Metadata` (complex type → jsonb — flexible per-content-type data)
- `ParentContentId` (Guid?, FK to self — for repurposed content relationships)
- `TargetPlatforms` (List<PlatformType> — which platforms to publish to)
- `ScheduledAt` (DateTimeOffset?, when to publish)
- `PublishedAt` (DateTimeOffset?, when actually published)
- `CreatedAt`, `UpdatedAt` (DateTimeOffset, auditing)

**ContentMetadata (jsonb complex type):**
- `Tags` (List<string>)
- `SeoKeywords` (List<string>)
- `PlatformSpecificData` (Dictionary<string, string> — platform-specific formatting hints)
- `AiGenerationContext` (string — prompt/context that generated this content)
- `TokensUsed` (int? — LLM tokens consumed for generation)
- `EstimatedCost` (decimal? — LLM cost for generation)

### Platform
- `Id` (Guid, PK)
- `Type` (enum: TwitterX, LinkedIn, Instagram, YouTube)
- `DisplayName` (string)
- `IsConnected` (bool)
- `AccessToken` (string, encrypted via Data Protection API)
- `RefreshToken` (string, encrypted)
- `TokenExpiresAt` (DateTimeOffset?)
- `RateLimitState` (jsonb complex type — remaining calls, reset time)
- `LastSyncAt` (DateTimeOffset?)
- `Settings` (jsonb complex type — platform-specific config)

### BrandProfile
- `Id` (Guid, PK)
- `Name` (string — profile name, e.g., "Matt Kruczek - AI Thought Leader")
- `ToneDescriptors` (List<string> — e.g., ["professional", "authoritative", "approachable"])
- `StyleGuidelines` (string — prose description of writing style)
- `VocabularyPreferences` (jsonb — preferred terms, avoid terms)
- `Topics` (List<string> — focus areas)
- `PersonaDescription` (string — who the brand represents)
- `ExampleContent` (List<string> — few-shot examples for AI prompts)
- `IsActive` (bool)

### ContentCalendarSlot
- `Id` (Guid, PK)
- `ScheduledDate` (DateOnly)
- `ScheduledTime` (TimeOnly?)
- `Theme` (string? — weekly/monthly theme)
- `ContentType` (ContentType — what type of content for this slot)
- `TargetPlatform` (PlatformType)
- `ContentId` (Guid?, FK — assigned content, if any)
- `IsRecurring` (bool)
- `RecurrencePattern` (string? — cron-like pattern)

### AuditLogEntry
- `Id` (Guid, PK)
- `EntityType` (string)
- `EntityId` (Guid)
- `Action` (string — "StatusChanged", "Created", "Updated", etc.)
- `OldValue` (string?)
- `NewValue` (string?)
- `Timestamp` (DateTimeOffset)
- `Details` (string? — additional context)

### User
- `Id` (Guid, PK)
- `Email` (string)
- `DisplayName` (string)
- `Settings` (jsonb complex type — user preferences)
- `CreatedAt` (DateTimeOffset)

## Database Configuration

### PostgreSQL with EF Core 10
- Complex types with `ToJson()` for all jsonb columns (EF 10 recommended pattern)
- GIN indexes with `jsonb_path_ops` on frequently queried jsonb columns
- ASP.NET Data Protection API for OAuth token encryption (value converters)
- Auditable entity interceptor for automatic CreatedAt/UpdatedAt
- Seed data: default BrandProfile, platform configurations

### Key EF Configurations
- Content: TPH discriminator on ContentType, self-referential FK for ParentContentId
- Platform: unique index on Type (one connection per platform)
- ContentCalendarSlot: composite index on (ScheduledDate, TargetPlatform)

## API Skeleton

### Endpoints (foundation only — other splits add their own)
- `GET /health` — health check (DB connectivity, basic status)
- `GET /api/content` — list content (paginated, filterable)
- `GET /api/content/{id}` — get single content
- `POST /api/content` — create content draft
- `PUT /api/content/{id}` — update content
- `DELETE /api/content/{id}` — soft delete (archive)

### Cross-Cutting
- Global exception handler → Result<T> responses
- FluentValidation pipeline behavior in MediatR
- Serilog structured logging (console + file sinks)
- Swagger/OpenAPI with XML documentation
- CORS for Angular dev server (localhost:4200)
- Request/response logging behavior

## Docker Compose

### Services
1. **api** — .NET 10 API (multi-stage build, Ubuntu Chiseled runtime image ~110MB)
2. **db** — PostgreSQL 17 Alpine with health check (`pg_isready`)
3. **angular** — Angular 19 dev server with hot reload

### Configuration
- `docker-compose.yml` — base/production config
- `docker-compose.override.yml` — dev overrides (volume mounts, environment)
- `.env` file for secrets (gitignored)
- Volume: named volume for PostgreSQL data persistence
- Network: bridge network, services reference by name
- Health checks: PostgreSQL uses `pg_isready`, API depends on db health

### Synology Deployment
- Intel x86, 8GB+ RAM — no constraints
- Build images externally (dev machine or CI), pull pre-built on NAS
- Use `/volume1/docker/personal-brand-assistant/` for volumes

## Angular Foundation

### App Shell
- Standalone components (no NgModules)
- App layout with sidebar navigation + top header
- Lazy-loaded feature routes:
  - `/dashboard` — home/overview
  - `/content` — content workspace
  - `/calendar` — content calendar
  - `/analytics` — performance analytics
  - `/platforms` — platform connections
  - `/settings` — configuration

### Design System with PrimeNG
- PrimeNG component library
- Custom theme (SCSS variables for brand colors)
- Shared components: page header, card wrapper, status badge, loading spinner
- NgRx signals for state management (app-wide store setup)

### API Integration
- Central API service with HttpClient
- Environment-based API URL configuration
- HTTP interceptor for error handling

## Testing Setup
- **Framework:** xUnit
- **Mocking:** Moq
- **Integration:** WebApplicationFactory<Program> with Testcontainers for PostgreSQL
- **Coverage:** Coverlet with `dotnet test --collect:"XPlat Code Coverage"`
- **Minimum:** 80% coverage target
- **Test organization:** Mirror source structure in tests directory

## Definition of Done
- Solution builds with `dotnet build`
- All tests pass with `dotnet test`
- Docker Compose brings up all 3 services
- PostgreSQL migrations apply cleanly
- Health check endpoint returns 200
- Swagger UI accessible at /swagger
- Angular app serves at localhost:4200 with PrimeNG shell
- At least one Content CRUD operation works end-to-end (API → DB → response)
- Serilog outputs structured logs
