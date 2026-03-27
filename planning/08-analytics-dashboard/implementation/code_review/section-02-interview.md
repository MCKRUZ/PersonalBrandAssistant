# Section 02 Code Review Interview

## MED-1: Load credential once (shared singleton)
**Decision:** Auto-fix. Refactored to register `GoogleCredential` as a singleton, shared by both `IGa4Client` and `ISearchConsoleClient` registrations. Eliminates duplicate file reads.

## MED-2: Index-based metric parsing
**Decision:** Let go. GA4 API guarantees metric order matches request order. This is the standard approach.

## MED-3: Real PropertyId in tests
**Decision:** Let go. Tests use mocked clients; values never hit real APIs. No security risk.

## LOW-1 through LOW-4
**Decision:** Let go. Minimal impact, not worth the churn.
