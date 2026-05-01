# Code Review Interview — section-10-di-wiring

## Review Findings

### MINOR-1 — Concrete `SidecarClient` resolvable from DI — decorator bypass risk (AUTO-FIX)
**Decision:** Auto-fixed  
**Action:** Added comment on `AddSingleton<SidecarClient>()` explaining it's for decorator wiring only and consumers must depend on `ISidecarClient`. Guards against future developer accidentally injecting concrete type and bypassing `ObservabilityMiddleware`.

### MINOR-2 — Double-default for `SkillsPath` across 3 files (AUTO-FIX)
**Decision:** Auto-fixed  
**Action:** Changed `SkillOptions.SkillsPath` default from `Path.Combine(AppContext.BaseDirectory, "skills")` to `""`. `SkillRegistry` is now the single source of truth for the fallback path. Updated the property doc comment to reflect this.

### MINOR-3 — OTel version skew: `AspNetCore` 1.11.1 vs others at 1.11.2 (AUTO-FIX)
**Decision:** Auto-fixed  
**Action:** Added XML comment on the `OpenTelemetry.Instrumentation.AspNetCore` package reference in both Api.csproj explaining the intentional version gap (1.11.2 not published for that package). Prevents future devs from investigating a false alarm.

### NITPICK-1 — MCP mode has no OTel pipeline (LET GO)
**Decision:** Let go  
**Rationale:** MCP mode is a CLI transport. Span no-ops via null-conditional `?.` are acceptable. OTel tracing for MCP can be added when there's a real need.

### NITPICK-2 — Non-thread-safe `Dictionary` in scoped `ContextBudgetTracker` (LET GO)
**Decision:** Let go  
**Rationale:** Scoped lifetime = single request = single-threaded under current patterns. If parallel token recording becomes a requirement, switch to `ConcurrentDictionary`.

## Result

3 auto-fixes applied. 2 items let go. Implementation approved.
