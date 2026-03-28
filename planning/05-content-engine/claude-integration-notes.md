# Integration Notes — OpenAI (GPT-5.2) Review

## Integrating

### 1. Docker networking for sidecar URL
**Why:** `ws://localhost:3001/ws` won't work from inside Docker — `localhost` points to the API container itself.
**Change:** Environment-specific config: `ws://sidecar:3001/ws` in Docker profile, `ws://localhost:3001/ws` for local dev.

### 2. AbortAsync needs session/task context
**Why:** Stateless abort is unsafe if ever used with concurrent tasks.
**Change:** `AbortAsync(string? sessionId, CancellationToken ct)` — include session ID parameter.

### 3. Keep transport-level retries
**Why:** Claude Code retries API errors internally, but WS disconnects, sidecar crashes, and CLI failures need app-level handling.
**Change:** Add bounded retries (3 attempts) for connection/transport errors in SidecarClient, distinct from Claude Code's internal retries.

### 4. Source of truth: DB is authoritative
**Why:** Dual source (DB + git repo) creates consistency ambiguity.
**Change:** DB is source of truth. Blog writing flow: sidecar writes files + commits, then API persists body + metadata (commit hash, file path) to DB. If DB save fails, the commit is orphaned but content isn't "published." Add explicit states in metadata.

### 5. Recursive repurposing depth limit
**Why:** Auto-repurposing derivatives could create unbounded trees.
**Change:** Add max tree depth constraint (configurable, default 3). RepurposingService checks depth before creating children.

### 6. Repurposing idempotency key
**Why:** Event replays or status toggles could create duplicate children.
**Change:** Uniqueness constraint on `(ParentContentId, Platform, ContentType)`. Processor is idempotent — skips if child already exists.

### 7. TimeZoneId on ContentSeries
**Why:** RRULE BYHOUR without timezone context breaks across DST shifts.
**Change:** Add `string TimeZoneId` to ContentSeries. Generate occurrences in that zone, store slots in UTC.

### 8. CalendarSlot override linkage
**Why:** `IsOverride` alone doesn't identify which occurrence it replaces.
**Change:** Add `DateTimeOffset? OverriddenOccurrence` to CalendarSlot — the original occurrence timestamp + series ID pair identifies the override target.

### 9. Auto-fill transactional safety
**Why:** Concurrent auto-fill calls could double-assign content.
**Change:** Use `SELECT ... FOR UPDATE SKIP LOCKED` on slots during assignment transaction.

### 10. Strip HTML before brand voice validation
**Why:** Rule-based checks on HTML will false-positive on tags/attributes.
**Change:** Normalize content (strip HTML, decode entities) before running rule checks and LLM scoring.

### 11. LLM-as-judge structured output
**Why:** Parsing free-form LLM text for scores is brittle.
**Change:** Enforce JSON response format in the scoring prompt. Validate with schema. Handle invalid/partial responses gracefully (return error, not crash).

### 12. TrendSuggestion-TrendItem join entity
**Why:** Many-to-many needs an explicit join entity for EF Core and to store per-link metadata.
**Change:** Create `TrendSuggestionItem` join entity with similarity score. Replace `ICollection<TrendItem>` navigation.

### 13. Nullable engagement metrics per platform
**Why:** Not all platforms provide all metrics (e.g., Instagram doesn't expose clicks).
**Change:** Make `Impressions` and `Clicks` nullable on EngagementSnapshot. Document platform availability matrix.

### 14. Engagement snapshot retention
**Why:** 4-hour snapshots for 30 days per post grows fast.
**Change:** Add retention policy: keep hourly for 7 days, daily for 30 days, then delete. Add index on `(ContentPlatformStatusId, FetchedAt DESC)`.

### 15. Auth on API endpoints
**Why:** State-changing endpoints (sidecar tasks, git writes, publishing) need authorization.
**Change:** Note auth requirements per endpoint group. Admin-only for trend refresh, content generation. Server-side autonomy enforcement in commands.

### 16. Sidecar network security
**Why:** Sidecar in agent mode is effectively RCE — should not be exposed.
**Change:** Sidecar on internal Docker network only, no published ports to LAN. Only the .NET API container can reach it.

### 17. Pin Docker image versions
**Why:** `latest` tags are unpredictable on production.
**Change:** Pin TrendRadar and FreshRSS to specific version tags.

## NOT Integrating

### Per-task temp workspace clone for sidecar
**Why not:** Single-user self-hosted app. Concurrent blog writes are extremely unlikely. Git conflicts will be handled by error detection, not per-task cloning. Over-engineering.

### mTLS / shared-secret between API and sidecar
**Why not:** Both run on an internal Docker network on a personal NAS. Attack surface is minimal. If needed later, can add.

### Prompt injection sandboxing (file allowlists, tool restrictions)
**Why not:** This is a personal tool for one user, not multi-tenant. The prompts are system-generated, not user-submitted from untrusted sources. RSS/Reddit content feeds into scoring prompts, not command prompts.

### Outbox pattern / Hangfire / Quartz
**Why not:** Single-user, single-instance deployment. BackgroundService with idempotency guards is sufficient. No need for distributed job infrastructure.

### CSRF protection
**Why not:** Will use JWT token-based auth, not cookie auth. CSRF is a cookie-based attack vector.

### Engagement metrics normalization rules / availability matrix
**Why not:** Good idea but scope creep for this phase. Nullable fields handle the core issue. Normalization can be added in phase 06 (dashboard) when visualizing.

### Concurrency tests for multiplexed sidecar sessions
**Why not:** Single-user app. Tasks are serialized in practice. Basic connection lifecycle tests are sufficient.
