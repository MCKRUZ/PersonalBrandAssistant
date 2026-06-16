# Code Review — section-02-brand-profile-domain

## Findings
- **HIGH (claimed) — xmin emitted as a created column → migration fails.** REJECTED (false positive).
- **HIGH (security) — POST /api/brand-ranking-profile/seed unauthenticated in prod.** ADDRESSED.
- **MEDIUM — topic/voice-marker version-bump used set semantics (reorder/dupe ignored).** FIXED.
- **MEDIUM — pillar Id preservation contract is implicit (affects section-09 over-bumping).** FLAGGED for §09.
- **LOW — all Postgres-only guarantees (partial index, xmin conflict, vector round-trip) untested here.** Deferred to §12 (accepted).
- **LOW — seeder swallowed ALL DbUpdateException.** FIXED (narrowed to unique-violation).
- **LOW — dead null-guards in the value converter.** LET GO (harmless/defensive).
- **LOW — public settable Xmin leaks the concurrency token.** FIXED (private set).
