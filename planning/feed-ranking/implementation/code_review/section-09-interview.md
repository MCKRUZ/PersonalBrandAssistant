# Review triage & decisions — section-09

Triaged autonomously. The Critical was a genuine bug and was fixed immediately.

## Applied fixes
- **CRITICAL (fail-closed concurrency):** `UpdateBrandRankingProfile.Handler` now returns
  `ValidationFailure` on a missing/unparseable `ConcurrencyToken` *before* any mutation and sets the
  change-tracker original value unconditionally. Added `RuleFor(x => x.ConcurrencyToken).NotEmpty()` to the
  validator (defence in depth) and two tests (handler-level `…_FailsClosed_NoLostUpdate`, endpoint-level
  `Put_MissingConcurrencyToken_Returns400`).
- **HIGH (AudienceSecondary normalization):** added `Normalize(blank→null)`, applied in `BuildProposed` and
  the bump-branch write so a `""` vs `null` difference can't trigger a needless re-score.
- **MEDIUM (abstraction leak):** reverted the `IAppDbContext.Entry<TEntity>` addition; replaced with a
  narrow `void SetOriginalValue<TEntity>(TEntity, string propertyName, object value)` port implemented in
  `ApplicationDbContext`. The application layer no longer references EF's change-tracker namespace.
- **LOW (Guid.Empty collapse):** validator rejects `Guid.Empty` pillar ids; the defensive remap in
  `ApplyPillarDefinitions` is kept as belt-and-suspenders.
- **NITs:** clarified the knob comment ("ALWAYS apply regardless of mode"); the GET token assertion now
  asserts `"0"` (capable of failing).

## Decisions / deferrals
- **PUT full-replace** (omitted collection → empty) is intended REST semantics; documented on the endpoint
  rather than adding partial-update merge logic (which would invite exactly the smuggling R-H4 prevents).
- **Stale-token → 409 at the HTTP layer** can't be exercised on InMemory (no real xmin). The handler test
  proves Conflict and `ToApiResult` maps Conflict→409; the empty-token→400 endpoint test covers fail-closed.
  Real stale-xmin → 409 is deferred to **section-12 Testcontainers** (tracked).
- **Weights-only unknown-pillar branch** left as defensive (unreachable in normal flow — an unknown id is a
  structural change that routes to the bump path); no dedicated test.

## Verification after fixes
`dotnet build` clean (0 warnings). Section-09: 21 Application + 4 API tests green. Full solution: 840 tests,
0 failures.
