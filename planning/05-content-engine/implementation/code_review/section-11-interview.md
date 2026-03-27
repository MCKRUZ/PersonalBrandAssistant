# Code Review Interview: Section 11 — API Endpoints

## Triage Summary

| Issue | Decision | Action |
|-------|----------|--------|
| CRITICAL-01: No `.RequireAuthorization()` | **Let go** | Consistent with all existing endpoints — auth is via global ApiKeyMiddleware |
| CRITICAL-02: Unvalidated AutoFill query params | **Auto-fix** | Added from/to validation matching GetSlots |
| HIGH-01: Duplicated autonomy check | **Let go** | Only 2 instances, premature to abstract |
| HIGH-02: Route collision `/api/content` | **Auto-fix** | Changed RepurposingEndpoints to `/api/repurposing` |
| HIGH-03: Duplicate ValidateVoice | **Auto-fix** | Removed from ContentPipelineEndpoints; lives in BrandVoiceEndpoints |
| HIGH-04: 403 vs 422 for autonomy | **Let go** | 403 is conventional for "not permitted" operations |
| HIGH-05: Error in 202 response | **Auto-fix** | Return proper error via ToHttpResult() on failure |

## Auto-fixes Applied

1. Added `from >= to` and `> 90 days` validation to `CalendarEndpoints.AutoFill`
2. Changed RepurposingEndpoints route from `/api/content` to `/api/repurposing`
3. Removed duplicate `ValidateVoice` handler from ContentPipelineEndpoints
4. Fixed TrendEndpoints.RefreshTrends to return error result on failure instead of 202 with error string
5. Updated test routes to match `/api/repurposing`
