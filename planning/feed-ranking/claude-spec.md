# Feed Ranking Redesign — Consolidated Spec

Synthesis of: initial spec (`feed-ranking-spec.md`), the agreed design (`docs/feed-ranking-redesign.md`,
14 decisions), the agreed Brand Profile v1 (`planning/brand-strategy/brand-profile-v0.md`),
research (`claude-research.md`), and the interview (`claude-interview.md`).

## Objective

Replace the single opaque 0-10 LLM "content opportunity" score with a brand-anchored, multi-factor
composite rank computed against an editable Brand Profile, and surface it on the Ideas (feeds) page
as the default ordering plus a dedicated Ranked view.

## Confirmed environment (from research)
- **PostgreSQL / Npgsql** (confirmed, not TBD) → vector storage via **pgvector**, exact cosine, **no index** at ~3,800 rows.
- **OpenRouter has a native embeddings endpoint** → extend `ISidecarClient` with `EmbedAsync`; no second provider. Model `openai/text-embedding-3-small` (1,536-dim), batched.
- .NET 10, MediatR + `Result<T>` (`PBA.Domain.Common`), EF Core, FluentValidation pipeline behavior, options via `IOptions`/`IOptionsMonitor`. No endpoint auth in v2.
- Angular 19 standalone + NgRx signal store + PrimeNG. Lazy feature routes.
- Tests: xUnit + InMemory/Moq (note: InMemory can't run pgvector SQL — needs a cosine unit test + Postgres testcontainer or isolated similarity test). Jasmine/Karma + HttpTestingController frontend.

## Functional requirements

### R1 — Brand Profile (the ranker's source of truth)
- New DB-backed `BrandProfile`: positioning, audience, **weighted pillars** (name, description, weight),
  authority topics, anti-topics, recency `halfLifeDays` + decay `floor`, voice markers, **version** stamp.
- Single active profile. Seeded from `brand-profile-v0.md` (5 pillars: Agent-Native Architecture 0.28,
  Enterprise AI Adoption & Governance 0.23, Agentic SDLC 0.22, Claude/Anthropic Agent Engineering 0.15,
  Microsoft Enterprise AI Stack 0.12).
- Each scored Idea records the profile **version** it was scored under (detect stale sub-scores).

### R2 — Per-pillar scoring (replaces hardcoded IdeaAnalyzer prompt)
- Score an item against the BrandProfile, emitting **raw per-pillar sub-scores (0-1)** + anti-topic flag
  + authority-topic flag + one-line reason. Stored once per item.
- LLM call uses **JSON-schema structured output, fixed few-shot anchor exemplars, per-level rubric
  ({0,.25,.5,.75,1}), low temperature (0-0.2)** for cross-corpus calibration.

### R3 — Hybrid scoring pipeline (embeddings as brand-fit pre-filter)
- Embed every item (title+description; title+summary once summarized) and every **pillar description**.
- First-pass embedding brandFit = weighted cosine similarity of item vs pillars.
- Only items **above a similarity threshold AND within the rolling 30-day window** get a full LLM
  per-pillar call. Below-threshold items keep the embedding-only brandFit (no LLM).
- Embed all ~3,800 once at cutover; LLM-score only the 30-day window.

### R4 — Composite rank (query-time)
- `rank = brandFit(weighted Σ pillar sub-scores) × recencyDecay × antiTopicMult × authorityBoost`.
- `recencyDecay = max(exp(-ln2/halfLifeDays · ageDays), floor)` (7-day half-life, floor ~0.075).
- antiTopic ×0.1, authority ×1.2.
- **Weights applied at read time** over stored raw sub-scores → weight changes re-rank instantly,
  zero LLM. LLM re-run only when pillar **definitions** change.
- Composite rank becomes the **default sort** in ListIdeas (replaces "Highest score").

### R5 — Dedup via embeddings (replaces LLM clusterer)
- Drop `IdeaClusterer`/its LLM call. Dedupe by cosine similarity over computed embeddings
  (near-duplicate = similarity above threshold ~0.85). Gate on the new brandFit, not old Score≥6.

### R6 — Legacy score compatibility
- Repurpose `Idea.Score` (0-10) as a **derived** value (round of brandFit×10) so the existing
  score-badge and score-distribution keep working. Add new per-pillar fields alongside.

### R7 — Brand Profile editor (Angular)
- New page (route under Ideas, e.g. `/ideas/brand-profile`) to edit positioning/pillars/weights/topics/
  half-life/floor. **Weight slider edits auto-apply** (cheap query-time re-rank). **Pillar-definition
  edits require explicit confirm** before triggering a re-score of the 30-day window.

### R8 — Ranked view (third view mode)
- Add `'ranked'` to the store `viewMode` union + a third toggle button. Numbered Top-N (default 20),
  window toggle (Today / This week, default This week), big rank numerals, per-item brand-fit
  breakdown: pillars hit, the LLM reason, anti-topic/authority flags.

## Non-goals (this phase)
- Learned/performance-based brand (revealed/performance signal) — future.
- Source-authority factor (explicitly cut).
- Multi-profile / A-B profiles — single active profile only.
- Vector ANN index — unnecessary at this scale.

## Key risks
- InMemory EF provider can't execute pgvector SQL → test strategy must isolate cosine logic + use a
  Postgres testcontainer for similarity queries.
- OpenRouter embedding model catalog is smaller/shifting → verify slug via `/embeddings/models`;
  fallback is a direct provider call (a second client path).
- Cross-corpus calibration of independently-scored items → mitigated by fixed few-shot anchors + rubric.
- Cutover ordering: embed-all must complete (or be in-flight) before dedup/first-pass make sense.
