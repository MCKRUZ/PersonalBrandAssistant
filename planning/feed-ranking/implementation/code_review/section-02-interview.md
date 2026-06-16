# Code Review Triage — section-02-brand-profile-domain

Run mode: auto-run. One item (prod seed endpoint) was a real security decision → asked the user.

## REJECTED (false positive)
- **HIGH "xmin emitted as a created column."** The reviewer read the migration .cs (which lists
  `xmin = table.Column<uint>(type:"xid", rowVersion:true)` — Npgsql's scaffold representation of the
  system-column concurrency token) but did NOT check the generated DDL. Verified empirically:
  `dotnet ef migrations script` produces a CREATE TABLE with NO xmin column and **0** `xmin` references
  in the whole script (Npgsql special-cases the system column). `has-pending-model-changes` is also clean.
  The migration applies correctly. No change.

## USER DECISION (security)
- **HIGH unauthenticated prod seed endpoint.** User chose: startup seeder, no public endpoint. APPLIED:
  the seed endpoint moved back inside `if (IsDevelopment())` (alongside feed/idea-source seeds); a
  scoped, try/catch-guarded `IBrandRankingProfileSeedService.SeedAsync()` now runs once at app startup
  (Program.cs). Profile guaranteed on every deploy; no unauthenticated prod write surface.

## AUTO-FIX (applied)
- **Ordered topic/marker comparison** — `RequiresVersionBump` now uses `SequenceEqual` (ordered) for
  AuthorityTopics/AntiTopics/VoiceMarkers, since they render verbatim into the analyzer prompt; reorder
  or duplicate is a real change and bumps Version. Removed the set-based helper.
- **Narrowed seeder catch** — `DbUpdateException when inner is PostgresException{SqlState:UniqueViolation}`;
  any other failure propagates instead of being reported as a benign no-op.
- **Private Xmin setter** — `public uint Xmin { get; private set; }`; EF sets it on load, section-09
  forces conflicts via the change tracker's OriginalValue, not the property.

## LET GO
- Dead null-guards in the float[]<->Vector converter: harmless and defensive; EF bypasses null anyway.

## FLAGGED for section-09
- The update command MUST preserve pillar Ids across edits or RequiresVersionBump sees full add/remove
  churn and over-bumps (unnecessary re-scores). No affordance enforces this at the entity level.

## DEFERRED (accepted)
- Partial-unique-index rejection (R-L4), xmin DbUpdateConcurrencyException (R-H3), and vector(1536)
  round-trip are Postgres-only → section-12 Testcontainers. xmin migration correctness settled here by
  SQL inspection; the rest genuinely need a real Postgres.
