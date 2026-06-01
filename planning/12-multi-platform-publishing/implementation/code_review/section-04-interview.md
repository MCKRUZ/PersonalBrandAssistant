# Section 04: Blog Connector Migration -- Code Review Interview

**Date:** 2026-05-27

---

## Triage Summary

| Finding | Disposition | Action |
|---------|------------|--------|
| HIGH-1: PlatformPostId not persisted | Auto-fix | Added to both PublishContent.cs and ContentPublisher.cs + test |
| MED-1: ContentPublisher ignores failure | Auto-fix | Added failure check with early return, records PublishStatus.Failed |
| MED-2: TransformedContent = raw Body | Let go | Intentional -- section 05 adds transformer pipeline wiring |
| MED-3: Dead BlogConnectorOptions props | Let go | Shared config between BlogConnector and BlogFormatter, not dead |
| MED-4: RemoveAll fragile for multi-connector | Let go | Will update TestWebApplicationFactory when new connectors land |
| SUG-1: ArgumentException mixed error model | Let go | Validation errors are programming errors, 500 is correct |
| SUG-2: Missing null request test | Let go | Record type with non-nullable Content, compiler enforces |
| SUG-3: GetCapabilities allocates per call | Let go | Minor, not hot path |
| SUG-4: No cancellation token test | Let go | Low risk |

---

## Auto-Fixes Applied

### HIGH-1: PlatformPostId not persisted

**Files modified:**
- `src/PBA.Application/Features/Content/Commands/PublishContent.cs` -- Added `PlatformPostId = result?.PlatformPostId` to ContentPlatformPublish initializer
- `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` -- Same fix

**Test added:**
- `ContentPublisherTests.PublishAsync_PersistsPlatformPostId` -- Verifies PlatformPostId flows from PlatformPublishResult to the database record

### MED-1: ContentPublisher ignores publish failure

**File modified:**
- `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` -- Added `!result.Success` check after blogConnector.PublishAsync. On failure: records ContentPlatformPublish with PublishStatus.Failed + ErrorMessage, logs warning, returns early (does NOT fire state machine transition).

**Test added:**
- `ContentPublisherTests.PublishAsync_RecordsFailure_WhenConnectorFails` -- Verifies: content stays Scheduled, record has PublishStatus.Failed and ErrorMessage

---

## Additional Fix (Build Error)

`PublishContent.cs` had a stale `IBlogConnector` reference not caught during initial file edits (the file wasn't in the original section-04 plan scope). Migrated to `[FromKeyedServices(Platform.Blog)] IPlatformConnector` with full `PlatformPublishRequest` construction and failure result checking.

---

## Test Results

455 tests passing (9 migration + 302 application + 96 infrastructure + 48 API). Up from 453 pre-review (2 new tests for review fixes).
