# Code Review — section-09 Brand Ranking Profile API

Reviewer: `deep-implement:code-reviewer`. Build green, 838 solution tests pass at review time.

## Findings
- **CRITICAL — lost-update hole.** `if (uint.TryParse(token, out var t)) SetOriginalValue(...)` skipped the
  optimistic-concurrency check entirely when the token was empty/garbage → a blank token silently clobbers
  a concurrent edit (violates R-H3). → **Fixed: fail closed** (invalid/empty token → `ValidationFailure`
  before any mutation; `OriginalValue` set unconditionally) + validator `ConcurrencyToken NotEmpty` + new
  handler and endpoint tests for the empty-token path.
- **HIGH — `AudienceSecondary` null-vs-empty.** A client `""` for a stored `null` flipped
  `RequiresVersionBump` → needless Version bump + re-score (token spend). → **Fixed: `Normalize` (blank→null)
  applied in both `BuildProposed` and the apply branch.**
- **HIGH — no test for weights-only ignoring an unknown pillar Id.** Branch is unreachable in normal flow
  (an unknown id is a structural change → bump → definition branch), so left as defensive code; the
  "rename forces bump" test already proves structural changes route to the bump path.
- **MEDIUM — `IAppDbContext.Entry` abstraction leak** (exposed the whole EF change-tracker to the app
  layer). → **Fixed: replaced with a narrow `SetOriginalValue(entity, propertyName, value)` port,
  implemented in `ApplicationDbContext` (Infrastructure); EF stays out of the application layer.**
- **MEDIUM — PUT binds the command directly; an omitted collection deserializes to empty (full-replace).**
  Intended REST semantics. → **Documented** on the endpoint ("PUT is a FULL REPLACE").
- **LOW — missing stale-token → 409 endpoint test.** InMemory can't produce an xmin conflict; the handler
  test proves Conflict→`Result`, `ToApiResult` maps Conflict→409, and a new endpoint test covers the
  fail-closed empty-token → 400 path. True stale-xmin → 409 is deferred to **section-12 Testcontainers**.
- **LOW — `Guid.Empty` pillar collapse** (two new empty-id pillars merge to one). → **Fixed: validator
  rejects `Guid.Empty` pillar ids** (clients must assign ids, R-C3).
- **NITs — misleading "weights-only" knob comment; vacuous `NotNull` token assertion.** → **Fixed.**

## Confirmed good
Server-enforced write mode via the entity's `RequiresVersionBump` (no client mode flag); a definition
change can never be applied without a bump (and vice-versa); pillar reconciliation matches by Id (R-C3) and
nulls `DescriptionEmbedding` on a description edit so section-06 re-embeds; route `/api/brand-ranking-profile`
is collision-free with the voice `BrandProfile`; no weights-sum constraint (read-time renormalization).
