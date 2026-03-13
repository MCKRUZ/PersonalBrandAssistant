# Section 05 - Code Review Interview Transcript

## Auto-Fixed

### 1. [CRITICAL] Timing-safe API key comparison
Replaced `string.Equals` with SHA256 hash + `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.

### 2. [HIGH] DELETE returns 204 No Content
Changed DELETE endpoint to return `Results.NoContent()` on success instead of `Results.Ok()`.

### 3. [HIGH] pageSize lower bound
Changed `Math.Min(pageSize, 50)` to `Math.Clamp(pageSize, 1, 50)` to prevent zero/negative page sizes.

### 4. [MEDIUM] OPTIONS preflight exemption
Added `HttpMethods.Options` exemption in ApiKeyMiddleware so CORS preflight requests aren't blocked.

### 5. [MEDIUM] HashSet optimization
Changed `ExemptPaths` to use `StringComparer.OrdinalIgnoreCase` and `Contains` instead of LINQ `.Any()`.

## Let Go

### [HIGH] Health readiness probe exemption
Intentional design — `/health/ready` requires API key. Orchestrator can be configured with the key header.

### [HIGH] ToCreatedHttpResult type safety
Only used with `Result<Guid>` currently. Adding a generic constraint or ID selector is premature abstraction.

### [HIGH] UpdateContent silent ID override
Standard pattern for Minimal APIs. The route parameter is authoritative. Separate request DTOs are over-engineering for MVP.

### [MEDIUM] CORS environment-awareness
Will be addressed in Docker/deployment section (section-06).

### [MEDIUM] Test boilerplate duplication
Acceptable for 5 test classes. Base class extraction can come in the testing section (section-08).

### [MEDIUM] ProblemDetails URI canonicalization
Cosmetic — `tools.ietf.org` redirects correctly.

### All LOW items
Let go — Swagger metadata, anonymous types, test project location are acceptable for MVP.
