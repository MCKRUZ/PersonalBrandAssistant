# TDD Plan — 01 Foundation & Infrastructure

## Testing Stack
- **Unit tests:** xUnit + Moq
- **Integration tests:** xUnit + Testcontainers (real PostgreSQL)
- **API tests:** WebApplicationFactory<Program>
- **Coverage target:** 80%+

Each section below mirrors `claude-plan.md` and specifies tests to write BEFORE implementing.

---

## 2. Solution Structure

No tests — scaffolding only. Verify: solution builds, all projects reference correctly.

---

## 3. Domain Layer

### Entity Base Class
- Test: entity ID is generated as valid UUIDv7 (version byte = 7, timestamp extractable)
- Test: two entities created sequentially have IDs that sort chronologically

### Content — Status State Machine
- Test: new Content defaults to Draft status
- Test: Draft → Review transition succeeds
- Test: Draft → Archived transition succeeds
- Test: Draft → Published transition throws InvalidOperationException
- Test: Review → Draft transition succeeds (send back for edits)
- Test: Review → Approved transition succeeds
- Test: Approved → Scheduled transition succeeds
- Test: Approved → Draft transition succeeds (revert)
- Test: Scheduled → Publishing transition succeeds
- Test: Scheduled → Draft transition succeeds (unschedule)
- Test: Publishing → Published transition succeeds
- Test: Publishing → Failed transition succeeds
- Test: Publishing → Draft throws (can't go back mid-publish)
- Test: Published → Archived transition succeeds
- Test: Published → Draft throws (must archive first)
- Test: Failed → Draft transition succeeds (retry)
- Test: Failed → Archived transition succeeds
- Test: Archived → Draft transition succeeds (restore)
- Test: Archived → Published throws
- Test: TransitionTo raises ContentStateChangedEvent with correct old/new status

### Content — TargetPlatforms
- Test: Content created with multiple PlatformType values stores them correctly
- Test: Content with empty TargetPlatforms array is valid

### ContentMetadata
- Test: ContentMetadata with all fields populated creates valid object
- Test: ContentMetadata with null optional fields (AiGenerationContext, TokensUsed, EstimatedCost) is valid
- Test: Tags and SeoKeywords are initialized as empty lists (not null)

### Platform
- Test: Platform created with all required fields
- Test: EncryptedAccessToken/RefreshToken are byte arrays (not auto-decrypted strings)

### BrandProfile
- Test: BrandProfile with valid fields creates successfully
- Test: ToneDescriptors and Topics initialize as empty lists

### ContentCalendarSlot
- Test: Slot with valid IANA TimeZoneId creates successfully
- Test: Slot with RecurrencePattern stores cron string
- Test: Non-recurring slot has null RecurrencePattern

### AuditLogEntry
- Test: AuditLogEntry created with required fields
- Test: OldValue/NewValue accept null

### User
- Test: User created with valid TimeZoneId
- Test: User Settings is complex type (not null by default)

### Enums
- Test: ContentType has exactly 4 values (BlogPost, SocialPost, Thread, VideoDescription)
- Test: ContentStatus has exactly 8 values
- Test: PlatformType has exactly 4 values
- Test: AutonomyLevel has exactly 4 values

---

## 4. Application Layer

### Result<T>
- Test: Result.Success(value) → IsSuccess=true, Value set, ErrorCode=None, Errors empty
- Test: Result.Failure(errorCode, errors) → IsSuccess=false, Value=null, ErrorCode set, Errors populated
- Test: Result.NotFound(message) → ErrorCode=NotFound, single error message
- Test: Result.ValidationFailure(errors) → ErrorCode=ValidationFailed, multiple errors
- Test: Result.Conflict(message) → ErrorCode=Conflict

### ValidationBehavior
- Test: valid request passes through to handler, handler result returned
- Test: invalid request short-circuits, returns Result.ValidationFailure with errors
- Test: request with no validators passes through (no validators registered)

### LoggingBehavior
- Test: successful request logs start and completion with duration
- Test: failed request logs start and failure
- Test: sensitive fields (matching *Token*, *Password*, *Secret*) are not logged

### CreateContentCommand
- Test: valid command creates content with Draft status, returns new ID
- Test: command with missing Body fails validation
- Test: command with invalid ContentType fails validation
- Test: created content has UUIDv7 ID and CreatedAt set

### UpdateContentCommand
- Test: update Draft content succeeds, fields updated
- Test: update Review content succeeds
- Test: update Published content returns failure (not editable)
- Test: update non-existent content returns NotFound
- Test: update with stale version returns Conflict (concurrency)

### DeleteContentCommand
- Test: delete existing content transitions to Archived
- Test: delete already-Archived content returns appropriate result
- Test: delete non-existent content returns NotFound

### GetContentQuery
- Test: existing content returned with all fields
- Test: non-existent ID returns NotFound
- Test: archived content excluded by default (query filter)

### ListContentQuery
- Test: returns paginated results with default sort (CreatedAt desc)
- Test: filters by ContentType correctly
- Test: filters by Status correctly
- Test: keyset pagination returns correct next page (cursor-based)
- Test: max page size capped at 50
- Test: empty result set returns empty list (not error)
- Test: archived content excluded from results

### Validators (FluentValidation)
- Test: CreateContentValidator — Body required, ContentType must be valid enum
- Test: UpdateContentValidator — Id required, at least one field to update
- Test: ListContentValidator — page size between 1 and 50

---

## 5. Infrastructure Layer

### ApplicationDbContext — Entity Configuration
- Test: Content table uses TPH with ContentType discriminator
- Test: Content.Metadata maps to jsonb column
- Test: Content.TargetPlatforms maps to PlatformType array with GIN index
- Test: Content has xmin concurrency token configured
- Test: Content has global query filter excluding Archived status
- Test: Platform.Type has unique constraint
- Test: ContentCalendarSlot has composite index on (ScheduledDate, TargetPlatform)

### Database Migration
- Test: migrations apply cleanly to empty database
- Test: all tables created with correct columns and types
- Test: indexes exist (TargetPlatforms GIN, CalendarSlot composite)

### Encryption Service
- Test: Encrypt(plaintext) returns non-null byte array
- Test: Decrypt(encrypted) returns original plaintext
- Test: Encrypt same plaintext twice produces different ciphertext (Data Protection uses random IV)
- Test: Decrypt with tampered data throws
- Test: round-trip: Encrypt then Decrypt preserves original string

### Auditable Interceptor
- Test: inserting entity sets CreatedAt to current UTC time
- Test: inserting entity sets UpdatedAt to current UTC time
- Test: updating entity updates UpdatedAt but not CreatedAt
- Test: entities not implementing IAuditable are unaffected

### Audit Log Interceptor
- Test: modifying a Content entity creates AuditLogEntry
- Test: AuditLogEntry contains correct EntityType and EntityId
- Test: encrypted fields (EncryptedAccessToken, EncryptedRefreshToken) excluded from OldValue/NewValue
- Test: OldValue/NewValue are structured JSON
- Test: OldValue/NewValue truncated at 4KB

### Seed Data (DataSeeder hosted service)
- Test: seeder creates default BrandProfile when table empty
- Test: seeder creates 4 Platform records (one per PlatformType) when table empty
- Test: seeder creates default User when table empty
- Test: seeder does NOT duplicate records on second run (idempotent)

### Audit Log Cleanup Service
- Test: entries older than 90 days are deleted
- Test: entries within 90 days are preserved
- Test: cleanup runs without error on empty table

### Optimistic Concurrency
- Test: concurrent update to same Content entity → DbUpdateConcurrencyException
- Test: concurrent update to same Platform entity → DbUpdateConcurrencyException
- Test: sequential updates with fresh reads succeed

### Global Query Filter
- Test: querying Content excludes Archived by default
- Test: IgnoreQueryFilters() includes Archived content
- Test: GetContentQuery for an archived ID returns NotFound (filter active)

---

## 6. API Layer

### API Key Middleware
- Test: request with valid X-Api-Key header → passes through (200 on health/ready)
- Test: request with invalid X-Api-Key → 401 ProblemDetails
- Test: request with missing X-Api-Key → 401 ProblemDetails
- Test: GET /health (liveness) → 200 without API key (exempt)
- Test: GET /health/ready → 401 without API key (not exempt)

### Content Endpoints
- Test: POST /api/content with valid body → 201 with created ID
- Test: POST /api/content with invalid body → 400 ProblemDetails with validation errors
- Test: GET /api/content/{id} with existing ID → 200 with content
- Test: GET /api/content/{id} with non-existent ID → 404 ProblemDetails
- Test: GET /api/content with no params → 200 with paginated list
- Test: GET /api/content?contentType=BlogPost → 200 filtered results
- Test: PUT /api/content/{id} with valid body → 200 updated
- Test: PUT /api/content/{id} with stale version → 409 ProblemDetails
- Test: DELETE /api/content/{id} → 200 (soft delete)

### Result-to-HTTP Mapper
- Test: Result.Success maps to 200 with JSON body
- Test: Result.ValidationFailure maps to 400 ProblemDetails
- Test: Result.NotFound maps to 404 ProblemDetails
- Test: Result.Conflict maps to 409 ProblemDetails
- Test: all error responses have content-type application/problem+json

### Global Exception Handler
- Test: unhandled exception → 500 ProblemDetails (no stack trace in Production)
- Test: unhandled exception logged via Serilog

### Health Endpoints
- Test: /health returns 200 when API running
- Test: /health/ready returns 200 when DB connected (with valid API key)
- Test: /health/ready returns 503 when DB unreachable

### Swagger
- Test: /swagger accessible in Development environment
- Test: /swagger returns 404 in Production environment

---

## 7. Docker Environment

No unit tests — verification via docker-compose integration:
- Test: `docker compose build` succeeds without errors
- Test: `docker compose up` starts all 3 services
- Test: API health check returns 200
- Test: Angular app serves on port 4200
- Test: PostgreSQL accepts connections
- Test: Data Protection keys volume persists across restarts

---

## 8. Angular Foundation

### API Service
- Test: GET request sends correct URL with base from environment
- Test: POST request sends body as JSON
- Test: HTTP interceptor adds X-Api-Key header to all requests
- Test: HTTP error interceptor shows toast on 4xx/5xx responses
- Test: 401 response handled (API key invalid)

### Shared Components
- Test: PageHeaderComponent renders title
- Test: PageHeaderComponent renders action buttons when provided
- Test: StatusBadgeComponent renders correct color for each ContentStatus
- Test: LoadingSpinnerComponent shows spinner
- Test: EmptyStateComponent renders message

### Routing
- Test: default route redirects to /dashboard
- Test: lazy routes load correctly (/content, /calendar, etc.)

### NgRx Store
- Test: ui store toggles sidebar state
- Test: ui store persists theme preference
- Test: auth store holds user info

---

## 9. Verification (End-to-End)

- Test: full Docker Compose stack starts, API returns health 200
- Test: create content via API → read it back → verify fields match
- Test: Angular app loads and displays dashboard
- Test: database persists data across container restart
