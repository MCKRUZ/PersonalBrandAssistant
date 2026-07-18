# Section-02 Code Review — Domain + Persistence

**Reviewer verdict:** Ship-worthy. All three load-bearing invariants land correctly and converge across entity, EF config, generated migration, and the idempotent SQL script:
- `VideoId` non-null sentinel (`""`) → unique index `(Platform, SnapshotDate, Scope, VideoId)` genuinely enforces one Account row per platform-day (no NULLs-distinct trap).
- Composite filtered unique index `(Platform, Purpose) WHERE "IsActive" = true` → one active Publishing + one active Analytics per platform, rejects two of the same purpose. Byte-identical across config / migration / script.
- Enum append-only (0..6 stable, Instagram=7/TikTok=8 appended).
- Migration `Up` drops `IX_PlatformCredentials_Platform`, creates table + composite + both snapshot indexes; `Down` reverts cleanly in dependency-safe order. Script keyed on correct MigrationId `20260717191429_AddChannelAnalytics` / ProductVersion `10.0.7`; column types, index names, filter string all match the EF-emitted DDL.
- jsonb converter/comparer is a faithful clone of `IdeaConfiguration`; snapshot lambda deep-copies. No GIN index (matches plan).
- Migration revert test target `20260617144341_AddIsMicrosoftSource` verified as the immediate predecessor.

## Findings

| # | Severity | Category | Finding |
|---|----------|----------|---------|
| 1 | MEDIUM | test gap | `CredentialPurpose` and `SnapshotScope` are BOTH persisted as int and embedded in unique indexes, so renumbering silently corrupts data/index semantics — same risk class as `Platform`, but neither has a stability-guard test. |
| 2 | LOW | latent wart | `metricsComparer` is key-order sensitive (JSON serialize compares insertion order). Snapshots are write-once so impact is minimal; mirrors `IdeaConfiguration` precedent. Worth a one-line comment. |
| 3 | LOW | test isolation | Real-DB tests share an `IClassFixture` with no per-test cleanup; isolation depends on hand-picked disjoint Platform/date keys. Works (xUnit serializes methods in a class) but brittle. |
| 4 | LOW | test gap | No positive test that an Account-scope and a Video-scope row for the same `(Platform, SnapshotDate)` coexist (proves `Scope` discriminates in the unique key). Cheap to add. |
| 5 | LOW | EF wart | `Purpose` added with persistent DB `DEFAULT 0` while model declares no default — can provoke a spurious `AlterColumn` later. Matches `AddIsMicrosoftSource` precedent. |

## Docker caveat
Real-DB tests (index enforcement + migration apply/revert) could not run this session (no Docker). Reviewer confirms they are written correctly and would pass on real Postgres (fixture mirrors the CutoverTests Testcontainers pattern; `DbUpdateException` is the right expectation; `to_regclass`/`information_schema` assertions correct). InMemory-runnable tests are sufficient for what runs here. Flag for a Docker-enabled run before prod apply.
