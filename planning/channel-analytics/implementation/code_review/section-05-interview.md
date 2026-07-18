# Section-05 Review Triage & Decisions

No user interview required. The safety invariant is confirmed correct; findings are correctness/coverage fixes + documented design decisions. Decisions autonomous.

## Auto-fixed

### #1 (HIGH) — Dedup video rows by VideoId
Added `.DistinctBy(v => v.VideoId)` before the cap. A duplicate `VideoId` in one poll (pagination overlap) would otherwise violate the `(Platform, SnapshotDate, Scope, VideoId)` unique index on Postgres, fail the whole write (swallowed by the per-credential catch), and leave the platform permanently un-snapshotted. Added `Poller_DeduplicatesVideoRows_ByVideoId`.

### #2 (HIGH) — Test the self-heal path
Added `Poller_ReRunAfterMidWriteFailure_SelfHeals_NoDuplicateVideoRows`: run 1 crashes on the account write (2 video rows, no sentinel); run 2 against the SAME in-memory store with a healthy context must clear the stale video rows and write exactly 2 video + 1 account — proving the stale-clear branch (previously dead in the suite) and that a re-poll doesn't duplicate rows.

### #3 (MEDIUM) — Strengthen the host-local date test
Added a conditional `Assert.NotEqual(utcDate, SnapshotDate)` that fires only when the host's local and UTC dates differ — giving the test discriminating power against a `UtcDateTime` regression on non-UTC hosts (a no-op on UTC CI, which can't distinguish them anyway).

## Decisions (documented, no code change)

### #4 (MEDIUM) — "Retry tomorrow" vs hourly retry
KEPT hourly retry. The plan's "retry tomorrow" wording is superseded by the **DigestService-consistent** pattern this poller mirrors: the hourly loop re-attempts any platform without a completion sentinel until it succeeds or the day ends. This gives same-day recovery from a morning blip and is gentle (≥1h spacing between attempts). A per-day attempt marker would add real complexity for marginal benefit (YAGNI). Documented in the section doc.

### #5 (LOW) — Non-issue
Multiple active Analytics creds per platform cannot occur — section-02's `(Platform, Purpose) WHERE IsActive` filtered unique index enforces exactly one active analytics credential per platform. No change.

### #6 (LOW) — Provider-throws is the safe failure mode
If a future provider threw from `RefreshAsync` instead of returning `Fail(Revoked)`, the per-credential catch leaves `IsActive=true` — it never *wrongly* deactivates. All shipped providers catch and return `Fail`. Acceptable; no change.
