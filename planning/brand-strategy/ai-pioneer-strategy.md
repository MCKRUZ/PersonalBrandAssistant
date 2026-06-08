# AI Pioneer Brand Strategy — Matt Kruczek

**Status:** Channel decision resolved (2026-06-03). Ready to execute.
**Goal:** Establish Matt as a recognized enterprise-AI pioneer — someone who shows enterprise teams what AI can actually ship, by building it himself.

---

## 1. Positioning

- **Core claim:** "I show enterprise teams what AI can actually ship — by building it myself."
- **Credibility triangle:** Big-4 consulting scale + hands-on .NET builder + consistent publisher. The combination is the moat; few people sit at all three corners.
- **Audience:** Senior engineers, EMs, and architects at enterprise companies who are frustrated by AI hype and want proof, patterns, and shipped artifacts.

## 2. Content Pillars

Every post is grounded in something actually built, deployed, or measured (the Mollick rule). No abstract hot takes without a build behind them.

1. **"Here's what I shipped."** Concrete builds, with the artifact, the benchmark, or the deploy behind it.
2. **"Here's what enterprise actually needs to know."** Translating frontier capability into enterprise-real constraints (security, scale, compliance, cost).
3. **"Here's where everyone is wrong."** False-debate debunking, hype correction, naming the real tradeoff.
4. **"Here's the pattern I keep seeing."** Reusable patterns abstracted from repeated hands-on work.

## 3. Cadence

- **LinkedIn:** 3–4× / week
- **Substack:** 2× / month
- **Blog (matthewkruczek.ai):** 1× / month (pioneer-grade long-form; already at 40+ posts)

## 4. Channel Hierarchy (phased)

**Decision (2026-06-03):** Twitter/X — *willing but not yet*. Phased ramp. LinkedIn + blog carry distribution now; X is added in Phase 3 once a content reservoir and rhythm exist.

| Channel | Role | When |
|---------|------|------|
| **Blog** | Proof anchor — the deep artifact every short post points back to | Now (live, mature) |
| **LinkedIn** | Enterprise-audience distribution and reach | Now |
| **Substack** | Owned-audience depth, email moat, escapes algorithm risk | Now |
| **Twitter/X** | Pioneer-status conferral, peer/community visibility | Phase 3 (later) |
| Conference speaking | Authority signal, compounding | Opportunistic throughout |
| Open source | Builder proof, inbound credibility | Opportunistic throughout |

**Rationale for deferring X:** X confers pioneer status through peer recognition, but it punishes inconsistency and cold starts. Entering X with an existing reservoir of shipped-build threads (repurposed from blog/LinkedIn) is far stronger than starting from zero. The blog + LinkedIn engine produces that reservoir first.

## 5. 90-Day Plan

### Phase 1 — Days 1–30: Rhythm and proof
- Lock the LinkedIn 3–4×/week cadence. Each post ties to a shipped build (pillar 1 or 4).
- Publish 1 blog long-form (pillar 2 or 3) + repurpose it into 2–3 LinkedIn posts and 1 Substack issue.
- Establish the repurpose pipeline in PBA: blog → LinkedIn → Substack, all from one source build.
- **Exit criteria:** 4 consecutive weeks of on-cadence LinkedIn; repurpose flow working in PBA.

### Phase 2 — Days 31–60: Depth and owned audience
- Maintain LinkedIn cadence; bias toward pillar 3 ("where everyone is wrong") for differentiation.
- 2 Substack issues; begin building the email list deliberately (CTA in LinkedIn + blog).
- 1 blog long-form. Start drafting X-ready threads from each build (banked, not yet posted).
- **Exit criteria:** A bank of 8–12 X-ready threads; Substack subscriber growth trend positive.

### Phase 3 — Days 61–90: X launch
- Launch Twitter/X using the banked thread reservoir — open with 2–3 strong shipped-build threads in the first two weeks, not a cold "hello world."
- Cross-link X ↔ blog ↔ LinkedIn. X for status/peer reach, LinkedIn for enterprise reach, blog as the anchor.
- Maintain all prior cadences.
- **Exit criteria:** X presence active and consistent; full four-channel engine running.

## 6. Known Gaps / PBA Platform Dependencies

- **Per-post category:** `Content` has no topic-category field (`ContentType` is a format enum: Blog/Tweet/LinkedInPost). BlogConnector defaults category to "Enterprise AI". A real per-post category needs a new field on `Content` — relevant if the strategy wants category-based content planning. Not blocking.
- **Substack:** No PBA integration today — manual publish, or a future connector.
- **Repurpose pipeline** is the Phase-1 platform dependency: blog → LinkedIn/Substack from one source. Confirm the existing repurpose capability covers it before relying on it.

## 7. Key Insight

Matt is already writing like a pioneer — the blog is pioneer-grade (specific benchmarks, concept naming, false-debate debunking). The gap was never content quality; it is distribution strategy and consistency. This plan is a distribution and sequencing plan, not a "write better content" plan.
