# Section 04: Blog Connector Migration -- Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-05-27
**Verdict:** WARNING -- one HIGH issue, several MEDIUM items. Mergeable after fixing the HIGH item.

---

## Summary

Clean migration from IBlogConnector to IPlatformConnector. The keyed DI pattern is correct, the old interface is fully deleted, tests are comprehensive and cover the new contract well. The Markdig/template responsibilities were properly moved to BlogFormatter (section 03). One real bug found: PlatformPostId is never persisted despite being available on PlatformPublishResult.

---

## HIGH -- Must Fix

### [HIGH-1] PlatformPostId is never persisted to ContentPlatformPublish

**Files:**
- `src/PBA.Application/Features/Content/Commands/PublishContent.cs:52-58`
- `src/PBA.Infrastructure/Publishing/ContentPublisher.cs:47-54`

**Issue:** Both PublishContent.Handler and ContentPublisher create a ContentPlatformPublish record but never set PlatformPostId from result.PlatformPostId. The BlogConnector returns the slug as PlatformPostId, and the entity has the column (500-char max in ContentPlatformPublishConfiguration), but the value is silently discarded. This matters for cross-post deduplication and the upcoming Medium/LinkedIn/Twitter connectors where PlatformPostId is the external post identifier.

**Fix:** Add PlatformPostId = result?.PlatformPostId to the ContentPlatformPublish object initializer in both files. Add a test in ContentPublisherTests verifying PlatformPostId is persisted.

---

## MEDIUM -- Should Fix
### [MED-1] ContentPublisher silently records success even when publish fails

**File:** `src/PBA.Infrastructure/Publishing/ContentPublisher.cs:31-56`

**Issue:** When blogConnector.PublishAsync returns result.Success == false, the code still proceeds to fire the state machine transition, set Status = PublishStatus.Published, and save. The publish is recorded as successful when it actually failed. Compare with PublishContent.Handler which correctly returns Result.Fail(...) on failure.

**Fix:** Check result.Success before proceeding. On failure, log and return early or set Status = PublishStatus.Failed and persist the ErrorMessage.

### [MED-2] TransformedContent is set to raw content.Body -- no transformation applied

**Files:**
- `src/PBA.Application/Features/Content/Commands/PublishContent.cs:41`
- `src/PBA.Infrastructure/Publishing/ContentPublisher.cs:36`

**Issue:** Both callers pass content.Body directly as TransformedContent. BlogFormatter (section 03) handles markdown-to-HTML conversion. With content.Body passed raw, the blog receives unrendered markdown instead of HTML. The section plan notes this is intentional (pipeline wiring comes in section 05). Any publish before section 05 will write raw markdown.

**No code change needed if section 05 lands before any publish.**

### [MED-3] BlogConnectorOptions.TemplatePath and Author are now dead properties in BlogConnector

**File:** `src/PBA.Infrastructure/Connectors/BlogConnectorOptions.cs`

**Issue:** TemplatePath is required but no longer used by BlogConnector -- only by BlogFormatter. Not broken (shared options), but confusing. Consider renaming to BlogOptions in a later cleanup pass.

### [MED-4] RemoveAll in TestWebApplicationFactory is fragile for multi-connector future

**File:** `tests/PBA.Api.Tests/TestWebApplicationFactory.cs:52`

**Issue:** `services.RemoveAll<IPlatformConnector>()` will remove ALL platform connector registrations. Fine today but will break when new connectors are registered. No change needed now.

---

## SUGGESTIONS -- Consider Improving

### [SUG-1] ArgumentException re-throw creates inconsistent error contract

**File:** `src/PBA.Infrastructure/Connectors/BlogConnector.cs:54-57`

**Issue:** The catch block re-throws ArgumentException while catching other exceptions into PlatformPublishResult(false, ...). Mixed error model. PublishContent.Handler has no try/catch, so ArgumentException becomes a 500. Recommendation: return a failure result for argument validation too (pure Result pattern).

### [SUG-2] Missing test: publish with null request or null Content

Low risk since PlatformPublishRequest is a record with non-nullable Content.

### [SUG-3] GetCapabilities allocates a new record + list on every call

Since capabilities are static, could be a cached static field. Minor.

### [SUG-4] No test for cancellation token propagation

No tests verify CancellationToken is forwarded to File.WriteAllTextAsync or process runner.

---

## Positive Observations

1. **Keyed DI is correct.** AddKeyedScoped with FromKeyedServices is the right .NET 8+ pattern.
2. **Clean interface deletion.** Zero remaining references to IBlogConnector.
3. **Git command injection prevention preserved.** The --file=- with stdin survives the migration.
4. **Test coverage is solid.** 17 tests covering happy path, git failure modes, argument validation, capabilities, credential validation, slug generation, interface conformance.
5. **Separation of concerns correct.** Markdig and template logic fully removed from BlogConnector.
6. **TestWebApplicationFactory correctly uses keyed singleton registration.**

---

## Checklist

| Check | Status |
|-------|--------|
| IBlogConnector fully removed | PASS |
| Keyed DI registration correct | PASS |
| IPlatformConnector contract fully implemented | PASS |
| Error handling: Result pattern for git failures | PASS |
| Error handling: ArgumentException contract | WARN (mixed throw/result model) |
| PlatformPostId persisted | FAIL (HIGH-1) |
| ContentPublisher handles failure | FAIL (MED-1) |
| Test coverage for new methods | PASS |
| No hardcoded secrets | PASS |
| No remaining IBlogConnector references | PASS |
| Thread safety | PASS (scoped lifetime, no shared mutable state) |
| File size within limits | PASS (all files under 150 lines) |

---

## Verdict

**WARNING** -- Fix HIGH-1 (PlatformPostId not persisted) and MED-1 (ContentPublisher ignoring failure) before merging. The rest can ride as-is or be addressed in section 05.
