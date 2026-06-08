# Idea Bank + Daily Brief — UX Redesign

**Date:** 2026-06-06
**Status:** Approved (design), pending implementation plan
**Scope:** Frontend only (`PersonalBrandAssistant.Web`). No API or DB changes.

## 1. Problem

The two newest radar surfaces are visually and structurally weak:

- **Daily Brief** (`features/digest/pages/daily-brief`) is an unstyled raw `<ol>` — no `styleUrl`, no
  layout, no date, no loading/empty states, no history.
- **Idea Bank** (`features/ideas`) is more developed (3-column layout, search, view toggle, paginator)
  but hardcodes a GitHub-blue palette (`#0d1117`, `#58a6ff`) that ignores the app's design system,
  and the browse experience is thin.

Meanwhile the app already has a mature design language — the **obsidian/terracotta token system**
(`src/styles/_tokens.scss`, `_variables.scss`) that the redesigned Content Studio is built on. The
redesign brings both pages up to that bar: a full visual + layout overhaul, not a reskin.

## 2. Goals & non-goals

**Goals**
- Idea Bank optimized for **browse & explore**: strong search/filter, source variety, scannable cards.
- Daily Brief as a **daily read + history archive**.
- Both authored against `var(--…)` tokens + PrimeNG, visually consistent with Content Studio.
- AI score shown with the existing threshold colors everywhere (≥7 green, 4–6 amber, <4 red).

**Non-goals**
- No backend/API/DB changes (digest history is already served by existing endpoints).
- No change to the NgRx data flow or the `IdeaService`/`DigestService` contracts.
- Not a new theme — reuse the existing token system; do not invent new brand colors.

## 3. Constraints (verified)

- **Design tokens** exist in `_tokens.scss` / `_variables.scss`: brand `#c87156`, surface/text scales,
  status colors, **score thresholds** (`$score-success #4ade80`, `$score-warning #fbbf24`,
  `$score-danger #f87171`), radius scale (`--r`, `--r-control`, `--r-pill`), fonts.
- **PrimeNG 20** + `@primeng/themes` is the component library; `_primeng-overrides.scss` exists.
- **Digest API** already exposes everything history needs: `GET /api/digests` (list),
  `/api/digests/latest`, `/api/digests/{id}`.
- Idea data flows through `IdeaStore` (NgRx signals) with existing `ideas()`, `loading()`, `viewMode()`,
  `totalCount()`, `page()`, `pageSize()`, `setFilter()`, `setPage()` — reused as-is.

## 4. Idea Bank redesign — "browse & explore"

**Approach:** keep the 3-column shell (`240px | 1fr | 280px`), overhaul visuals + enrich browsing.
Lower risk than re-architecting; preserves the working store and routing.
*Rejected:* 2-column (drops the discovery rail — worse for explore); masonry/magazine (harder to scan).

**Left — filter rail** (`idea-filter-sidebar`, restyled): collapsible groups for source, **score range**,
category/tags, date, status. All token-styled. Active filters surface as removable **chips** above the
results so applied state is always visible.

**Center** (`ideas.component` + `idea-grid` / `idea-list` / `idea-card`):
- Header: prominent search, **sort control (score / date / source)**, grid/list toggle, active-filter chip row.
- **Idea card** (grid): thumbnail (fallback when absent), source badge, title, AI summary, **color-coded
  score badge** (threshold tokens), tags, relative age, hover actions (save / dismiss / create content).
- **List view**: dense single-line-per-idea rows for rapid scanning (score, title, source, age, actions).
- Paginator retained.

**Right — discovery rail** (`smart-suggestions`, restyled) plus a compact **score-distribution** stat
(counts per score band) computed over the **currently loaded results page** — no new endpoint. It is
labeled "this page" so it is not mistaken for a global aggregate (a global stat would need a backend
endpoint, which is out of scope). If labeling it honestly makes it feel low-value, it is the one
droppable element here (YAGNI) — decide during implementation.

## 5. Daily Brief redesign — "daily read + history"

**Approach:** two-pane. *Rejected:* single column + date dropdown (history hidden); trends dashboard (scope creep).

- **Left — history timeline** (`brief-history` component): past briefs from `GET /api/digests`, listed by
  date, latest highlighted/selected by default. Click loads that brief.
- **Right — brief detail** (`brief-detail` component): editorial layout. Header = title + date + intro.
  **Hero the #1 item** (large card: rank, color-coded score, title, why-it-matters, act link). Remaining
  items as a clean ranked list. Loads latest on init; selecting a date fetches `/api/digests/{id}`.
- Real **loading** and **empty** states (skeleton + "no briefs yet" illustration/message), not bare text.

## 6. Component breakdown

Small, focused components (one job each):

| Component | Responsibility |
|---|---|
| `idea-card` (rebuild) | One idea: thumbnail, score badge, summary, tags, actions |
| `idea-list` / `idea-grid` (restyle) | Layout of cards/rows; emit save/dismiss/createContent |
| `idea-filter-sidebar` (restyle) | Filter groups → `IdeaStore.setFilter` |
| `active-filter-chips` (new) | Render + remove applied filters |
| `score-badge` (new, shared) | Reusable color-coded score pill used by cards, list, brief |
| `score-distribution` (new) | Compact per-band counts in the discovery rail |
| `daily-brief` (rebuild) | Two-pane shell; owns selected-brief state |
| `brief-history` (new) | Timeline list of past digests |
| `brief-detail` (new) | Hero + ranked list render of one digest |

`score-badge` is shared by Idea Bank and Daily Brief to keep score styling identical.

## 7. Testing

- Jasmine/Karma component tests for each new/rebuilt component: render, loading, empty, and
  interaction (filter apply/remove, sort change, brief selection, action emits).
- `score-badge`: correct color band per score boundary (e.g. 7→green, 6→amber, 3→red).
- Reuse `HttpTestingController` patterns already in the repo for service-backed components.
- Keep existing passing tests green; update specs whose markup changes.

## 8. Risks

- **Visual taste is subjective** — mitigate by matching the established Content Studio look exactly
  rather than introducing a new aesthetic.
- **Spec churn on markup-coupled tests** — expect to update existing component specs; that's in scope.
