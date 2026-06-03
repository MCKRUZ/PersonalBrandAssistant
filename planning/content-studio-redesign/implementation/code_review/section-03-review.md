# Code Review — section-03-publish-overlay

**Verdict:** BLOCK → resolved. One CRITICAL fixed, two nits applied; clean logic elsewhere.

## CRITICAL (fixed)
- **Result view + polling were dead code** — the editor's `onPublishConfirm` set
  `publishModalVisible.set(false)` synchronously on `confirm`, so the modal (wrapped in
  `@if (visible())`) hid immediately and the post-confirm result view never showed; polling ran
  against a hidden modal. **Fix:** removed the immediate close in
  `content-editor.component.ts:onPublishConfirm` — the modal stays open (still `visible`, with its
  internal `result()` set) to show the Publishing→Published / manual-Copy / Scheduled rows; it
  closes via the result view's "Done" (which emits `cancel`, already handled by the editor). The
  modal spec exercises the realistic flow (visible stays true → `app-publish-result` renders).

## SHOULD-FIX (fixed)
- **Polling not stopped on hide** (component not destroyed when `visible` flips false). Added an
  `else { stopPolling() }` branch to the reset effect.

## NIT (fixed / accepted)
- a11y: added `aria-labelledby="pub-title"` on the dialog + `id` on the `<h2>`; associated the
  datetime `<label for="pub-when">` with the input.
- Schedule-mode `platforms` leak: HARMLESS — toggling is blocked in schedule mode (so it's just
  `[primary]`) AND the editor's `onPublishConfirm` ignores `platforms` when `scheduledAt` is set
  (calls `schedule(id,{scheduledAt})` only). Left as-is.

## Verified clean (reviewer confirmed)
- `thread-splitter`: terminates (monotonic chunk count), worst-case ` n/n` reserve ≥ actual ` i/n`,
  so no numbered tweet exceeds the limit.
- `markdown-blocks.plainText`: no double-counting; link hrefs dropped / labels kept; visible-length
  semantics correct.
- `platform-metadata`: Twitter 280 matches splitThread default; no double-publish risk (parent makes
  exactly one publish/schedule call; modal makes none).

## Tests
Full suite 452 pass / 1 pre-existing unrelated fail (ContentCard 'Blog Post', owned by section 04).
After the fix, publish-modal + editor specs: 50 pass. `ng build` clean.
