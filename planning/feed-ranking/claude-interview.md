# Interview — Feed Ranking Redesign

Most of the design was settled in the prior grill session (see `docs/feed-ranking-redesign.md`,
14 decisions) and refined by research (`claude-research.md`). This interview resolved the
remaining architectural unknowns.

## Q1 — Embedding layer's primary job?
**A: Brand-fit pre-filter.** Embed each pillar's description + each item; cheap first-pass
brandFit = weighted similarity of item vs pillars. Only items above a threshold (and within the
30-day window) earn a full LLM per-pillar call. Below-threshold items keep the embedding-only
score, no LLM. Maximizes the cost saving — the embedding decides who's worth an LLM call.

## Q2 — Fate of the LLM clusterer (currently dedupes via LLM, gates on Score>=6)?
**A: Replace with embedding dedup.** Drop the LLM clusterer. Dedupe by cosine similarity over
the embeddings we already compute (near-duplicate = similarity above a threshold, ~0.85 default).
No extra LLM calls; reuses the embedding investment. Gate moves to the new brandFit.

## Q3 — Legacy Idea.Score (0-10) field + color badge?
**A: Keep as derived display.** Repurpose Idea.Score as a derived 0-10 (round of brandFit×10)
so the existing badge / score-distribution keep working; add new per-pillar sub-score fields
alongside. Least UI churn, back-compatible.

## Q4 — Cutover handling of the existing ~3,800 ideas?
**A: Embed all, LLM-score the 30-day window.** One-time: embed all ~3,800 (cheap; needed for
dedup/search). LLM per-pillar score only items detected in the last 30 days against the new
profile. Older items keep prior/null scores and rank ~0 via decay anyway. Bounded cost,
populated feed on day one.

## Plan defaults (baked in as tunable config, not separately asked)
- Embedding model: `openai/text-embedding-3-small` (1,536-dim), batched via OpenRouter `/embeddings`.
- Recency: multiplicative, 7-day half-life, decay floor ~0.075. `halfLifeDays` + `floor` config.
- Pre-filter similarity threshold + dedup similarity threshold (~0.85): config.
- brandFit pillar similarity uses weighted pillar weights at query time.
- Anti-topic ×0.1, authority-topic ×1.2.
- Ranked view: Top-N default 20, default window "This week" (toggle Today/This week).
- Brand Profile editor route: under Ideas (e.g. `/ideas/brand-profile`).
- LLM scoring: JSON-schema structured output, fixed few-shot anchors, per-level rubric, low temp (0–0.2).
- Single active BrandProfile, version-stamped; each scored Idea records the profile version used.
