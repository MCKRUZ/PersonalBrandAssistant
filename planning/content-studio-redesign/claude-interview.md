# Interview Transcript — Content Studio Redesign

Pre-settled before the interview (recorded during gap analysis, 2026-06-02):
- **Body model:** markdown stays source of truth; subtitle/byline derived (not persisted);
  previews parse markdown→blocks + strip to plain text for char/thread budgets; publish
  pipeline untouched.
- **List data:** load-all client-side; counts computed in-memory; filtering/search/board
  grouping all client-side.
- **Secondary filters:** status pills primary; platform/type/date demoted behind a "Filters"
  popover (not dropped).

---

## Q1. Editor prose surface technology
The design wants a clean prose surface (40px serif title, formatted body, no visible markdown
syntax) while markdown stays source of truth and free HTML is banned. Options: TipTap
(ProseMirror) with markdown I/O / styled textarea showing raw markdown / hand-rolled
contenteditable.

**Answer:** **TipTap (ProseMirror), markdown I/O.** Load markdown → TipTap doc → edit as clean
prose → serialize back to markdown → `ContentEditorStore.updateField('body', md)`. Accept the
new dependency (`@tiptap/core`, `@tiptap/starter-kit`, a markdown serialization extension) +
an Angular wrapper component bridging to signals. This satisfies both the clean-prose design
and the markdown-source-of-truth decision, and avoids the caret/paste/drift bug class of raw
contenteditable.

## Q2. Overlay technology per surface
Publish modal / detail drawer / Filters menu — PrimeNG vs bespoke token-styled overlays.

**Answer:** **Mix.** Build the **publish modal bespoke** (token-styled) to hit the exact
prototype CSS (1080px, custom shadows, .24s pop-in, 340px/1fr grid, #08080a canvas). Use
**PrimeNG `p-drawer` (position="right")** for the detail drawer and **PrimeNG `p-popover`** for
the secondary Filters menu — both are already-used patterns and give focus-trap/scrim/a11y for
free. Pipeline bar, board, empty state = plain token-styled components.

## Q3. Thread-split character limit
README ≤270, prototype default 270, real Twitter/X 280.

**Answer:** **280 (real Twitter limit).** Use the true 280-char cap (already in
`PLATFORM_CHAR_LIMITS`). The splitter MUST reserve room for the `1/n` numbering suffix so a
numbered tweet never exceeds 280 (budget = 280 − len(suffix)). Test the boundary.

## Q4. Sequencing & delivery
~20 new files + ~15 modified across a styling foundation + 3 flows.

**Answer:** **One branch, foundation-first sections.** Single feature branch
(`feature/content-studio-redesign`, off `v2-rebuild`). Plan sequences the token/styling
foundation FIRST (wire orphaned `styles/` partials into `src/styles.scss` + author `:root`
custom-property token block + add `@angular/cdk` dep + recolor content/sidebar from hardcoded
GitHub-dark hexes to tokens), then shared atoms, then list → editor → publish. One PR (or
stacked commits) at the end. Rationale: the foundation unblocks every component (today
`var(--…)` resolves to nothing).

---

## Architect decisions made without asking (low-risk, recorded for the plan)
- **marked sourcing:** rely on the `marked` already pulled transitively by
  `ngx-markdown@21.3.0`; import `marked`/`lexer`/`walkTokens` directly. Add an explicit
  `marked` dep ONLY if the resolved version doesn't expose the lexer/walkTokens API. Verify in
  the foundation section.
- **Voice meter data:** `ContentService.voiceCheck(id) → VoiceCheckResult{score,feedback}`
  EXISTS (corrects the earlier gap-analysis note). The side-panel voice meter shows
  `Content.voiceScore` on load and can call `voiceCheck` on demand for a live re-check; the
  band note uses `VoiceCheckResult.feedback`.
- **`@angular/cdk` version:** pin `^19.2.0` to match Angular 19 (NOT ^20).
- **TipTap markdown fidelity risk:** StarterKit covers headings/bold/italic/lists/links/code;
  the markdown serialization extension (e.g. `tiptap-markdown`) is community-maintained — the
  plan must include a round-trip fidelity test (markdown → doc → markdown stable for the marks
  we use) and a fallback note if a mark doesn't round-trip cleanly.
