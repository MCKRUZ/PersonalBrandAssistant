# Integration Notes — OpenAI Review (GPT-5.2)

## Integrating

### 1. API Key Authentication (#1, #15)
**Why:** Synology NAS is LAN-accessible. No auth = anyone on network can call APIs.
**Action:** Add simple API key auth middleware. Single shared key stored in User Secrets/env var. Protects all endpoints except `/health`.

### 2. UUIDv7 Generation (#2)
**Why:** .NET `Guid.NewGuid()` is v4. Need explicit v7 strategy for temporal locality.
**Action:** Use `Guid.CreateVersion7()` available in .NET 10. Specify in domain entity base class.

### 3. TargetPlatforms Storage (#4)
**Why:** Underspecified mapping will cause issues during implementation.
**Action:** Use PostgreSQL array type (`PlatformType[]`) with Npgsql native enum array mapping + GIN index. Simpler than join table for 4 possible values.

### 4. Content Status State Machine (#6)
**Why:** Transitions are referenced but never defined. Essential for workflow engine (split 02).
**Action:** Add explicit transition table in domain layer. Enforce in `Content.TransitionTo(status)` method.

### 5. Optimistic Concurrency (#7)
**Why:** Agent + UI concurrent updates are inevitable. Lost updates = data corruption.
**Action:** Add `xmin` concurrency token to Content, Platform, BrandProfile. Return 409 on conflict.

### 6. Soft Delete Query Filter (#9)
**Why:** Without explicit filter, archived content leaks into active views.
**Action:** Add EF global query filter excluding Archived status. Provide `.IgnoreQueryFilters()` for analytics.

### 7. Audit Log Policy (#10)
**Why:** Storing OldValue/NewValue as arbitrary strings risks leaking secrets.
**Action:** Define strict policy: structured JSON, never log encrypted fields, 90-day retention, size cap per entry.

### 8. Result<T> + ProblemDetails (#11, #12)
**Why:** Single Error string is too thin. No HTTP status mapping defined.
**Action:** Expand to `Result<T>` with `ErrorCode` enum + `Errors` list. Map to `application/problem+json`. Define validation error structure.

### 9. Token Encryption Approach (#13)
**Why:** Value converter auto-decrypts on every materialization — security risk.
**Action:** Store as encrypted byte[] columns. Decrypt only via `IEncryptionService.Decrypt()` when needed for API calls. No value converter.

### 10. Data Protection Single Registration (#20)
**Why:** Duplicate registration between Infrastructure and API.
**Action:** Infrastructure owns all DP configuration. API just calls `AddInfrastructure()`.

### 11. Seed Data Approach (#21)
**Why:** `HasData` vs runtime seed have different tradeoffs.
**Action:** Use runtime seed (conditional on empty tables) in a hosted service. More flexible, environment-aware.

### 12. Docker Compose Split (#22)
**Why:** Angular dev server in prod compose is wrong.
**Action:** `docker-compose.yml` = prod (API + DB + nginx serving Angular static). `docker-compose.override.yml` = dev overrides (Angular dev server, dotnet watch).

### 13. Pagination Strategy (#18)
**Why:** Undefined pagination will become a problem.
**Action:** Keyset pagination using `(CreatedAt, Id)` cursor. Max page size 50. Default sort by CreatedAt descending.

### 14. Timezone Strategy (#24)
**Why:** `DateOnly/TimeOnly` on calendar slots without timezone = ambiguous.
**Action:** All persistence in UTC (DateTimeOffset). Calendar slots add `TimeZoneId` (string, IANA). UI converts to user timezone. Recurrence evaluated in user's timezone.

### 15. Recurrence Pattern Clarification (#5)
**Why:** Cron dialect and interaction with scheduled date/time unclear.
**Action:** Standard 5-field cron (NCrontab library). ScheduledDate/Time = first occurrence. Cron pattern generates subsequent slots. Evaluate in user timezone.

### 16. GIN Index Targeting (#17)
**Why:** Blanket GIN on whole jsonb is wasteful.
**Action:** Defer GIN indexes until query patterns emerge. Add targeted expression indexes only when specific queries are identified. Initial: index only `Tags` path in Content.Metadata.

### 17. Logging Strategy (#19)
**Why:** File logging in containers fills disk.
**Action:** Console sink (stdout) primary for Docker log aggregation. File sink only in development. Bind mount `/logs` volume with 30-day rotation. Exclude sensitive fields via Serilog destructuring policy.

### 18. Domain Events — Document Limitations (#8)
**Why:** In-process events lose on crash, but outbox is premature for foundation.
**Action:** Use MediatR notifications (in-process) for now. Document limitation. Add TODO for outbox pattern in split 02 if needed.

## Not Integrating

### TPH Per-Subtype Invariants (#3)
**Why not:** Valid concern but manageable. FluentValidation per command handles subtype-specific rules. DB-level enforcement not needed for single-user app.

### Blog Publisher Interface (#23)
**Why not:** Out of scope for foundation. That's split 04/05 (Platform Integration / Content Creation).

### Outbox Pattern (#8 — full implementation)
**Why not:** Premature optimization for foundation. Single-user, single-instance. Document the limitation and revisit in split 02.

### Swagger Auth Guard (#16)
**Why not:** Dev-only with `IsDevelopment()` check is sufficient. The API key auth middleware added in #1 will also protect Swagger in non-dev environments.
