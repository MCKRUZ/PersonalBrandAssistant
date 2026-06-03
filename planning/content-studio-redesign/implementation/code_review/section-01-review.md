# Code Review — section-01-foundation

**Verdict:** CLEAN — no CRITICAL or SHOULD-FIX. Safe to commit.

## Verified correct
- Token wiring: `styles.scss` is the registered global stylesheet (angular.json build+test); `@use 'styles/tokens'` emits the `:root{}` block into the global cascade, so `var(--…)` resolves app-wide. No `@use` namespacing pitfall (pure CSS output).
- SCSS `$var` consistency: every `$var` referenced in `_status-badges.scss` exists; `:root` tokens match `$variables` 1:1.
- Recolor: all 10 files spot-checked — no corrupted strings, no broken `var(--…)NN` alpha remnants; alpha-hex correctly expanded to `rgba()`; semantic mappings sane.
- Sidebar: `userInitials`/`userName` declared; footer markup balanced; 212px width.
- `@angular/cdk@^19.2.19` aligns with Angular 19.2.x.

## Triage of NITs (all let-go or auto-resolved — no user interview needed)
- **Published/Approved green collapse** (`content-card`, `content-list-table`): both source greens `#3fb950`/`#2ea043` → `var(--status-approved)`; `--status-published` (#34d399) unused there. LET GO — both files are fully rewritten in section 04 with the proper `STATUS_META` color map.
- **`--font-display` fallback divergence** (Georgia in tokens vs not in $variables): cosmetic. LET GO.
- **Off-palette literals left** (`#bc8cff` Review, `#39d2c0` Scheduled, etc.): not GitHub-dark so gate-clean; files rewritten later. LET GO.
- **`tiptap-markdown@0.9` vs `@tiptap/core@3.24` peer mismatch:** KNOWN RISK, deferred to section-05 round-trip gate (nothing imports TipTap yet). No action now.
- **Orphaned `_layout.scss`:** dead grid edit, harmless; candidate for a later cleanup. LET GO.

## Auto-fixes applied
None — review was clean; no fixes required.
