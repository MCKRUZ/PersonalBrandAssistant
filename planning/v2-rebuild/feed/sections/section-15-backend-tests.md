# Section 15: Backend Tests

## Status: IMPLEMENTED

## Overview

This section covers all xUnit tests for the Feed module backend: query handlers, command handlers, validators, mapping extensions, and the seed service. 90 tests across 14 files, all passing.

## Implementation Summary

- **90 tests** total (up from initial 82 after code review added coverage for error paths)
- **Shared helper** `FeedTestHelpers.cs` extracts `CreateContext()` and `CreateFeedItem()` factory, used by all test files via `using static`
- **Seed test consolidated** from `Seeding/FeedSeedServiceTests.cs` to `Features/Feed/FeedSeedServiceTests.cs`

## Deviations from Plan

1. **CreateFeedItem handler** uses `IFeedNotifier` interface (not `IHubContext<FeedHub>` as planned) — tests mock `IFeedNotifier` instead
2. **Validators** only allow `["approve", "dismiss", "view", "create-content"]` — plan mentioned "edit"/"schedule" which don't exist in implementation
3. **Shared FeedTestHelpers.cs** added (not in plan) per code review recommendation to reduce 7-file duplication
4. **Additional error-path tests** added per code review: sub-command failures, null ActionTargetId, ParseIdeaSuggestionData errors, BatchMarkRead Ids filtering, seed idempotency

## Technology and Patterns

- **Framework:** xUnit with Arrange-Act-Assert
- **Database:** In-memory EF Core (`UseInMemoryDatabase` with a unique `Guid` name per test to ensure isolation)
- **Mocking:** Moq for `ISender` (MediatR) and `IHubContext<FeedHub, IFeedHubClient>` in commands that dispatch sub-commands or push SignalR. Use `Mock<ILogger<T>>` for log-verifying tests.
- **Validation:** FluentValidation `TestValidate` extensions (`ShouldHaveValidationErrorFor`, `ShouldNotHaveAnyValidationErrors`)
- **Result:** `Result<T>` from `PBA.Domain.Common` -- assert `IsSuccess`, `Value`, `FailureType`, `Errors`
- **Naming:** `MethodName_Scenario_ExpectedResult`

## Shared Test Infrastructure

Every test class that exercises a handler needing `IAppDbContext` uses a local static `CreateContext()` helper:

```csharp
private static ApplicationDbContext CreateContext()
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    return new ApplicationDbContext(options);
}
```

A shared `CreateFeedItem` factory method per test class simplifies seeding:

```csharp
private static FeedItem CreateFeedItem(
    FeedItemType type = FeedItemType.AgentDraft,
    FeedItemPriority priority = FeedItemPriority.Normal,
    bool isRead = false,
    bool isActedOn = false,
    string? data = null,
    Guid? actionTargetId = null,
    DateTimeOffset? createdAt = null,
    DateTimeOffset? expiresAt = null)
{
    return new FeedItem
    {
        Type = type,
        Title = $"Test {type}",
        Summary = $"Summary for {type}",
        Data = data,
        ActionType = type switch
        {
            FeedItemType.AgentDraft => "approve",
            FeedItemType.TrendAlert => "view",
            FeedItemType.IdeaSuggestion => "create-content",
            FeedItemType.AnalyticsHighlight => "view",
            FeedItemType.ApprovalRequest => "approve",
            FeedItemType.SystemNotification => "view",
            _ => null
        },
        ActionTargetId = actionTargetId,
        Priority = priority,
        IsRead = isRead,
        IsActedOn = isActedOn,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt
    };
}
```

## File Structure (Actual)

```
tests/PBA.Application.Tests/Features/Feed/
  FeedTestHelpers.cs                          ← NEW: shared CreateContext + CreateFeedItem
  FeedSeedServiceTests.cs                     ← MOVED from Seeding/
  Queries/
    ListFeedItemsHandlerTests.cs              (10 tests)
    GetFeedSummaryHandlerTests.cs             (7 tests)
    GetTrendingTopicsHandlerTests.cs          (6 tests)
  Commands/
    MarkFeedItemReadHandlerTests.cs           (3 tests)
    ActOnFeedItemHandlerTests.cs              (17 tests)
    BatchMarkReadHandlerTests.cs              (5 tests)
    BatchDismissHandlerTests.cs               (5 tests)
    BatchActHandlerTests.cs                   (2 tests)
    CreateFeedItemHandlerTests.cs             (4 tests)
  Validators/
    ActOnFeedItemRequestValidatorTests.cs     (7 tests)
    BatchActRequestValidatorTests.cs          (7 tests)
    BatchDismissRequestValidatorTests.cs      (7 tests)
  Mappings/
    FeedMappingsTests.cs                      (2 tests)
                                              TOTAL: 90 tests
```

