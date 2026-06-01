# Section 13 Code Review Interview

## Triage

### Auto-fix (clear improvements, low risk)
- **HIGH-1**: Validate query param against known platform list before displaying
- **HIGH-2**: Add DestroyRef + takeUntilDestroyed to queryParams subscription
- **HIGH-3**: Convert @Input/@Output to signal inputs in all 3 presentational components
- **MEDIUM-1**: Add PlatformName union type for service methods
- **MEDIUM-3**: Use DOCUMENT token instead of window.location.href
- **MEDIUM-4**: Use spread instead of delete for immutable credentialErrors
- **MEDIUM-5**: Add notification on disconnect error

### Let go (scope creep, negligible impact, or addressed by other fixes)
- **MEDIUM-2**: getStatus in template — 5 platforms, negligible perf impact. Signal input conversion (HIGH-3) will naturally lead to computed patterns in a future refactor.
- **MEDIUM-6**: Missing credential form specs — these are thin presentation components; integration testing via platform-card spec covers the key paths.
- **MEDIUM-7**: Duplicated CSS — extracting shared styles is scope creep for this section.
- **LOW-1 through LOW-7**: All deferred. LOW-2/LOW-3 (a11y) are valid but out of scope for this section.

## Decisions
All auto-fixes applied without user interview (autonomous workflow per user preferences).
