# Phase 02 — Workflow Engine Research

## Codebase Analysis

### Existing Foundation (Phase 01)

#### Domain Layer
- **Content Entity** — Full state machine already implemented via `AllowedTransitions` dictionary and `TransitionTo()` method. States: Draft → Review → Approved → Scheduled → Publishing → Published → Failed → Archived. Raises `ContentStateChangedEvent` on transitions.
- **EntityBase** — UUIDv7 via `Guid.CreateVersion7()`, domain event management
- **AuditableEntityBase** — CreatedAt/UpdatedAt timestamps
- **AuditLogEntry** — Captures entity changes (type, ID, action, old/new JSON values, timestamp, details). Max 4096 chars per value.
- **ContentCalendarSlot** — Already exists with ScheduledDate, ScheduledTime, TimeZoneId, Theme, ContentType, TargetPlatform, ContentId FK, IsRecurring, RecurrencePattern
- **Platform** — EncryptedAccessToken/RefreshToken, TokenExpiresAt, RateLimitState, LastSyncAt, Settings
- **User** — Email, DisplayName, TimeZoneId, Settings (includes `AutonomyLevel DefaultAutonomyLevel`)

#### Value Objects
- **UserSettings** — Already has `AutonomyLevel DefaultAutonomyLevel` (default: Manual)
- **ContentMetadata** — Tags, SeoKeywords, PlatformSpecificData, AiGenerationContext, TokensUsed, EstimatedCost
- **PlatformRateLimitState** — RemainingCalls, ResetAt, WindowDuration
- **PlatformSettings** — DefaultHashtags, MaxPostLength, AutoCrossPost

#### Enums
- **ContentStatus**: Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived
- **ContentType**: BlogPost, SocialPost, Thread, VideoDescription
- **PlatformType**: TwitterX, LinkedIn, Instagram, YouTube
- **AutonomyLevel**: Manual, Assisted, SemiAuto, Autonomous

#### Application Layer
- **Result<T>** — Success/Failure with ErrorCode enum (None, ValidationFailed, NotFound, Conflict, Unauthorized, InternalError)
- **PagedResult<T>** — Cursor-based pagination
- **MediatR CQRS** — Commands/Queries with handlers
- **FluentValidation** — Pipeline behavior for validation
- **LoggingBehavior** — Request logging with timing, sensitive field redaction
- **IApplicationDbContext** — DbSets for all entities
- **IDateTimeProvider** — Abstraction for time
- **IEncryptionService** — Data Protection API wrapper

#### Infrastructure Layer
- **EF Core 10 + Npgsql** — PostgreSQL with JSONB, array types, xmin concurrency
- **AuditableInterceptor** — Auto-sets CreatedAt/UpdatedAt
- **AuditLogInterceptor** — Auto-logs all entity changes (excludes sensitive fields)
- **AuditLogCleanupService** — BackgroundService, runs every 24h, deletes entries older than 90 days
- **DataSeeder** — Seeds default BrandProfile, Platforms, User on startup
- **EncryptionService** — ASP.NET Core Data Protection API

#### API Layer
- **Minimal APIs** with endpoint groups
- **ApiKeyMiddleware** — SHA256 timing-safe comparison, exempt paths: /health, OPTIONS
- **GlobalExceptionHandler** — Returns ProblemDetails
- **ResultExtensions** — Maps Result<T> to HTTP status codes
- **JsonStringEnumConverter** — Already configured in Program.cs

#### Test Infrastructure
- **xUnit** + Moq + Testcontainers.PostgreSql
- **CustomWebApplicationFactory** — Configurable environment, test API key, disables DataSeeder/AuditLogCleanup
- **PostgresFixture** — Collection fixture, unique connection strings per test
- **TestEntityFactory** — Static factory methods including CreateArchivedContent (full state transitions)

### Key Insight: What Already Exists vs What's Needed

**Already exists:**
- Basic content state machine (hard-coded transitions in Content entity)
- Audit logging (automatic via interceptor, but not workflow-aware)
- AutonomyLevel enum and UserSettings.DefaultAutonomyLevel
- ContentCalendarSlot entity
- BackgroundService pattern (AuditLogCleanupService as reference)

**Needs to be built:**
- Configurable transition rules (per content type, per platform, per autonomy level)
- Autonomy dial configuration system with override hierarchy
- Approval service (approve, reject, edit-and-approve, batch)
- Workflow engine service (orchestrates transitions with rules)
- Content scheduler (processes scheduled content at the right time)
- Background job infrastructure (Channel<T> + BackgroundService consumers)
- Notification system (entity, service, SignalR hub)
- Extended audit trail with queryable API

---

## Technology Decisions

### 1. State Machine: Stateless NuGet (v5.20.1)

**Decision: Use Stateless**

Stateless provides a clean, testable state machine without overhead. Key features:
- External state storage pattern: `new StateMachine<TState, TTrigger>(() => entity.Status, s => entity.Status = s)` — maps directly to EF Core entity
- Guard clauses via `PermitIf()` — perfect for autonomy-level conditional transitions
- Async support for entry/exit actions
- Graph export (DOT/Mermaid) for documentation
- Zero dependencies, .NET Standard compatible

**Integration plan:** Wrap the existing Content.TransitionTo() with a Stateless-powered `WorkflowEngine` service that adds configurable guards based on autonomy settings. The domain entity keeps its validation, but the application layer adds the business rules.

### 2. Background Jobs: BackgroundService + Channel<T>

**Decision: Built-in BackgroundService with Channel<T> for queuing**

For a single-user self-hosted Docker app, no need for Hangfire or Quartz.NET.

- **Channel<T>** (bounded, capacity ~100) for in-process async queuing
- **BackgroundService** consumers read from channels
- **PeriodicTimer** for recurring work (check for due scheduled content, poll APIs)
- **Database-backed durability:** Write work items to PostgreSQL first, then push to channel. Rehydrate unprocessed items on startup.

**Job types:**
- ScheduledPublishProcessor — checks for content where ScheduledAt <= now and Status == Scheduled
- RetryFailedProcessor — retries failed publishes with exponential backoff
- ContentExpiryProcessor — archives expired content
- EngagementCheckProcessor — periodic engagement data refresh

### 3. Queue: In-Process Channel<T>

**Decision: Channel<T> (bounded)**

No external queue needed. Pattern:
1. MediatR handler validates and persists work item to DB
2. Handler writes to Channel<T>
3. BackgroundService reads from channel, processes, updates DB
4. On crash recovery: startup rehydrates unprocessed items from DB

### 4. Notifications: SignalR

**Decision: SignalR for real-time, PostgreSQL for persistence**

- SignalR hub for real-time push to Angular frontend
- Notification entity in PostgreSQL for offline resilience
- On reconnect, load unread notifications from DB
- Single-server = no backplane needed

### 5. Testing: xUnit + Testcontainers + TimeProvider

**Decision: Follow existing patterns, add TimeProvider for time control**

- Extract logic from BackgroundServices into testable scoped services
- Use TimeProvider (built into .NET 8+) to control time in scheduled tests
- Testcontainers for integration tests with real PostgreSQL
- Keep existing CustomWebApplicationFactory pattern
