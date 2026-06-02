# section-01-foundation — Styling foundation (blocks everything)

> Self-contained. Depends on: **nothing**. Blocks: every other section (02–05). Branch:
> `feature/content-studio-redesign` off `v2-rebuild`. Web app root:
> `src/PersonalBrandAssistant.Web/`. No backend/API/DB changes. `signalr.service.ts` untouched.

## Why this section exists (background)

The Angular app (`PersonalBrandAssistant.Web`) currently ships a generic **GitHub-dark** theme. The
design target is a "terracotta-on-obsidian" writing studio. Two structural problems block all
redesign work:

1. **`angular.json` builds `src/styles.scss`**, and that file is GitHub-dark
   (`body { background:#0f1117; color:#e1e4e8 }` + a `#30363d/#161b22` scrollbar). It `@use`s **none**
   of the partials under `src/styles/`.
2. **No CSS custom properties exist anywhere.** There is no `--brand-primary`, `--surface-card`,
   `--accent-soft`, etc. Any component authored against `var(--surface-card)` renders **unstyled**
   (the var resolves to nothing). Every redesigned component in sections 02–05 is authored against
   these tokens, so they must exist first.

The SCSS partials in `src/styles/` already hold *most* of the correct terracotta `$variables` but are
orphaned (never imported) and have gaps/wrong values. This section makes the token system **real and
referenceable** and removes GitHub-dark.

**Design CSS source of truth:** exact values are reproduced inline below. (They originate from the
prototype `<style>` block; you do not need to open the prototype.)

## Files in scope

Create / modify (all under `src/PersonalBrandAssistant.Web/src/`):

| File | Action |
|------|--------|
| `package.json` | MODIFY — add `@angular/cdk@^19.2.0` + TipTap deps; verify `marked` |
| `styles/_tokens.scss` | **NEW** — `:root { … }` custom-property block (the design system) |
| `styles/_variables.scss` | MODIFY — add idea/archived status, fix published/draft, accent-soft, delivery pairs, radius scale, sidebar 212 |
| `styles/_status-badges.scss` | MODIFY — add `.status-idea`, real `.status-archived`, `.delivery-badge--auto|--manual|--warn` |
| `styles/_layout.scss` | MODIFY — app shell grid `212px 1fr`, reconcile sidebar widths to 212 |
| `styles.scss` | MODIFY — `@use` partials, drop GitHub-dark body/scrollbar, add reduced-motion media query |
| `app/shell/sidebar/sidebar.component.ts` (+ its `.html`) | MODIFY — recolor 11 hexes → `var(--…)`, footer user block, serif brand, active-nav restyle |
| `app/features/content/**` (10 files, 59 hexes) | MODIFY — recolor GitHub-dark hexes → `var(--…)` |

Current state (verified):
- `styles.scss` is the 30-line GitHub-dark reset shown in "Acceptance" below.
- `_variables.scss` has terracotta `$vars` but `$sidebar-width: 200px`, `$status-published: #4ade80`
  (should be `#34d399`), `$status-draft: #5a5a66` (display should be `#8a8a96`), and **no**
  `$status-idea`, `$status-archived`, delivery pairs, or radius map.
- `_status-badges.scss` maps `.status-archived` to `$status-draft` (placeholder) and has no
  `.status-idea` / delivery classes.
- `_layout.scss` uses `grid-template-columns: auto 1fr auto` with `$sidebar-width` (200px).
- `sidebar.component.ts` hardcodes width **220px** and 11 GitHub-dark hexes inline; it renders from
  `sidebar.component.html` (separate template file).
- Content feature: **59** GitHub-dark hex occurrences across 10 files (`content-editor.component.ts`,
  `content-list.component.ts`, `sidecar-chat.component.ts`, `markdown-editor.component.ts`,
  `content-list-table.component.ts`, `platform-targets.component.ts`, `publish-modal.component.ts`,
  `content-grid.component.ts`, `content-card.component.ts`, `content-filter-sidebar.component.ts`).

---

## TESTS FIRST

Section 1 is almost entirely SCSS/tokens — there is no business logic to unit-test. The acceptance
gates are **build-green** and **grep-clean**, not Jasmine specs. Do these checks (in order) and treat
them as the test suite for this section.

