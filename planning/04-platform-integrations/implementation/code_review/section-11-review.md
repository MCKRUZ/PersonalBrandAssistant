# Section 11 -- Background Processors: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-15
**Verdict:** WARNING -- no critical issues, several HIGH/MEDIUM findings to address before merge

---

## Summary

Three new BackgroundService implementations (TokenRefreshProcessor, PlatformHealthMonitor, PublishCompletionPoller), one new interface method (CheckPublishStatusAsync), one new model record, three enum additions, and corresponding test suites. The code follows established patterns from ScheduledPublishProcessor consistently. Overall quality is good, but there are correctness and performance issues that should be fixed.

---

## Findings

### 1. [HIGH] PlatformHealthMonitor calls SaveChangesAsync per platform inside the loop

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs:127`

**Issue:** SaveChangesAsync is called inside the foreach loop on every successful health check. With N connected platforms, this produces N separate database round-trips. The other processors (PublishCompletionPoller, ScheduledPublishProcessor) batch saves outside the loop.

**Fix:** Move SaveChangesAsync after the foreach loop.

---

### 2. [HIGH] TokenRefreshProcessor loads expired OAuthStates into memory instead of using ExecuteDeleteAsync

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs:90-102`

**Issue:** The plan calls for ExecuteDeleteAsync, but the implementation uses ToListAsync + Remove + SaveChangesAsync. O(N) memory for what should be a single SQL DELETE. The codebase already uses ExecuteDeleteAsync elsewhere.

**Fix:** Replace with ExecuteDeleteAsync.

---

### 3. [HIGH] PlatformHealthMonitor does not check result of token refresh attempt

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs:137-138`

**Issue:** RefreshTokenAsync is called but the result is discarded. No notification or corrective action on failure. The plan specifies logging on failure.

**Fix:** Capture the result and log/notify on failure.

---

### 4. [HIGH] PlatformTokenExpiring enum value is never used in production code

**File:** `src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs:11`

**Issue:** PlatformTokenExpiring is added but no code path sends this notification. Dead code / incomplete.

**Fix:** Send notification in TokenRefreshProcessor for Instagram token expiry warnings.

---

### 5. [MEDIUM] PublishCompletionPoller dereferences PlatformPostId with null-forgiving operator

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs:53`

**Issue:** entry.PlatformPostId! uses null-forgiving operator. Could pass null to adapter on data corruption.

**Fix:** Add null guard or query filter.

---

### 6. [MEDIUM] Auth error detection relies on string matching

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs:131-133`

**Issue:** String matching for 401 is fragile -- could misclassify errors containing 1401.

**Fix:** Rely solely on ErrorCode.Unauthorized.

---

### 7. [MEDIUM] TokenRefreshProcessor disconnects on first failure with no retry

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs:77-85`

**Issue:** Single failure disconnects. ScheduledPublishProcessor uses retry/backoff.

**Fix:** Add retry counter; disconnect after 3+ consecutive failures.

---

### 8. [MEDIUM] Test coverage gaps

**Files:** All three test files

**Issue:** Missing: Instagram log-level test, Failed status polling test, 3-platform happy path test, SaveChangesAsync call count test.

**Fix:** Add the missing test cases.

---

### 9. [MEDIUM] Unused import in TokenRefreshProcessorTests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TokenRefreshProcessorTests.cs:12`

**Issue:** using MediatR; is unused.

**Fix:** Remove it.

---

### 10. [LOW] Notification spam from scope checks

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs:156-159`

**Issue:** 96 notifications/day per misconfigured platform.

**Fix:** Add 24-hour cooldown per platform.

---

### 11. [LOW] IOAuthManager resolved unnecessarily

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs:105`

**Issue:** Resolved every cycle, used only on auth errors.

**Fix:** Resolve lazily or accept minor overhead.

---

### 12. [LOW] Default CheckPublishStatusAsync could mask bugs

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs:66-69`

**Issue:** Default returns Published. New platforms needing polling could forget to override.

**Fix:** Consider NotSupportedException in base or add XML doc comment.

---

## Approval Decision

**WARNING** -- Merge with caution after addressing HIGH findings.

**Must fix before merge (HIGH):**
1. Finding #1: Batch SaveChangesAsync outside the loop in PlatformHealthMonitor
2. Finding #2: Use ExecuteDeleteAsync for OAuthState cleanup
3. Finding #3: Handle failed token refresh result in PlatformHealthMonitor
4. Finding #4: Either use PlatformTokenExpiring notification or remove the dead enum value

**Should fix (MEDIUM):**
5. Finding #5: Guard against null PlatformPostId
6. Finding #6: Remove fragile string-matching auth detection
7. Finding #7: Add retry tolerance before disconnecting on refresh failure
8. Finding #8: Add missing test cases
9. Finding #9: Remove unused MediatR import

**Consider (LOW):**
10-12. Notification deduplication, lazy resolution, default return value documentation
