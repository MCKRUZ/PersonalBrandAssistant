# Feed Ranking Redesign — Design Spec

Status: **design agreed (grill session 2026-06-15)**, not yet implemented.
Origin: two asks — (1) a "ranked view" on the feeds page, (2) a better ranking system.

## Problem

The current rank is a single Gemini-2.5-flash call that scores an item 0–10 for
"content opportunity for Matt's brand" (`IdeaAnalyzer.cs`). Five weaknesses, all real:

- **Coarse** — 11 buckets; dozens tie at "8", no ordering within a tier.
- **No recency** — last week's item outranks this morning's.
- **One-dimensional** — brand-fit, novelty, authority, timeliness collapsed into one opaque number.
- **No feedback loop** — never learns from what was published or performed.
- **Drift** — each item scored in isolation; "8" means different things day to day.

Root cause behind all five: **"the brand" is one hardcoded sentence** in the prompt.
Better machinery pointed at a vague target still produces a vague rank.

## Approach

Define the brand as a structured, editable **Brand Profile**, seed it by *inferring*
from real evidence, then rank items against it with a multi-factor composite score.

Pipeline: **mine evidence → synthesize draft profile → human edits → ranker source of truth.**

## Decisions (locked)

1. **Task 1 ("ranked view")** — a plain score-sort already exists but points at the old
   coarse number. Don't just re-sort; surface the *new* rank properly (see #11).
2. **Root problem** — all five weaknesses, driven by the one-sentence brand definition.
3. **Foundation** — a structured, editable Brand Profile is the first deliverable; the
   multi-factor rank is built on top of it.
4. **Brand definition strategy** — **bootstrap declared profile from inferred signal**, then
   human-refine. Declared (inspectable/tunable) seeded by inference (honest, no blank page).
5. **Evidence to draft from:** published posts (matthewkruczek.ai) + 3 voice skills
   (blog/LinkedIn/Twitter writers) + paused "AI pioneer brand strategy" doc + GitHub
   **created** repos (`gh repo list MCKRUZ`) + GitHub **starred** repos, 1-yr window
   (`gh api user/starred`, has `starred_at`).
   **Reality-check only (not in draft):** kept/high-scored ideas + GA4/GSC analytics.
   Watch: GitHub skews dev/technical, published brand skews exec — tension is signal
   ("exec-altitude voice who actually ships"), but don't drag pillars into dev-tooling weeds.
6. **Brand Profile schema** — Positioning (1 sentence) · Audience (primary+secondary) ·
   **Content Pillars (3–5, each weighted)** · Authority Topics (build-earned, score boost) ·
   Anti-topics (looks-relevant-but-off-brand, score penalty) · Voice markers.
7. **Scoring mechanism** — **Hybrid (C)**: embeddings as cheap first-pass over all items,
   LLM-per-pillar on candidates clearing a similarity threshold. Build LLM-per-pillar quality
   first; embedding layer is the scale/recompute optimization.
   **Critical:** LLM emits **raw per-pillar sub-scores stored once**; **weights apply at
   query/read time** as a weighted sum. Sliding a weight re-ranks instantly, zero LLM calls.
   Re-run LLM only when **pillar definitions** change.
8. **Composite formula** — **multiplicative decay**:
   `rank = brandFit(weighted pillars) × recencyDecay × antiTopicMult × authorityBoost`
   Brand-fit is substance; recency is a gate that can only suppress. Anti-topic ≈ ×0.1
   (near-kill), authority-topic ≈ ×1.2 (nudge).
9. **Recency** — exponential, **7-day half-life** (thought leadership, not breaking news;
   a strong angle stays writable ~a week). Config knob, not a constant.
10. **Source authority** — **cut** (decision D). brandFit × recency carries the rank;
    revisit as a learned factor later if needed.
11. **Scoring scope** — **rolling 30-day LLM window + forward** (decision B). 7-day decay
    means items >~3–4 wks rank ≈0, so backlog LLM-scoring is wasted spend. Embed all items
    (search/dedup); LLM-score only the window. Pillar-definition change → re-score window only.
    Supersedes the `BackfillEnabled` all-or-nothing flag.
12. **Storage + editing** — **DB-backed `BrandProfile` entity + Angular editor page**
    (decision A), single active profile, **version-stamped** (each item records which profile
    version it was scored under → detect stale sub-scores). **Weight edits auto-apply**
    (cheap query-time re-sort); **pillar-definition edits require explicit confirm** before
    firing re-score LLM calls.
13. **Surfacing** — **both** (decision C): make composite rank the **default sort everywhere**
    (replacing "Highest score"), *and* build a dedicated **"Ranked" view mode** (3rd beside
    Grid/List): numbered Top-N, time window (Today / This week), big rank numerals, per-item
    **brand-fit breakdown** (pillars hit + LLM one-line reason + anti-topic/authority flags).
    The "why #1 beat #2" surface is the payoff of the multi-factor work.
14. **Inference session** — **one-shot synthesis, evidence cited per claim** (decision A).
    Gather all #5 evidence → produce **draft profile v0** with each pillar justified by its
    evidence → human red-pens → iterate. Corpus = site + GitHub for now; social posts folded
    in later if exported.

## Build order (proposed, for /deep-plan)

1. **Brand inference session** → draft Brand Profile v0 (no code; produces seed data).
2. `BrandProfile` entity + migration + DB-backed config, version stamp.
3. Rewrite `IdeaAnalyzer` → per-pillar raw sub-scores + anti-topic/authority flags against profile.
4. Query-time composite rank (weighted sum × recency decay) in `ListIdeas`; default sort = rank.
5. Embedding first-pass layer + 30-day scoring window scope.
6. Angular Brand Profile editor (sliders, auto-apply weights, confirm-before-rescore).
7. Dedicated "Ranked" view mode with brand-fit breakdown.

## Key files (current system)

- `src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs` — scoring prompt (the hardcoded brand sentence).
- `src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs` — background batch scorer.
- `src/PBA.Domain/Entities/Idea.cs` — Score/ScoreReason/ScoredAt fields.
- `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs` — list/sort/filter handler.
- `src/PersonalBrandAssistant.Web/src/app/features/ideas/` — ideas page, store, view-toggle, sort dropdown.
- `src/PBA.Api/appsettings.json` — `IdeaScoring` / `Clustering` config.
