<!-- PROJECT_CONFIG
runtime: typescript-npm
test_command: cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-foundation
section-02-shared-atoms-store
section-03-publish-overlay
section-04-content-list
section-05-editor
END_MANIFEST -->

# Implementation Sections Index — Content Studio Redesign

Angular 19 frontend, `@ngrx/signals`, PrimeNG 20. All work on one branch
`feature/content-studio-redesign` off `v2-rebuild`. Web app lives at
`src/PersonalBrandAssistant.Web/`; tests are colocated `*.spec.ts`, run via the PROJECT_CONFIG
command. No backend changes; `signalr.service.ts` untouched.

Plan source: `../claude-plan.md` (implementation) + `../claude-plan-tdd.md` (tests-first).
Design CSS source of truth: exact values in `../claude-research.md` §A.

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-foundation | - | all | Yes (alone) |
| section-02-shared-atoms-store | 01 | 03, 04, 05 | No |
| section-03-publish-overlay | 02 | 04, 05 | No |
| section-04-content-list | 02, 03 | - | Yes (with 05) |
| section-05-editor | 02, 03 | - | Yes (with 04) |

Rationale for 03 before 04/05: both the list detail-drawer ("Publish →") and the editor action bar
open the bespoke **publish modal** built in section 03. Building it first avoids forward references;
04 and 05 then run in parallel.

## Execution Order
1. section-01-foundation (no dependencies — token system + deps; blocks everything)
2. section-02-shared-atoms-store (after 01)
3. section-03-publish-overlay (after 02)
4. section-04-content-list AND section-05-editor (parallel after 03)

## Section Summaries

### section-01-foundation
The styling/dependency prerequisite. Add `@angular/cdk@^19.2.0` + TipTap deps; verify `marked`
(via ngx-markdown) exposes `lexer`/`walkTokens`. Author `:root` CSS custom-property token block
(`_tokens.scss`), wire orphaned `styles/` partials into `src/styles.scss`, fix `_variables.scss`
(add idea/archived status, fix published, accent-soft, delivery pairs, radius scale, sidebar 212),
app grid `212px 1fr`, status-badge classes. Recolor content feature (~90 hexes) + sidebar (11) from
GitHub-dark to `var(--…)`; add sidebar footer user block + serif brand. Plan §"Section 1"; TDD §1
(build green + no GitHub-dark hexes remain).

### section-02-shared-atoms-store
Cross-flow primitives + store. `content-display.utils.ts` additions (STATUS_META, TYPE_GLYPH,
voiceBandColor `>=80`, relativeTime, nextStatus, LEGAL_TRANSITIONS). Atom components `status-tag`,
`voice-score-ring`, `platform-dot`. `ContentStore` extensions: `viewMode 'board'|'grid'|'table'`,
load-all single source of truth, `counts`/`filtered`/`byStatus` computeds, `activeStatus`/`search`,
and the `transition(id, target)` state-machine dispatcher (legal-transition validation, optimistic
status-only patch, no fabricated `updatedAt`, rollback on error). Plan §"Section 2"; TDD §2 (pure
logic first — highest coverage).

### section-03-publish-overlay
Bespoke 1080px publish modal + the markdown adapters. `platform-metadata.ts` (PLATFORM_META,
deliveryBadge), `markdown-blocks.ts` (toBlocks + plainText via marked), `thread-splitter.ts`
(splitThread, 280 incl. `i/n` suffix), `delivery-badge`, `publish-result` (polling 2s until all
platform statuses ∈ {Published,Failed}, cap 30s), 5 preview renderers (blog/medium/substack/
linkedin/twitter). Destinations panel + schedule (schedule sends no platforms; selection disabled in
schedule mode). Modal a11y: focus-trap/Esc/scrim/aria-modal. Plan §"Section 5"; TDD §5 (pure
adapters + splitter first).

### section-04-content-list
The list flow (consumes section 02 atoms + section 03 modal). `content-list.component` orchestrator
(renders from `filtered()`), `pipeline-bar`, `content-board` (CDK kanban with
`cdkDropListEnterPredicate` enforcing legal transitions; drop → `store.transition`, Approved→Scheduled
opens schedule dialog), `content-card` board variant, refined `content-list-table`, `detail-drawer`
(`p-drawer`), `filters-popover` (`p-popover`), `studio-empty-state` (inspire + filtered). Remove
`content-filter-sidebar`. Plan §"Section 3"; TDD §3.

### section-05-editor
The editor flow (consumes section 02 atoms + section 03 modal). Relayout `content-editor.component`
(remove `p-splitter`, wire `/content/new` query-param seeding), `prose-editor` (TipTap↔signals,
markdown round-trip — write the round-trip test FIRST; fallback documented), `manuscript-surface`,
`stage-tracker`, `editor-top-bar`, `voice-meter` (uses `voiceCheck`), inline restyled `sidecar-chat`
(SignalR untouched, 3-dot thinking), restyled `platform-targets` + `@switch(status)` action bar
(logic kept). Delete `markdown-editor` (+ orphaned codemirror deps after importer check). Plan
§"Section 4"; TDD §4.
