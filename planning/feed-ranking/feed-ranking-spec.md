# Feed Ranking Redesign — Spec

Two source documents hold the agreed design. Read both first:
- `docs/feed-ranking-redesign.md` — full system design, 14 locked decisions (the authority).
- `planning/brand-strategy/brand-profile-v0.md` — agreed Brand Profile v1 (the ranker's seed data).

## Goal

Replace the current single opaque 0–10 LLM "content opportunity" score with a brand-anchored,
multi-factor composite rank, and surface it properly on the feeds (Ideas) page.

## What to build (build order from the design spec)

1. **BrandProfile entity + EF migration + DB-backed config.** Single active profile,
   version-stamped. Holds: positioning, audience, weighted pillars (name + description + weight),
   authority topics, anti-topics, recency half-life, voice markers. Each scored Idea records the
   profile version it was scored under (to detect stale sub-scores).

2. **Rewrite IdeaAnalyzer** to score an item against the BrandProfile and emit **raw per-pillar
   sub-scores** (0–1 per pillar) + anti-topic flag + authority-topic flag + one-line reason,
   replacing the hardcoded one-sentence brand prompt. Sub-scores stored once per item.

3. **Query-time composite rank** in ListIdeas:
   `rank = brandFit(weighted sum of pillar sub-scores) × recencyDecay(7-day half-life)
   × antiTopicMult(×0.1 if flagged) × authorityBoost(×1.2 if flagged)`.
   Weights applied at read time over stored raw sub-scores so weight changes re-rank instantly
   with zero LLM calls. Make composite rank the default sort (replacing "Highest score").
   Re-run LLM only when pillar *definitions* change.

4. **Hybrid scoring + scope.** Embedding first-pass over all ideas; LLM-per-pillar only on
   candidates clearing a similarity threshold. LLM-scoring scoped to a rolling 30-day window
   (embed everything for search/dedup; only LLM-score what can rank under 7-day decay).
   Supersedes the BackfillEnabled all-or-nothing flag.

5. **Angular Brand Profile editor page.** Edit positioning/pillars/weights/topics. Weight slider
   edits auto-apply (cheap query-time re-rank). Pillar-definition edits require explicit confirm
   before triggering a re-score of the 30-day window (LLM cost).

6. **Dedicated "Ranked" view mode** (third beside Grid/List). Numbered Top-N, time window
   (Today / This week), big rank numerals, per-item brand-fit breakdown: which pillars it hit,
   the LLM one-line reason, anti-topic/authority flags. The "why #1 beat #2" surface.

## Stack / constraints

- Backend: .NET 10, MediatR/CQRS, Result<T> (PBA.Domain.Common), EF Core. No endpoint auth in v2.
- Frontend: Angular 19 standalone components, NgRx signals, PrimeNG.
- LLM via ISidecarClient (OpenRouter; default `google/gemini-2.5-flash`). Embeddings: pick an
  approach during planning (sidecar embedding model vs. alternative) — flag as a research item.
- Tests: xUnit + WebApplicationFactory + in-memory DB (backend); Jasmine/Karma (frontend). 80% coverage on new code.
- Immutability, small files, Result<T> for expected failures, FluentValidation at boundaries.

## Key existing files

- `src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs` — current scoring prompt (the hardcoded brand sentence).
- `src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs` — background batch scorer.
- `src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs` — dedup/clustering.
- `src/PBA.Domain/Entities/Idea.cs` — Score/ScoreReason/ScoredAt/DuplicateOfId fields.
- `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs` — list/sort/filter handler.
- `src/PBA.Api/Endpoints/IdeaEndpoints.cs` — GET /api/ideas.
- `src/PBA.Api/appsettings.json` — IdeaScoring / Clustering config.
- `src/PersonalBrandAssistant.Web/src/app/features/ideas/` — ideas component, store, view-toggle, sort dropdown, score-badge, filter sidebar.
