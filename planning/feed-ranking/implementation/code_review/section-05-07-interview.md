# Review triage & decisions — sections 05+06+07

Triaged autonomously (per the user's standing "don't prompt for routine workflow decisions" preference).
No items required a user decision beyond the temperature path, which was already chosen up front
(path 2 — real `SendPromptAsync(...,temperature?,...)` overload).

## Applied fixes

- **M2 (fixed)** — the scoring sweep now calls `embedder.ComputeEmbeddingBrandFit(idea.Embedding,
  profileEntity.Pillars)` instead of `BrandFit.EmbeddingWeightedSum` directly. The sweep already loads
  `profileEntity.Pillars` (BrandPillar entities) and resolves `embedder`, so the section-06 wrapper
  becomes the live shared entry point it was specified to be, while still delegating to the single
  `BrandFit` formula. Removed the now-unused `pillarVectors` local.

- **M3 (fixed)** — `EmbedIdeasAsync` now `SaveChangesAsync` after each successful chunk (was: once after
  the whole loop). A ~3,800-item backfill is now resumable; a crash never re-spends embedding tokens on
  already-stored vectors (embedding is irreversible token spend, flagged in project memory).

- **H2 (fixed)** — dedup primary selection keeps the `Score` proxy (faithful within a group: true
  near-duplicates share a pre-filter side, so all members use the same Score formula) but adds a
  deterministic tie-break: `OrderByDescending(Score).ThenBy(DetectedAt).ThenBy(Id)`. Oldest story wins
  ties (canonical original); the primary no longer depends on EF load order.

- **H1 (clarified, no logic change)** — the two-source derived `Score` is **per R-M1 by design**
  (above = renormalized LLM brandFit, below = raw embedding brandFit; the UI distinguishes score vs rank
  sorts). The comments were sharpened to state this explicitly so the difference is not read as a bug.

## Decisions to NOT change (with rationale)

- **M1 (TimeProvider)** — kept `DateTimeOffset.UtcNow`. The section text says inject a clock "consistent
  with the existing services"; the five existing radar background services (old IdeaScoringService,
  IdeaClusteringService, DigestService, HighScoreAlertService, SourcePollingService) all use
  `DateTimeOffset.UtcNow` directly. Introducing `TimeProvider` to only the two new services would
  *diverge* from the established pattern. A TimeProvider migration belongs as a separate cross-cutting
  refactor of all radar services, not piecemeal here. Window tests use real `UtcNow` with wide relative
  offsets (e.g. 40 days vs a 30-day window), which is deterministic in practice.

- **L1 (dedup gate wedge)** — a permanent embed-attempt cap requires a new `Idea` column, which is owned
  by section-03 (entity schema), not this unit. Logged as a future hardening item; low severity.

- **L2 / L3 / N1 / N2** — meet the section specs as written (exact-equality collapse guard; window-bounded
  below partition; intentional placeholder anchors; build-as-guard for removed config symbols).

## Verification after fixes
- `dotnet build` clean, 0 warnings.
- Section 05/06/07 suites: 33/33 green.
- Full solution suite re-run before commit.