No changes to `PBA.Application.Tests.csproj` were needed.

## Query Handler Tests

### `ListFeedItemsHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Queries/ListFeedItemsHandlerTests.cs`

Handler under test: `ListFeedItems.Handler` (injected with `IAppDbContext`).

Tests:

1. **Handle_DefaultQuery_ReturnsPaginatedResultsSortedByCreatedAtDesc** -- Seed 25 items with staggered `CreatedAt`. Assert default page size (20), `TotalCount` = 25, first item has the most recent `CreatedAt`.

2. **Handle_ReturnsCorrectTotalCountAndPageMetadata** -- Seed 25 items. Query page 1, pageSize 10. Assert `TotalCount` = 25, `TotalPages` = 3, `Items.Count` = 10.

3. **Handle_FiltersByType_WhenSpecified** -- Seed 3 items of different types. Query with `Type = FeedItemType.TrendAlert`. Assert only matching items returned.

4. **Handle_FiltersByPriority_WhenSpecified** -- Seed items with Normal and High priorities. Query with `Priority = FeedItemPriority.High`. Assert only High items returned.

5. **Handle_FiltersByIsReadStatus_WhenSpecified** -- Seed mix of read/unread. Query with `IsRead = false`. Assert only unread returned.

6. **Handle_ExcludesExpiredItemsByDefault** -- Seed items: some with `ExpiresAt` in the past, some with null. Default query should exclude expired.

7. **Handle_IncludesExpiredItems_WhenIncludeExpiredTrue** -- Same seeding as above but query with `IncludeExpired = true`. Assert all items returned including expired.

8. **Handle_EmptyDatabase_ReturnsEmptyPage** -- No seeding. Assert `Items` empty, `TotalCount` = 0.

9. **Handle_RespectsPageSizeLimit** -- Seed 10 items. Query with `PageSize = 5`. Assert exactly 5 items returned.

10. **Handle_AppliesSortDirectionAscending** -- Seed 3 items with known `CreatedAt` values. Query with `SortDirection = "asc"`. Assert ascending order.

### `GetFeedSummaryHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Queries/GetFeedSummaryHandlerTests.cs`

Handler under test: `GetFeedSummary.Handler` (injected with `IAppDbContext`).

Tests:

1. **Handle_ReturnsCorrectUnreadCount** -- Seed 5 unread + 3 read items (all non-expired). Assert `UnreadCount` = 5.

2. **Handle_ReturnsCorrectPendingApprovals** -- Seed AgentDraft (not acted on), ApprovalRequest (not acted on), AgentDraft (acted on), TrendAlert (not acted on). Assert `PendingApprovals` = 2 (only the non-acted AgentDraft and ApprovalRequest).

3. **Handle_ReturnsCorrectTrendingCount** -- Seed 3 unread TrendAlert items, 1 read TrendAlert, 2 unread AgentDraft. Assert `TrendingCount` = 3.

4. **Handle_CalculatesEngagementDelta_FromAnalyticsHighlightItemsInLast24h** -- Seed 2 AnalyticsHighlight items with `CreatedAt` within last 24 hours containing Data JSON like `{"delta": 25.0}` and `{"delta": 15.0}`. Assert `EngagementDelta` = 20.0 (average).

5. **Handle_ReturnsZeroEngagementDelta_WhenNoAnalyticsHighlightItems** -- Seed only non-AnalyticsHighlight items. Assert `EngagementDelta` = 0.

6. **Handle_ReturnsAllZeros_WhenFeedIsEmpty** -- No seeding. Assert all fields are 0.

7. **Handle_ExcludesExpiredItemsFromAllCounts** -- Seed expired items of each type. Assert all counts are 0.

### `GetTrendingTopicsHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Queries/GetTrendingTopicsHandlerTests.cs`

