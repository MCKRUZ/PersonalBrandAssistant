# Code Review — section-10-di-wiring

## Verdict: Approve with minor items

No CRITICAL or MAJOR issues. Registration order correct, decorator pattern correct, OTel wiring complete, empty-string fallback sound.

---

### MINOR-1 — Concrete `SidecarClient` resolvable from DI — decorator bypass risk

`services.AddSingleton<SidecarClient>()` registers the concrete type publicly. Any future constructor dependency typed as `SidecarClient` instead of `ISidecarClient` would bypass `ObservabilityMiddleware` silently.

**Action:** Auto-fix — add explanatory comment on the registration.

---

### MINOR-2 — Double-default for `SkillsPath` across 3 files

`SkillOptions.SkillsPath` has a C# default of `Path.Combine(AppContext.BaseDirectory, "skills")`. `appsettings.json` overrides it with `""`. `SkillRegistry` then re-computes the same fallback. Three locations for one default — risk of divergence if any one changes.

**Action:** Auto-fix — change `SkillOptions.SkillsPath` default to `""` so `SkillRegistry` is the single source of truth.

---

### MINOR-3 — OTel version skew: `AspNetCore` 1.11.1 vs others at 1.11.2

All other OTel packages are 1.11.2; `OpenTelemetry.Instrumentation.AspNetCore` is 1.11.1 (matches Infrastructure.csproj — likely 1.11.2 has not been published for this package).

**Action:** Auto-fix — add comment noting intentional version gap.

---

### NITPICK-1 — MCP mode has no OTel pipeline (let go)

MCP mode calls `AddInfrastructure` but has no `AddOpenTelemetry()` call. Spans are silently dropped. Acceptable for a CLI transport.

---

### NITPICK-2 — Non-thread-safe `Dictionary` in scoped `ContextBudgetTracker` (let go)

Scoped = single request = single thread under normal patterns. Not an active bug.

---

## Result

3 auto-fixes applied. 2 items let go. Implementation approved.
