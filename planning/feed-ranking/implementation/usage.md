# Feed Ranking Redesign — Usage Guide

Brand-anchored composite ranking for the Idea Bank: ideas are embedded, LLM-scored per brand pillar, and
ranked at read time by `brandFit × recencyDecay × antiTopicMultiplier × authorityBoost`. Pillar weights are
applied at query time, so re-weighting re-ranks instantly with zero LLM calls.

## Architecture at a glance

```
ingest idea ─► IdeaEmbeddingService (OpenRouter) ─► store vector(1536)
                                                 └─► brand-fit pre-filter (cosine vs pillar vectors)
                                                       ├─ ≥ threshold ─► IdeaScoringService ─► IIdeaAnalyzer (per-pillar LLM)
                                                       └─ < threshold ─► embedding-only brandFit
IdeaDedupService ─► in-memory cosine dedup (gated on full embedding coverage)
ListIdeas (read) ─► ComputeRank in memory ─► rank-sorted IdeaDto (Ranked view + score sort)
Brand Profile editor ─► GET/PUT /api/brand-ranking-profile (weights-only vs definition modes)
```

## Backend

### Config (`appsettings.json`)
- `Embedding`: `Model` (`openai/text-embedding-3-small`), `Dimensions` (1536), `BatchSize` (128).
- `Ranking`: `PreFilterThreshold` (0.30), `DedupThreshold` (0.85), `ScoringWindowDays` (30).
- `IdeaScoring`: `IntervalMinutes`, `BatchSize`, `ThrottleMs`, `Model`, **`ScoringEnabled` (default false —
  the gate for the irreversible LLM-scoring run).**
- `Clustering`: `IntervalMinutes`, `LookbackHours`, `MaxItemsPerSweep` (dedup threshold comes from `Ranking`).

### Background services (auto-registered, run on a timer)
- `IdeaEmbeddingService` — embeds ideas (`title + description`) + active-profile pillar descriptions; never
  persists a zero/NaN vector; resumable per-chunk.
- `IdeaScoringService` — per sweep: snapshot profile + ranking options once; candidate set = embedded,
  in-window, not-current-version, under the attempt cap; pre-filter; **LLM-score above threshold only when
  `ScoringEnabled` is true**; embedding-only brandFit below threshold.
- `IdeaDedupService` — in-memory cosine dedup, gated on full in-window embedding coverage; highest-brandFit
  (oldest on ties) is primary.

### API
- `GET /api/brand-ranking-profile` → `BrandRankingProfileDto` (incl. `concurrencyToken`).
- `PUT /api/brand-ranking-profile` → full-replace; the server decides the write mode:
  - **weights/knobs only** → no version bump, no re-score;
  - **definition change** (positioning/audience/topics/voice/pillar add-remove-rename-description) → bumps
    `Version` (triggers re-score) and nulls the edited pillar's embedding.
  - Send back `concurrencyToken`; a stale/blank token → `400`/`409` (fails closed).
- `GET /api/ideas?sortBy=rank` (default) → `IdeaDto` with `rank`, `brandFit`, `pillarBreakdown[]`,
  `isAntiTopic`, `isAuthorityTopic`, `recencyFactor`, `stale`. `sortBy=score` still works.

## Frontend (`/ideas`)

- **Ranked view** — third view-toggle button (`ranked-toggle`): numbered Top-N by composite rank, Today/
  This-week window, per-item pillar breakdown + authority/anti/stale badges.
- **Brand Profile editor** (`/ideas/brand-profile`) — weight sliders/knobs **auto-apply** (weights-only PUT,
  instant re-rank, no dialog); pillar-definition/topic edits **stage** behind a "Save & re-score" confirm
  that warns about LLM cost. Stale-token conflicts surface "changed elsewhere" without losing the edit.

## Go-live (manual runbook — see `sections/section-12-cutover.md`, gated at step 4)

1. Apply migrations in order (pgvector extension → BrandRankingProfile → Idea columns); verify the v1
   profile seeded (one active row, Version 1, 5 pillars).
2. Backfill embeddings (cheap, reversible) until coverage is complete.
3. **GATE (irreversible):** flip `IdeaScoring:ScoringEnabled` → `true` and restart — LLM-scores the last
   30 days. Verify, then promote the second host. R-L6 grep the two deployed appsettings.

## Tests
- Backend: `dotnet test` (844 green, incl. `CutoverTests` real-Postgres migration guard via Testcontainers).
- Frontend: `cd src/PersonalBrandAssistant.Web && ng test --watch=false --browsers=ChromeHeadless` (586 green).
