# Section-05 Code Review — Snapshot Poller

**Reviewer verdict:** Solid, careful. Both deliberate deviations justified and correct. Highest-value safety invariant (revoked-only deactivation) verified correct.

## Verified correct
- **Revoked-vs-Transient (HIGHEST VALUE):** only `Revoked` sets `IsActive=false`; `Transient` only logs+returns. `OAuthRefreshResult.Fail` always sets a non-null reason — no null path. Both separate tests present with opposite `IsActive` assertions.
- **Deviation 1 (provider.RefreshAsync directly):** confirmed the coordinator would wrongly deactivate Instagram (null refresh token short-circuit) and drops `FailureReason`. Self-persist mirrors the coordinator's persist exactly (re-encrypt access + rotated refresh, set expiry).
- **Deviation 2 (no transaction, account-row sentinel):** crash-safety holds — account row written last in its own SaveChanges after videos commit. Splitting stale-delete and insert into separate SaveChanges correctly dodges the EF/Postgres insert-before-delete unique-key hazard.
- **Host-local date:** `now.LocalDateTime` used for both guard and rows; no UtcDateTime mixing.
- **Loop safety + BackgroundService pattern:** per-credential try/catch; disabled platforms skipped; only active Analytics creds polled; matches `DigestService`.

## Findings

| # | Severity | Category | Finding |
|---|----------|----------|---------|
| 1 | HIGH | correctness (Postgres) | Video rows written with a plain `AddRange`, no dedup. If a platform API returns the same `VideoId` twice in one list (pagination overlap, common on YT/TikTok), the video SaveChanges hits the unique index → swallowed by the per-credential catch → no account row → the platform re-fails EVERY day forever (silent permanent data loss). InMemory can't catch it. |
| 2 | HIGH | test gap | The self-heal stale-clear branch never executes in any test (all start empty → `staleVideos.Count==0`). The central idempotency guarantee is asserted only in prose. No two-run test proving re-poll yields exactly 2 video + 1 account (not 4). |
| 3 | MEDIUM | test quality | `Poller_UsesHostLocalDate` compares `SnapshotDate` to the identical `now.LocalDateTime` expression the code uses → tautological; a regression to `UtcDateTime` still passes on a UTC CI host. |
| 4 | MEDIUM | spec drift | "Retry tomorrow" (plan step b) is violated — the hourly loop re-polls a failing platform every hour until midnight (~19 attempts/day), risking hammering a throttled endpoint (the Transient case). |
| 5 | LOW | (non-issue) | Reviewer flagged multiple active Analytics creds per platform collapsing to one — but section-02's `(Platform, Purpose)` filtered unique index already enforces one active analytics cred per platform, so the scenario can't occur. |
| 6 | LOW | latent | If a future provider *throws* from `RefreshAsync` instead of returning `Fail(Revoked)`, the catch leaves `IsActive` true (safe default — never wrongly deactivates). All shipped providers catch. |