Handler under test: `GetTrendingTopics.Handler` (injected with `IAppDbContext`).

Tests:

1. **Handle_ReturnsTopicsGroupedByTopicField** -- Seed 3 TrendAlert items with Data JSON `{"topic": "Claude Code"}` and 2 with `{"topic": "AI Agents"}`. Assert 2 topics returned with correct counts.

2. **Handle_OrdersByCountDescending** -- Seed topics with varying counts. Assert first result has highest count.

3. **Handle_LimitsToTop10Results** -- Seed 15 distinct topics. Assert only 10 returned.

4. **Handle_OnlyConsidersLast7Days** -- Seed items: some with `CreatedAt` 10 days ago, some within 7 days. Assert only recent items contribute.

5. **Handle_ReturnsEmptyList_WhenNoTrendAlertItems** -- Seed only AgentDraft items. Assert empty list.

6. **Handle_SetsLatestAtToMostRecentCreatedAtPerTopic** -- Seed 3 items for same topic with different `CreatedAt` values. Assert `LatestAt` equals the most recent one.

## Command Handler Tests

### `MarkFeedItemReadHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Commands/MarkFeedItemReadHandlerTests.cs`

Handler under test: `MarkFeedItemRead.Handler`.

Tests:

1. **Handle_MarksItemAsRead** -- Seed unread item. Execute command. Assert `IsRead` = true.

2. **Handle_AlreadyReadItem_ReturnsSuccess** -- Seed read item. Execute command. Assert `IsSuccess` = true (idempotent).

3. **Handle_NonexistentId_ReturnsNotFound** -- Execute command with random Guid. Assert `FailureType` = `ResultFailureType.NotFound`.

### `ActOnFeedItemHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Commands/ActOnFeedItemHandlerTests.cs`

Handler under test: `ActOnFeedItem.Handler`. This handler requires `IAppDbContext` and `ISender` (MediatR) for dispatching sub-commands (`ApproveContent`, `CreateContentFromIdea`).

**Mocking setup:** Create `Mock<ISender>()` and configure `.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), ...)).ReturnsAsync(Result.Success())` etc.

Tests:

1. **Handle_AgentDraftApprove_DispatchesApproveContentAndMarksActedOn** -- Seed AgentDraft item with `ActionTargetId`. Execute with action "approve". Verify `ISender.Send(ApproveContent.Command)` was called. Assert item `IsActedOn` = true.

2. **Handle_AgentDraftDismiss_MarksReadAndActedOn** -- Seed AgentDraft item. Execute with action "dismiss". Assert `IsRead` = true and `IsActedOn` = true.

3. **Handle_TrendAlertView_MarksAsRead** -- Seed TrendAlert item. Execute with action "view". Assert `IsRead` = true, `IsActedOn` unchanged.

4. **Handle_TrendAlertDismiss_MarksReadAndActedOn** -- Assert both flags set.

5. **Handle_IdeaSuggestionCreateContent_DeserializesDataAndDispatchesCommand** -- Seed IdeaSuggestion with Data JSON `{"keywords":["AI"],"confidence":0.85,"sourceIdeaTitle":"Test"}`. Execute with action "create-content". Verify `ISender.Send(CreateContentFromIdea.Command)` called with correct parameters extracted from the item.

6. **Handle_IdeaSuggestionDismiss_MarksReadAndActedOn** -- Assert both flags set.

7. **Handle_AnalyticsHighlightView_MarksAsRead** -- Assert `IsRead` = true.

8. **Handle_ApprovalRequestApprove_DispatchesApproveContentAndMarksActedOn** -- Same pattern as AgentDraft approve.

9. **Handle_SystemNotificationView_MarksAsRead** -- Assert `IsRead` = true.

10. **Handle_UnknownAction_ReturnsValidationFailure** -- Seed any item. Execute with action "invalid-action". Assert `FailureType` = `ResultFailureType.Validation`.

11. **Handle_NonexistentFeedItem_ReturnsNotFound** -- Execute with random Guid. Assert `FailureType` = `ResultFailureType.NotFound`.

12. **Handle_ReturnsNavigationTarget_ForNavigatingActions** -- Seed AgentDraft with `ActionTargetId`. Execute "approve". Assert the result contains a navigation target (route + ID) so the client knows where to navigate.

