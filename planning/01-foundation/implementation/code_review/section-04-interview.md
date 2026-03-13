# Section 04 - Code Review Interview Transcript

## Auto-Fixed (applied without user input)

### 1. [HIGH] EncryptionService null validation
Added `ArgumentNullException.ThrowIfNull()` to both `Encrypt` and `Decrypt` methods.
Not changing to `Result<T>` return type since the interface is defined in the Application layer and the section plan doesn't call for it.

### 2. [HIGH] AuditLogCleanupService delay-before-first-run
Moved `Task.Delay` after cleanup logic so first cleanup runs immediately on startup. Added 5-minute retry delay on error to avoid tight error loops.

### 3. [MEDIUM] Missing xmin concurrency tokens
Added `xmin` concurrency token configuration to `ContentCalendarSlotConfiguration` and `UserConfiguration` for consistency with other writable entities.

### 4. [MEDIUM] AuditLogCleanupService uses DateTimeOffset.UtcNow
Injected `IDateTimeProvider` and replaced `DateTimeOffset.UtcNow` for testability.

## Let Go (not fixing)

### [HIGH] No UserId in audit log entries
Deferred — `ICurrentUserService` doesn't exist yet. Will be added when the API/auth layer is implemented (section-05 or later).

### [MEDIUM] Data Protection relative path
Acceptable for development. Will be properly configured with absolute paths in the Docker/deployment section.

### [MEDIUM] Interceptor ordering fragility
Works correctly as-is. EF Core preserves array order for `AddInterceptors`.

### [MEDIUM] Audit exclusion pattern brittleness
Substring matching is pragmatic for MVP. Can evolve to attribute-based approach later.

### [MEDIUM] DataSeeder environment guard
Seeder is idempotent with `AnyAsync` checks. Fine to run in all environments.

### [MEDIUM] No AuditLogCleanupService tests
The cleanup service is straightforward. Integration testing would require complex time manipulation with Testcontainers. Acceptable gap for now.

### All LOW items
Let go — cosmetic or documentation-level concerns.