```
# Gate 1 (build): `ng build` succeeds after wiring partials + tokens.
#   cd src/PersonalBrandAssistant.Web && npx ng build
#   Must complete with no SCSS resolution errors and no "undefined variable" failures.

# Gate 2 (sanity, optional unit test): a fixture element with `background: var(--surface-card)`
#   computes to #141418 via getComputedStyle under Karma/jsdom — proves :root tokens resolve.
#   (Optional. If you write it, colocate as a throwaway spec and assert the rgb() equivalent of
#    #141418 = rgb(20, 20, 24).)

# Gate 3 (grep gate — review, not a unit test): NO GitHub-dark hexes remain under
#   app/features/content or app/shell/sidebar. Run from src/PersonalBrandAssistant.Web/src:
#     grep -rniE '#0d1117|#161b22|#30363d|#58a6ff|#1f6feb' app/features/content app/shell/sidebar
#   Expected: zero matches. (These five are the canonical GitHub-dark hexes.)
#   Also sweep the broader set actually present today and confirm none of the GitHub-dark ones
#   survive: #0f1117, #e1e4e8, #8b949e, #f0f6fc, #1c2128, #484f58, plus the five above.

# Gate 4 (visual sanity): sidebar shows the footer user block (gradient avatar + name + "Solo
#   studio"); the brand renders as DM Serif Display with an italic terracotta "v2"; the active nav
#   item shows accent-soft bg + terracotta icon.
```

Run the project test command at the end to confirm nothing regressed:
`cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless`.

---

## IMPLEMENTATION

### Step 1 — Dependencies (`package.json`)

1. Add **`@angular/cdk@^19.2.0`** (CDK major must track Angular major exactly; the app is Angular
   `^19.2.0`, latest 19.x is 19.2.19). **Do NOT use `^20`.** CDK is not currently installed (no CDK
   imports anywhere). Used by section 04's kanban board; added here so the dependency change is a
   single foundation step.
2. Add **TipTap deps** for the section-05 prose editor: `@tiptap/core`, `@tiptap/pm`,
   `@tiptap/starter-kit`, and a markdown serialization extension (`tiptap-markdown`). Confirm they
   build under the app's ESM toolchain (run an `ng build` after install).
3. **Verify `marked`:** `ngx-markdown@^21.3.0` bundles `marked` transitively. Confirm the resolved
   `marked` exports **`lexer`** and **`walkTokens`** (used by section-05 markdown adapters). If the
   transitive version does not expose them cleanly, add an **explicit compatible `marked`** dep.
   Verify the resolved version (`npm ls marked`) before relying on the token API downstream.
4. Run `npm install`. Run `npm audit` per security policy before committing the lockfile.

> Why front-load TipTap/CDK here even though sections 04/05 consume them: the global rule is one
> foundation step for dependency churn, so later sections never touch `package.json`.

### Step 2 — `styles/_tokens.scss` (NEW)

Author a single `:root { … }` block defining the **full** custom-property set. These are the values
every redesigned component reads. Exact values:

```scss
:root {
  /* brand */
  --brand-primary: #c87156;
  --brand-primary-hover: #d4836a;
  --brand-primary-active: #b5624a;

  /* surfaces */
  --surface-base: #0e0e10;
  --surface-card: #141418;
  --surface-elevated: #1a1a20;
  --surface-hover: #22222a;
  --surface-border: #2c2c36;
  --surface-disabled: #3a3a46;
  --surface-sidebar: #0b0b0d;
  --surface-inset: #0c0c0e;
  --surface-publish-canvas: #08080a;

  /* text */
  --text-primary: #f0f0f5;
  --text-secondary: #8a8a96;
  --text-muted: #5a5a66;

  /* accent */
  --accent-soft: rgba(200, 113, 86, .13);   /* brand @ ~13% (prototype used #c8715622) */

  /* status (7-enum: Idea, Draft, Review, Approved, Scheduled, Published, Archived) */
  --status-idea: #8a7df0;
  --status-draft: #8a8a96;
  --status-review: #c87156;
  --status-approved: #4ade80;
  --status-scheduled: #60a5fa;
  --status-published: #34d399;
  --status-archived: #5a5a66;

  /* voice bands */
  --voice-high: #4ade80;
  --voice-mid: #fbbf24;
  --voice-low: #f87171;

  /* delivery badge pairs */
  --delivery-auto-bg: #1f3a2b;   --delivery-auto-fg: #4ade80;
  --delivery-manual-bg: #3a2f1c; --delivery-manual-fg: #fbbf24;
  --delivery-warn-bg: #3a2420;   --delivery-warn-fg: #f0935f;

  /* radius scale */
  --r: 12px;
  --r-inner: 10px;
  --r-control: 8px;
  --r-pill: 99px;
  --r-modal: 16px;

  /* fonts */
  --font-body: 'DM Sans', sans-serif;
  --font-display: 'DM Serif Display', Georgia, serif;
  --font-mono: 'JetBrains Mono', monospace;
}
```

