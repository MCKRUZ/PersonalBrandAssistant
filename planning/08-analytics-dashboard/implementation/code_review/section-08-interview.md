# Section 08 Code Review Interview

**Date:** 2026-03-25
**Verdict:** APPROVE (1 auto-fix, rest let go)

## Triage

### Auto-fix
1. **Round percentChange to 2 decimal places** - Prevents floating-point display artifacts like `19.999999999999996`. Simple `Math.round(x * 100) / 100` fix.

### Let go
1. **DRY violation (load/refresh duplication)** - The 18-line patchState block is duplicated but clear and maintainable. Collapsing into a single rxMethod<boolean|void> would add indirection. The duplication is acceptable at this scale.
2. **switchMap cancellation note** - Correct operator choice, no action needed.
3. **isStale auto-update** - UI concern for section-09 (timer/interval to trigger re-check).
4. **Error state for loadContentReport** - Nice-to-have, not needed for dashboard feature.
5. **Additional test scenarios** - Coverage is sufficient for the core behaviors.

## Applied Fixes

### Fix 1: Round percentChange output
```typescript
function percentChange(current: number, previous: number): number | null {
  if (previous === 0) return null;
  return Math.round(((current - previous) / previous) * 10000) / 100;
}
```
