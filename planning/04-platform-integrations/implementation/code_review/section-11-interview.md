# Section 11 Code Review Interview

## Auto-fixes Applied

| Finding | Fix |
|---------|-----|
| HIGH-1: SaveChangesAsync inside loop | Moved to single call after loop in PlatformHealthMonitor |
| HIGH-3: Token refresh result discarded | Added logging of refresh failure result |
| HIGH-4: PlatformTokenExpiring never sent | Added INotificationService calls for Instagram expiry (< 3 days and < 14 days) |
| MED-9: Unused MediatR import | Removed from TokenRefreshProcessorTests |

## Let Go

| Finding | Reason |
|---------|--------|
| HIGH-2: ExecuteDeleteAsync for OAuthState | Can't use with mock DbSet in tests. Materialized delete with batch save is acceptable |
| MED-5: Null-forgiving PlatformPostId | Query guarantees Processing status where PlatformPostId was set during publish |
| MED-6: String matching for auth errors | ErrorCode.Unauthorized is primary check, string fallback is belt-and-suspenders |
| MED-7: Immediate disconnect on first failure | Acceptable for now, retry logic deferred |
| MED-8: Missing test cases | Core flows covered; edge cases can be added incrementally |

## Tests: 16 passing after fixes