To avoid drift between SCSS `$vars` and CSS custom props, prefer deriving `:root` values from the
`$variables` (e.g. `--surface-card: #{$surface-card};`) where a `$var` already exists. Where a token
has no `$var` (sidebar/inset/publish-canvas, delivery pairs, idea/archived, radius scale), add the
`$var` in Step 3 first, then reference it here. The literal hexes above are the authority either way.

### Step 3 — `styles/_variables.scss` (MODIFY)

Bring the SCSS layer in line with the tokens so partials and component SCSS keep compiling:
- Add `$status-idea: #8a7df0;` and `$status-archived: #5a5a66;`.
- Fix `$status-published: #34d399;` (was `#4ade80`).
- Change `$status-draft: #8a8a96;` (was `#5a5a66`) — this is the **display** color.
- Add the sidebar/inset/publish-canvas surfaces (`$surface-sidebar: #0b0b0d`,
  `$surface-inset: #0c0c0e`, `$surface-publish-canvas: #08080a`) and `$accent-soft: rgba(200,113,86,.13)`.
- Add the **delivery** pairs and a **radius** map matching the `--r*` tokens.
- Set `$sidebar-width: 212px;` (was `200px`).

Keep `$vars` and `:root` consistent (ideally `:root` in `_tokens.scss` derives from these `$vars`).

### Step 4 — `styles/_status-badges.scss` (MODIFY)

`@use 'variables' as *;` already present. Existing file maps `.status-draft/review/approved/
scheduled/publishing/published/failed/archived` via `::before { background: $status-… }`. Changes:
- Add `.status-idea::before { background: $status-idea; }` (or `var(--status-idea)`).
- Make `.status-archived::before` use the **real** `$status-archived` (`#5a5a66`), not the
  `$status-draft` placeholder it uses today.
- Add delivery-badge classes driven by the delivery tokens:
  - `.delivery-badge--auto { background: var(--delivery-auto-bg); color: var(--delivery-auto-fg); }`
  - `.delivery-badge--manual { background: var(--delivery-manual-bg); color: var(--delivery-manual-fg); }`
  - `.delivery-badge--warn { background: var(--delivery-warn-bg); color: var(--delivery-warn-fg); }`
  (Section 05 consumes these classes; defining them here keeps badge styling centralized.)

### Step 5 — `styles/_layout.scss` (MODIFY)

Set the app shell grid to **`212px 1fr`** full height and reconcile the three conflicting sidebar
widths (the layout uses `$sidebar-width` 200, the sidebar component hardcodes 220) to **212**.
- Current: `grid-template-columns: auto 1fr auto;` with `.sidebar-area { width: $sidebar-width; }`.
  After Step 3, `$sidebar-width` is 212. Either keep `auto 1fr auto` (sidebar width drives column) or
  switch to an explicit `212px 1fr` per the plan — pick `212px 1fr` for the content shell so the
  column is deterministic. Sidecar column behavior (`auto`/hidden) is preserved.
- Keep `$sidebar-collapsed-width`, `$header-height`, `$sidecar-width` untouched.

### Step 6 — `styles.scss` (MODIFY)

Replace the GitHub-dark global with token-driven values and wire the partials. Current file is:

```scss
/* CURRENT — replace */
body { background: #0f1117; color: #e1e4e8; … }
::-webkit-scrollbar-track { background: #161b22; }
::-webkit-scrollbar-thumb { background: #30363d; … }
::-webkit-scrollbar-thumb:hover { background: #484f58; }
```

Do:
1. `@use` the partials so tokens/vars/layout/badges/animations actually load. Order: tokens first
   (`@use 'styles/tokens';`), then variables, then the rest. (`@use` is namespaced; partials already
   `@use 'variables' as *;` internally — keep that.) Ensure `_tokens.scss` is emitted into the
   cascade (a partial with only `:root{}` must be `@use`d / `@forward`ed from `styles.scss`).
