# Section-02 Review Triage & Decisions

No user interview required — no security decision or product tradeoff. Decisions made autonomously.

## Auto-fixed

### #1 (MEDIUM) — Stability guards for the two new persisted enums
Added `CredentialPurpose_HasStableNumericValues` and `SnapshotScope_HasStableNumericValues` to `ChannelAnalyticsModelTests`. Both enums are persisted as int (`HasConversion<int>()`) and embedded in unique indexes, so a renumber corrupts data — same risk class as `Platform`, now guarded identically.

### #2 (LOW) — Comparer key-order comment
Added a one-line comment on `metricsComparer` noting the JSON-serialize comparison is key-order sensitive and why it's acceptable (write-once snapshots, mirrors `IdeaConfiguration`).

### #4 (LOW) — Positive coexistence test
Added `ChannelMetricSnapshotConfiguration_UniqueIndex_AllowsAccountAndVideoRows_SamePlatformDate` (real-DB): an Account row and a Video row for the same `(Platform, SnapshotDate)` both persist — proves `Scope` genuinely discriminates in the unique key, not just that collisions throw.

## Let go (with rationale)

### #3 (LOW) — Shared fixture, no per-test cleanup
KEPT as-is. The disjoint Platform/date keys are intentional and xUnit serializes methods within a class. Adding respawn-per-test would triple container cost for these already Docker-gated tests with marginal isolation benefit. Documented the convention (future real-DB tests must pick fresh keys) in the section doc.

### #5 (LOW) — `Purpose` persistent DB default
KEPT. Matches the `AddIsMicrosoftSource` precedent (`DEFAULT FALSE`) and is required so the migration can add a NOT NULL column to the existing `PlatformCredentials` rows. Consistent with the repo; no change.
