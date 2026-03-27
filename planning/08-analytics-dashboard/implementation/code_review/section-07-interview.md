# Section 07 Code Review Interview

**Date:** 2026-03-25
**Verdict:** APPROVE (2 warnings auto-fixed, 3 suggestions let go)

## Triage

### Auto-fix (low risk, obvious improvements)

1. **Missing error handling test** - Add one test that flushes a 500 error to verify Observable error propagation. This is a standard Angular testing pattern.

2. **Missing refresh flag test** - Add one test for `getEngagementTimeline` with `refresh=true` to close the gap. One method is sufficient since all use the same `periodToParams` utility.

### Let go (nitpicks, not actionable)

1. **DashboardPeriod custom range date validation** - Backend validates at the boundary. Store/component will guard in section-08.
2. **periodToParams in separate file** - File is well under 200 lines. Not needed now.
3. **Hardcoded baseUrl in tests** - Acceptable for test stability.

## Applied Fixes

### Fix 1: Error handling test added to `getDashboardSummary` describe block
```typescript
it('should propagate HTTP errors', () => {
  service.getDashboardSummary('7d').subscribe({
    next: () => fail('should have errored'),
    error: (err) => expect(err.status).toBe(500),
  });
  const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?period=7d`);
  req.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
});
```

### Fix 2: Refresh flag test added to `getEngagementTimeline` describe block
```typescript
it('should append refresh=true when refresh flag is set', () => {
  service.getEngagementTimeline('30d', true).subscribe();
  const req = httpMock.expectOne(`${baseUrl}/analytics/engagement-timeline?period=30d&refresh=true`);
  expect(req.request.method).toBe('GET');
  req.flush([]);
});
```
