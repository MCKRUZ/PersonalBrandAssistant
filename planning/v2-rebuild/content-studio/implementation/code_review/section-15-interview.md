# Section 15 Code Review Interview — Content Editor Page

## Triage Summary

| # | Finding | Severity | Decision | Rationale |
|---|---------|----------|----------|-----------|
| 1 | app.config.ts regression | CRITICAL | **Already fixed** | Restored original providers, added only provideMarkdown() |
| 2 | No error handling on create() subscribe | Important | **Auto-fix** | Obvious improvement, low risk |
| 3 | No error handling on doStatusAction | Important | **Auto-fix** | Obvious improvement, low risk |
| 4 | onCrossPostAction casts unvalidated prompt() to Platform | Important | **Auto-fix** | Validate against enum values |
| 5 | onSchedule sends raw prompt() as ISO date | Important | **Auto-fix** | Validate date parses before sending |
| 6 | ::ng-deep usage | Minor | **Let go** | Required for PrimeNG styling overrides |
| 7 | Test signals at module scope | Minor | **Let go** | Jasmine runs sequentially, acceptable |
| 8 | No takeUntilDestroyed | Minor | **Let go** | Short-lived HTTP calls, low risk |
| 9 | Hardcoded dark-theme colors | Minor | **Let go** | Consistent with codebase pattern |
| 10 | DraftActionEvent.action typed string | Minor | **Let go** | Toolbar controls emission |
| 11 | canDraft disables when hasBody | Minor | **Let go** | Intentional — use Refine instead |
| 12 | @for track usage | Minor | **Let go** | Confirmed correct Angular 19 syntax |

## Auto-Fixes Applied

### Fix #1 (Critical #1): app.config.ts regression
**Status:** Already fixed before review interview.
Restored original file content and added only `provideMarkdown()` import + provider entry.

### Fix #2 (Important #2): Error handling on create() subscribe
**Change:** Add error callback to the create() subscribe in ngOnInit new-mode branch.
Logs error to console.error and navigates back to content list on failure.

### Fix #3 (Important #3): Error handling on doStatusAction
**Change:** Add error callback to doStatusAction helper. Reloads content to get server state on failure.

### Fix #4 (Important #4): Validate platform in onCrossPostAction
**Change:** Validate prompt() input against `Object.values(Platform)` before sending. Alert user on invalid input.

### Fix #5 (Important #5): Validate date in onSchedule
**Change:** Parse prompt() string with `new Date()` and check `isNaN`. Alert user on invalid date.

## Items Let Go

#6-12: All minor findings are acceptable for the current build stage. No action needed.