2. Keep the `* { margin/padding/box-sizing }` reset.
3. Replace `body` background/color with `var(--surface-base)` / `var(--text-primary)` and set
   `font-family: var(--font-body);`.
4. Replace the scrollbar hexes: track → `var(--surface-base)` (or `--surface-card`), thumb →
   `var(--surface-border)`, thumb hover → `var(--surface-disabled)`.
5. Add a global reduced-motion rule (covers bespoke + PrimeNG animations in later sections):

```scss
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: .001ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: .001ms !important;
    scroll-behavior: auto !important;
  }
}
```

### Step 7 — Sidebar restyle (`app/shell/sidebar/sidebar.component.ts` + `.html`)

The component holds 11 GitHub-dark hexes in its inline `styles: [\`…\`]` block (verified list:
`#161b22` ×2, `#30363d` ×3, `#f0f6fc` ×2, `#58a6ff`, `#8b949e`, `#e1e4e8`, `#1c2128`, plus
`#1f6feb22` / `rgba(31,111,235,…)` active states). Restyle:
- Replace **all** hexes with `var(--…)`: `:host` bg → `var(--surface-sidebar)` (#0b0b0d),
  border-right → `1px solid var(--surface-border)`, width/min-width **212px** (was 220), padding
  `18px 0 14px`.
- **Brand:** DM Serif Display 24px, line-height 1, letter-spacing .3px, padding `6px 22px 22px`;
  the `.brand span` (the "v2" accent) → `color: var(--brand-primary); font-style: italic;`. Color the
  rest `var(--text-primary)`. (The brand text lives in `sidebar.component.html` — confirm the markup
  has a `<span>` for the accent; add one if the template hardcodes the brand as a single string.)
- **Nav:** container padding `0 12px`, item gap 2px; item padding `9px 12px`, icon+label gap 13px,
  idle color `var(--text-secondary)`; hover bg `var(--surface-hover)` + color `var(--text-primary)`,
  transition .15s ease. **Active item:** bg `var(--accent-soft)` + icon color `var(--brand-primary)`
  (replaces the `#1f6feb22` / `rgba(31,111,235,…)` active states). `RouterLinkActive` already applies
  `.active`; restyle that class.
- **Footer user block (NEW markup in `.html` + styles in `.ts`):** a `:host`-bottom block, top border
  `1px solid var(--surface-border)`, containing a **32px gradient avatar**
  (`background: linear-gradient(135deg, var(--brand-primary), #9c5440); border-radius: 50%;`), a name
  `13px/600 var(--text-primary)`, and "Solo studio" `11px var(--text-muted)`. Use `flex-direction:
  column` so the footer sits below the nav (`:host` is already `display:flex; flex-direction:column`).
- Keep the `@media (max-width:768px)` mobile bottom-bar behavior; recolor its hexes too (the footer
  user block can hide on mobile like `.brand` does).

### Step 8 — Recolor the content feature (10 files, 59 hexes)

Replace every GitHub-dark hex across the content feature with the nearest `var(--…)`. This is
mechanical token substitution; the mapping:

| GitHub-dark hex | Replace with |
|-----------------|--------------|
| `#0f1117` / `#0d1117` | `var(--surface-base)` |
| `#161b22` | `var(--surface-card)` |
| `#1c2128` | `var(--surface-hover)` |
| `#30363d` | `var(--surface-border)` |
| `#484f58` | `var(--surface-disabled)` |
| `#e1e4e8` | `var(--text-primary)` |
| `#f0f6fc` | `var(--text-primary)` |
| `#8b949e` | `var(--text-secondary)` |
| `#58a6ff` / `#1f6feb` (and `…22` alpha forms) | `var(--brand-primary)` / `var(--accent-soft)` for soft backgrounds |

Files (occurrence counts verified): `content-editor.component.ts` (6),
`content-list.component.ts` (3), `sidecar-chat.component.ts` (11), `markdown-editor.component.ts` (6 —
this file is **deleted** in section 04; recoloring it now is harmless but optional, lowest priority),
`content-list-table.component.ts` (11), `platform-targets.component.ts` (1),
`publish-modal.component.ts` (7), `content-grid.component.ts` (1), `content-card.component.ts` (10),
`content-filter-sidebar.component.ts` (3 — this file is **removed** in section 04; recolor optional).

Recolor only — **do not restructure** these components here; their layout/behavior rewrites belong to
sections 04/05. The goal of this step is solely "no GitHub-dark hex survives the grep gate."

> Skip-with-care note: `markdown-editor` and `content-filter-sidebar` are slated for deletion/removal
> in section 04. You may leave their hexes if you trust section 04 to delete them, but the grep gate
> (Gate 3) runs over the whole `features/content` tree now — so either recolor them or accept the gate
> will still flag them until section 04 lands. Safest: recolor everything now so Gate 3 is clean at
> the end of this section.

---

## Acceptance (must all pass before merging this section)

1. `ng build` is clean (no SCSS resolution / undefined-variable errors).
2. A throwaway element styled `background: var(--surface-card)` renders **#141418** (rgb(20,20,24)).
3. **No** GitHub-dark hexes (`#0d1117` / `#161b22` / `#30363d` / `#58a6ff` / `#1f6feb`) remain under
   `app/features/content` or `app/shell/sidebar` (Gate 3 grep returns zero).
4. Sidebar shows the footer user block; brand is DM Serif Display with italic terracotta "v2"; active
   nav item shows accent-soft bg + terracotta icon; sidebar width is 212px.
5. `ng test --watch=false --browsers=ChromeHeadless` passes (no regression).

## Downstream dependencies (reference only — do not implement here)

- **Section 02** authors atoms/store against these `var(--…)` tokens and uses the new
  `$status-idea/$status-archived`.
- **Section 03** consumes `.delivery-badge--*` classes and the delivery tokens, plus CDK + `marked`.
- **Section 04** consumes `@angular/cdk` (kanban) and **deletes** `markdown-editor/` +
  `content-filter-sidebar/` (and may remove orphaned `@codemirror/*` deps then — not here).
- **Section 05** consumes TipTap deps + `marked.lexer`/`walkTokens` and the radius/modal tokens.

---

## IMPLEMENTATION NOTES (actual — as built)

Built and committed. Deviations from the plan above:

- **Deps installed:** `@angular/cdk@^19.2.19`, `@tiptap/core@^3.24`, `@tiptap/pm@^3.24`,
  `@tiptap/starter-kit@^3.24`, `tiptap-markdown@^0.9`. `marked@^17.0.4` was ALREADY a direct
  dependency (exposes `lexer`/`walkTokens`) — no marked change needed.
  - **Known risk (section-05 gate):** `tiptap-markdown@0.9` historically targets TipTap v2; v3.24
    was installed. npm resolved peers cleanly and nothing imports TipTap yet. Section 05 must verify
    a v3-compatible markdown round-trip or use the documented `<textarea>` fallback.
- **App shell is FLEX, not grid.** `_layout.scss` `.app-shell` grid is orphaned/unused; the real
  shell is `layout.component.ts` (`:host{display:flex}` + sidebar fixed-width + flex content). The
  212px width is set on the sidebar component's `:host`. The `_layout.scss` grid edit (212px 1fr) is
  dead but harmless — candidate for a later cleanup. Also recolored `layout.component.ts`'s
  `#0d1117` → `var(--surface-base)` (was an extra GitHub-dark hex in shell, not in the original list).
- **Content recolor delegated** to a subagent (mechanical hex→token swap, alpha-forms-first). All 10
  files done; grep gate returns zero GitHub-dark hexes across `app/features/content`, `app/shell/sidebar`,
  `app/shell/layout`.
- **Recolor nits (auto-resolved later):** in `content-card` + `content-list-table`, source greens
  `#3fb950` (Approved) and `#2ea043` (Published) both collapsed to `var(--status-approved)` —
  Published/Approved look identical until section 04 rewrites those files with the proper `STATUS_META`
  map. Off-palette literals (`#bc8cff`, `#39d2c0`) left as-is (not GitHub-dark).
- **Build status:** `ng build --configuration development` succeeds. The PRODUCTION build fails ONLY on
  Angular's Google-Fonts inlining over the network (`unable to verify the first certificate`) — an
  offline/SSL-interception environment issue, not a code defect. Pre-existing template-lint warnings
  (NG8107 optional-chain, unused RouterLink) are unrelated to this section.
- **Code review:** CLEAN (no critical/should-fix). See
  `implementation/code_review/section-01-review.md`.