### `BatchMarkReadHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Commands/BatchMarkReadHandlerTests.cs`

Handler under test: `BatchMarkRead.Handler`.

Tests:

1. **Handle_MarksAllUnreadItemsAsRead_ReturnsCount** -- Seed 5 unread + 3 read items. Execute. Assert returned count = 5. Assert all items now have `IsRead` = true.

2. **Handle_FiltersByType_WhenSpecified** -- Seed unread items of multiple types. Execute with `Type = FeedItemType.TrendAlert`. Assert only TrendAlert items marked read.

3. **Handle_SkipsExpiredItems** -- Seed expired unread items. Execute. Assert count = 0 (expired items untouched).

4. **Handle_Returns0_WhenNoMatchingItems** -- Seed only read items. Execute. Assert count = 0.

### `BatchDismissHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Commands/BatchDismissHandlerTests.cs`

Handler under test: `BatchDismiss.Handler`.

Tests:

1. **Handle_DismissesAllItemsOfSpecifiedType** -- Seed 3 AgentDraft + 2 TrendAlert items. Execute with type AgentDraft. Assert all 3 AgentDraft items have `IsRead` = true and `IsActedOn` = true.

2. **Handle_ReturnsCountOfDismissedItems** -- Same setup. Assert returned count = 3.

3. **Handle_SkipsExpiredItems** -- Seed expired AgentDraft items. Execute. Assert count = 0.

4. **Handle_DoesNotAffectOtherTypes** -- Seed both types. Execute for AgentDraft. Assert TrendAlert items unchanged.

### `BatchActHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Commands/BatchActHandlerTests.cs`

Handler under test: `BatchAct.Handler`. Requires `IAppDbContext` and `ISender`.

Tests:

1. **Handle_ProcessesMultipleItems_ReturnsSuccessCount** -- Seed 3 AgentDraft items. Execute with all 3 IDs and action "dismiss". Assert success count = 3.

2. **Handle_HandlesPartialFailures_Gracefully** -- Seed 2 real items. Include 1 nonexistent ID. Execute. Assert success count = 2, failures list contains the bad ID.

3. **Handle_ReturnsFailureDetails_ForFailedItems** -- Same setup. Assert the failure entry includes the ID and error reason.

### `CreateFeedItemHandlerTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Commands/CreateFeedItemHandlerTests.cs`

Handler under test: `CreateFeedItem.Handler`. Requires `IAppDbContext`, `IHubContext<FeedHub, IFeedHubClient>`, and `ILogger<CreateFeedItem.Handler>`.

**Mocking setup:**
- `Mock<IHubContext<FeedHub, IFeedHubClient>>()` -- set up the `Clients.All` property to return a `Mock<IFeedHubClient>`.
- `Mock<ILogger<CreateFeedItem.Handler>>()` -- for log verification.

Tests:

1. **Handle_CreatesFeedItemWithAllFields_AndSaves** -- Execute with all fields populated. Assert item exists in DB with correct values.

2. **Handle_PushesNewItemViaSignalRHubContext** -- Execute. Verify `hubClient.ReceiveFeedItem()` was called with the new item's DTO.

3. **Handle_ContinuesSuccessfully_EvenIfSignalRPushFails** -- Configure hub mock to throw on `ReceiveFeedItem`. Execute. Assert `IsSuccess` = true and item saved to DB.

4. **Handle_LogsError_WhenSignalRPushFails** -- Configure hub mock to throw. Execute. Verify logger was called with an error-level message.

## Validator Tests

### `ActOnFeedItemRequestValidatorTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Validators/ActOnFeedItemRequestValidatorTests.cs`

Validator under test: `ActOnFeedItemRequestValidator`.

Tests:

1. **Validate_EmptyAction_HasError** -- Validate request with `Action = ""`. Assert error on Action.

2. **Validate_NullAction_HasError** -- Validate request with `Action = null`. Assert error on Action.

3. **Validate_ValidAction_Approve_NoErrors** -- Validate with `Action = "approve"`. Assert no errors.

4. **Validate_ValidAction_Dismiss_NoErrors** -- Validate with `Action = "dismiss"`. Assert no errors.

5. **Validate_ValidAction_View_NoErrors** -- Validate with `Action = "view"`. Assert no errors.

