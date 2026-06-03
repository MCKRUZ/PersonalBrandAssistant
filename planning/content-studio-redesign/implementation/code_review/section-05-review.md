# Code Review — section-05-editor

**Verdict:** CLEAN / Ship — no CRITICAL or SHOULD-FIX. Two nits, both applied.

## Verified (the risk areas)
- **prose-editor (TipTap):** caret guard correct — `setContent` only when incoming value differs
  from `lastSerialized` AND editor not focused; external apply uses `{ emitUpdate: false }` and
  `onDocUpdate` sets `lastSerialized` before the debounced emit → no serialize→input→setContent loop.
  `setEditable(!readOnly)` honored; editor destroyed in teardown (ngOnDestroy + destroyRef); 300ms
  debounce; serialization via `editor.storage.markdown.getMarkdown()`. **Round-trip spec genuinely
  asserts** md→doc→md stability across h1–h3/bold/italic/links/bullet+ordered lists/inline code
  (normalizer only collapses whitespace, doesn't mask mark loss). `tiptap-markdown@0.9` works with
  TipTap v3.24 — no fallback needed.
- **content-editor:** new-content seeding reads topic/type/sourceIdeaId from `queryParamMap` with
  sensible fallbacks; `onPublishConfirm` does NOT close the modal (section-03 behavior preserved);
  autosave gated to Idea/Draft/Review; all status handlers preserved.
- **sidecar-chat:** SignalR wiring unchanged (connect/disconnect, tokens$→appendToken,
  generationComplete$→completeGeneration, sendChatMessage); inline (no p-drawer); 3-dot thinking.
  `signalr.service.ts` git-diff empty.
- **voice-meter:** 0–1 vs 0–100 normalization is a defensible defensive heuristic (real backend `1`
  is a near-impossible content state; both map to the "low" band).
- **markdown-editor deletion:** no other importer; `@acrodata/code-editor` + `@codemirror/lang-markdown`
  removed from package.json.
- Tokens only (the `#1a0f0a` is dark text-on-brand foreground, not a GitHub-dark surface).

## Nits — APPLIED
- **Lockfile pruned:** ran `npm install` so `package-lock.json` no longer carries the removed
  `@acrodata`/`@codemirror` tree (0 entries now).
- **Added a spec** pinning the load-bearing "publish modal stays open after confirm" invariant
  (`content-editor.component.spec.ts`).

## Tests
Full suite 513 pass / 0 fail; editor spec 12 pass (incl. the new modal-stays-open test).
`ng build` clean. (Implementation delegated to a subagent from the section file; reviewed here.)
