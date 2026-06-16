# Review triage & decisions — section-10

Triaged autonomously. The Critical (and the HIGHs that share its root) were real UX-contract violations and
were fixed immediately.

## Applied fixes (all the must-fixes the reviewer named)
1. **Auto-apply gated on `!definitionsDiffer()`** — the single change that closes the add/remove/reorder
   smuggling/drop hole. While any definition edit is staged, weight nudges no longer auto-apply; the user
   saves everything through the confirm dialog (which sends the full form).
2. **`order` removed from `weightsKnobsDiffer`** — reorder is a definition edit.
3. **Weights-path 409 → `reloadFromServer`** (resync token + form), never blind-retry.
4. **min-1-pillar `FormArray` validator.**
5. **Weights-apply version-bump notice** (defense in depth against the gate being bypassed server-side).
6. **Form gated on `hasProfile()`**; load test asserts no PUT on init; a11y aria-labels.
7. Strengthened the definition-path 409 test to prove the staged edit survives; added the missing specs
   (add/remove pillar staged, weight-nudge-while-staged, zero-pillars, weights-path 409).

## Decisions / deferrals
- **Component specs drive the class (manual `ngOnInit`) rather than the rendered DOM.** Kept — it isolates
  behavior and avoids PrimeNG render flakiness in headless Karma; the data-testid selectors exist in the
  template (verified by `ng build`) and the full `ng test` renders other PrimeNG components fine. The
  behavior contract (gating, confirm, 409, no-smuggle) is fully covered at the component level.
- **Server validation ranges** (decayFloor [0,1], positive multipliers) are mirrored client-side from
  section-09's validator; a value the server would 400 falls into the generic "Save failed" message
  (acceptable — the client validators prevent the common cases).
- **`audienceSecondary` `|| null` coercion** left as-is (free-text field; empty == null is the intended
  canonicalization, matching section-09's server-side `Normalize`).

## Verification after fixes
`ng test` headless: 570 specs green (incl. the new section-10 cases). `ng build` clean (brand-profile lazy
chunk emitted; the 2 build warnings are pre-existing in unrelated components).