6. **Validate_ValidAction_Edit_NoErrors** -- Validate with `Action = "edit"`. Assert no errors.

7. **Validate_ValidAction_Schedule_NoErrors** -- Validate with `Action = "schedule"`. Assert no errors.

8. **Validate_ValidAction_CreateContent_NoErrors** -- Validate with `Action = "create-content"`. Assert no errors.

### `BatchActRequestValidatorTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Validators/BatchActRequestValidatorTests.cs`

Validator under test: `BatchActRequestValidator`.

Tests:

1. **Validate_EmptyIdsList_HasError** -- Empty `Ids` list. Assert error on Ids.

2. **Validate_NullIdsList_HasError** -- Null `Ids`. Assert error on Ids.

3. **Validate_EmptyAction_HasError** -- Valid IDs but empty action. Assert error on Action.

4. **Validate_ValidRequest_NoErrors** -- IDs with at least one Guid + valid action string. Assert no errors.

### `BatchDismissRequestValidatorTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Validators/BatchDismissRequestValidatorTests.cs`

Validator under test: `BatchDismissRequestValidator`.

Tests:

1. **Validate_ValidFeedItemType_NoErrors** -- Validate with `Type = FeedItemType.AgentDraft`. Assert no errors.

2. **Validate_InvalidEnumValue_HasError** -- Validate with `Type = (FeedItemType)999`. Assert error on Type.

## Mapping Tests

### `FeedMappingsTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/Mappings/FeedMappingsTests.cs`

Tests the `ToDto()` extension method on `FeedItem`.

Tests:

1. **ToDto_MapsAllFieldsCorrectly** -- Create a fully populated `FeedItem` entity. Call `ToDto()`. Assert every field on the resulting `FeedItemDto` matches the entity.

2. **ToDto_HandlesNullOptionalFields** -- Create a `FeedItem` with `Data = null`, `ActionType = null`, `ActionTargetId = null`, `ExpiresAt = null`. Call `ToDto()`. Assert those DTO fields are null without exception.

## Seed Service Tests

### `FeedSeedServiceTests.cs`

**Path:** `tests/PBA.Application.Tests/Features/Feed/FeedSeedServiceTests.cs`

Tests the `FeedSeedService` which creates 30-50 diverse feed items.

Tests:

1. **SeedAsync_CreatesItemsOfAllFeedItemTypeValues** -- Execute seed. Group items by type. Assert every `FeedItemType` enum value is represented.

2. **SeedAsync_CreatesMixOfReadAndUnreadItems** -- Execute seed. Assert at least 1 read and 1 unread item exist.

3. **SeedAsync_CreatesMixOfActedAndNotActedItems** -- Execute seed. Assert at least 1 acted-on and 1 not-acted-on item exist.

4. **SeedAsync_CreatesItemsWithVaryingPriorities** -- Execute seed. Assert at least 2 distinct `FeedItemPriority` values present.

5. **SeedAsync_CreatesSomeExpiredItems** -- Execute seed. Assert at least 1 item has `ExpiresAt` in the past.

6. **SeedAsync_CreatesItemsWithValidDataJsonPerType** -- Execute seed. For each AgentDraft item, assert Data JSON contains "contentType". For each TrendAlert, assert "topic" field. For each AnalyticsHighlight, assert "delta" field. Use `JsonDocument.Parse` for validation.

## Implementation Notes

- All tests use `await using var context = CreateContext();` to ensure the in-memory database is disposed after each test.
- For `ActOnFeedItem` and `BatchAct` tests that need `ISender`, create the mock in each test method or in a constructor-initialized field. Verify calls with `mock.Verify()` after the handler executes.
- For `CreateFeedItem` tests that need `IHubContext<FeedHub, IFeedHubClient>`, the mock chain is: `hubContextMock.Setup(x => x.Clients.All).Returns(hubClientMock.Object)`. Then verify `hubClientMock.Verify(c => c.ReceiveFeedItem(It.IsAny<FeedItemDto>()), Times.Once)`.
- The Data JSON in test seed items should match the canonical schemas defined in section 02.
- Use `System.Text.Json.JsonSerializer` for creating test Data JSON strings.
- The in-memory EF provider does not enforce indexes or constraints, so index tests are not meaningful here.
