# Code Review — section-10 Brand Profile editor (Angular)

Reviewer: `deep-implement:code-reviewer`. `ng build` + 565 frontend tests green at review time.

## Findings
- **CRITICAL — staged add/remove/reorder pillar defeated the no-smuggle guarantee.** Auto-apply fired on
  any weight/knob diff regardless of a pending definition edit; `buildWeightsOnlyRequest` mapped over
  baseline pillars, so an added pillar was silently dropped (or a removed one re-added) on a no-confirm
  weights apply. → **Fixed: auto-apply is now gated on `!definitionsDiffer()`** — once ANY definition diff
  is staged (text, add, remove, reorder) every change routes through the "Save & re-score" confirm.
- **HIGH — pillar `order` mis-routed as weights-only** (a reorder escaped the confirm). → **Fixed:** `order`
  removed from `weightsKnobsDiffer`; reorder is a definition edit (detected by the pillar-set comparison).
- **HIGH — weights-path 409 blind-retried the stale token** on every keystroke. → **Fixed:** a 409 on the
  weights apply resyncs from the server (`reloadFromServer`), refreshing token + form; no blind-retry.
- **HIGH — the definition-path 409 test proved nothing about not losing the edit.** → **Fixed:** the test
  now asserts the staged value survives in the form AND was carried in the PUT.
- **MEDIUM — no min-1-pillar validator** (emptying the array stayed "valid"). → **Fixed:** `minOnePillar`
  validator on the pillars `FormArray` + a test.
- **MEDIUM — weights apply ignored the returned version** (couldn't detect a server-side escalation). →
  **Fixed:** defense-in-depth — if a weights apply bumps the version, surface "Profile was re-scored."
- **LOW — form rendered on load failure with `baseline == null`.** → **Fixed:** form gated on `hasProfile()`.
- **LOW — load test didn't assert "no PUT on init".** → **Fixed.**
- **NIT — a11y:** added aria-labels to the weight slider and add/remove buttons.

## Coverage added
Specs now cover: add-pillar staged, remove-pillar staged, weight-nudge-while-staged (the gate), zero-pillars
blocks save, weights-path 409 resync (no loop), no-PUT-on-load, and the strengthened definition-path 409.

## Confirmed good
Service methods hit the correct section-09 route + field names; concurrency token round-tripped on every
PUT; `ready`+`emitEvent:false` patching prevents spurious auto-apply on load/refresh; `takeUntilDestroyed`
+ debounce correct; `loadIdeas` (already public on the store) is the reload trigger — `viewMode` untouched
(owned by section-11).
