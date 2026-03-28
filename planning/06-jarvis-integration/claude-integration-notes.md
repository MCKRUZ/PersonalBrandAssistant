# External Review Integration Notes

## Review Source: OpenAI GPT-5.2 (27 findings)

## Integrating (high-value fixes)

### 1. MCP process model (Finding #1, #2) -- CRITICAL
**Problem:** Plan says OpenClaw spawns MCP via `dotnet run --project /path/...` which requires source code in Jarvis stack. Also, MCP mode "replaces" HTTP server, breaking monitors/HUD.
**Fix:** MCP runs as a separate process alongside the main API, not instead of it. Two options:
- Option A: `dotnet publish` produces a standalone executable; OpenClaw spawns the published binary
- Option B: Run MCP as a small sidecar container in PBA stack with HTTP transport; Jarvis connects over LAN IP

Going with **Option A** (published executable, stdio transport). Update Section 3.5 and 7.1.

### 2. SSE broadcast + snapshot (Findings #3, #4, #5) -- HIGH
**Problem:** Single Channel reader load-balances among consumers; no initial state on connect.
**Fix:** Implement a broadcast hub pattern (one Channel per connection). On connect, send a `pipeline:snapshot` event with current state before streaming deltas. Document single-instance constraint for v1 (no Redis pub/sub needed yet).

### 3. API key scoping (Finding #10) -- HIGH
**Problem:** One API key for reads, writes, SSE, and MCP.
**Fix:** Introduce scoped keys: `pba-readonly` (monitor, HUD), `pba-write` (MCP tools). Enforce in middleware by checking key scope against endpoint.

### 4. Idempotency for write tools (Finding #20) -- HIGH
**Problem:** Voice retries and LLM tool retries can duplicate actions.
**Fix:** Add `clientRequestId` parameter to all write MCP tools. Store processed IDs with 5-minute TTL. Return cached result for duplicates.

### 5. Stage enum normalization (Finding #21) -- MEDIUM
**Problem:** Inconsistent stage names between MCP tools, endpoints, and monitor.
**Fix:** Define canonical `PipelineStage` enum used everywhere. Map to UI-friendly labels in HUD only.

### 6. Monitor threshold truth table (Finding #7) -- MEDIUM
**Problem:** Ambiguous queue/calendar monitor logic.
**Fix:** Explicit truth table:
- HasScheduledIn48h AND queueDepth >= 3 -> healthy
- HasScheduledIn48h AND queueDepth < 3 -> healthy (info: low buffer)
- No scheduled in 48h AND queueDepth > 0 -> degraded (medium)
- No scheduled in 48h AND queueDepth == 0 -> degraded (medium)

### 7. Engagement anomaly direction (Finding #23) -- MEDIUM
**Problem:** No distinction between viral (good) and low engagement (bad).
**Fix:** Add `direction: "positive" | "negative"` and `confidence` to anomaly objects. Only alert on negative anomalies; positive anomalies go to briefing as highlights.

### 8. Audit trail for MCP actions (Finding #25) -- MEDIUM
**Problem:** No logging of cross-stack tool invocations.
**Fix:** All MCP write tools log to the existing audit trail with `actor: "jarvis/openclaw"`, tool name, parameters (redacted), outcome, and correlation ID.

### 9. MCP tools must go through validation (Finding #19) -- MEDIUM
**Problem:** Direct service layer calls bypass validation/auth/logging middleware.
**Fix:** MCP tools call through the same service interfaces that HTTP endpoints use, which already include validation. Add explicit authorization scope check in each MCP tool method.

### 10. Timeout normalization (Finding #17) -- LOW
**Problem:** Inconsistent timeouts (2s, 5s, 10s, 30s).
**Fix:** Standardize: health checks 5s, monitor data endpoints 10s, HUD proxy 10s, MCP tools 30s, SSE keepalive 30s.

## NOT Integrating (and why)

### Finding #9: TLS between stacks
**Why not:** Both stacks run on the same physical machine (Synology NAS). LAN IP traffic doesn't leave the box. Adding TLS/mTLS is overhead without meaningful security benefit for a single-user home lab. Network segmentation is sufficient.

### Finding #11: CSRF on BFF
**Why not:** Jarvis HUD uses API token auth (not cookies). BFF routes require the server-side API token which the browser never sees. No cookie-based sessions = no CSRF surface.

### Finding #12: Redaction policy for briefings
**Why not:** This is a single-user system. The user is the only person who sees briefings, alerts, and dashboard. No multi-tenant data leakage concern. Content sensitivity filtering adds complexity without value here.

### Finding #16: SSE proxy fragility
**Why not:** Jarvis HUD runs on Node.js runtime (not serverless/edge). The BFF is a simple proxy. This works fine for a single-user dashboard. If issues arise, can switch to direct browser-to-PBA SSE later.

### Finding #18: Rate limiting / circuit breaker
**Why not:** Single user, single monitor instance, single HUD. No thundering herd risk at this scale. The monitor's jitter and interval scheduling already prevent simultaneous requests.

### Finding #22: Calendar conflict rules
**Why not:** The existing content calendar service already has conflict detection logic. MCP tools delegate to that service. No need to re-specify rules in the integration plan.

### Finding #27: Secret management docs
**Why not:** Already covered by project conventions (User Secrets for dev, Azure Key Vault for prod per CLAUDE.md). Not integration-specific.
