# Section 04: Query Handlers

## Overview

This section implements three MediatR query handlers for the Content Studio: `ListContent`, `GetContent`, and `CheckVoice`. These are read-only operations that retrieve content data for the list page, editor page, and voice scoring feature respectively.

## Dependencies

- **Section 01 (Schema Updates):** `IAppDbContext` must expose `ContentPlatformPublishes` and `BrandProfiles` DbSets. Content entity must have `HangfireJobId`, `IsDeleted`, and `Children` navigation property. Soft-delete query filter must be applied.
- **Section 03 (DTOs and Validators):** All response DTOs (`ContentDto`, `ContentDetailDto`, `PlatformPublishDto`, `ChildContentDto`, `VoiceCheckDto`) must exist.

## Files to Create

| File | Purpose |
|------|---------|
| `src/PBA.Application/Features/Content/Queries/ListContent.cs` | Paginated, filtered content listing |
| `src/PBA.Application/Features/Content/Queries/GetContent.cs` | Single content with related data |
| `src/PBA.Application/Features/Content/Queries/CheckVoice.cs` | AI voice scoring via sidecar |
| `tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs` | Unit tests for ListContent |
| `tests/PBA.Application.Tests/Features/Content/Queries/GetContentHandlerTests.cs` | Unit tests for GetContent |
| `tests/PBA.Application.Tests/Features/Content/Queries/CheckVoiceHandlerTests.cs` | Unit tests for CheckVoice |

## Existing Patterns to Follow

All query handlers follow the same pattern established in the Idea Bank (Step 2). Each is a static class wrapping a `record Query` (implementing `IRequest<Result<T>>`) and a sealed `Handler` class with primary constructor DI. Tests use `ApplicationDbContext` with EF Core InMemoryDatabase, direct handler instantiation, and xUnit `[Fact]` methods.

Key references:
- `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs` -- paginated list query pattern
- `src/PBA.Application/Features/Ideas/Queries/GetIdea.cs` -- single-entity detail query pattern

## Tests First

### ListContentHandlerTests

File: `tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs`

1. **`Handle_DefaultQuery_ReturnsPaginatedResults`** -- Seed 25 content items. Default query (page 1, pageSize 20). Assert 20 items returned, totalCount 25, totalPages 2.

2. **`Handle_StatusFilter_ReturnsMatchingOnly`** -- Seed content with different `ContentStatus` values. Filter by `Draft`. Assert only Draft items returned.

3. **`Handle_PlatformFilter_ReturnsMatchingOnly`** -- Seed content with different `Platform` values. Filter by `Platform.Blog`. Assert only Blog items returned.

4. **`Handle_ContentTypeFilter_ReturnsMatchingOnly`** -- Seed content with different `ContentType` values. Filter by `ContentType.BlogPost`. Assert only BlogPost items returned.

5. **`Handle_DateRangeFilter_ReturnsWithinRange`** -- Seed content with various `UpdatedAt` values. Set `DateFrom` and `DateTo`. Assert only in-range items returned.

6. **`Handle_SearchText_MatchesTitleCaseInsensitive`** -- Seed content with titles containing "Claude", "claude", "other". Search for "claude". Assert both Claude-titled items returned.

7. **`Handle_ExcludesChildContent`** -- Seed one parent and one child content (ParentContentId set). Assert only parent returned.

8. **`Handle_ExcludesSoftDeletedContent`** -- Seed two items, one with `IsDeleted = true`. Assert only non-deleted item returned.

9. **`Handle_OrdersByUpdatedAtDescending`** -- Seed three items with different `UpdatedAt`. Assert first result has most recent `UpdatedAt`.

### GetContentHandlerTests

File: `tests/PBA.Application.Tests/Features/Content/Queries/GetContentHandlerTests.cs`

1. **`Handle_ExistingContent_ReturnsDetailWithPlatformPublishes`** -- Seed content with two `ContentPlatformPublish` records. Assert both appear in `PlatformPublishes`.

2. **`Handle_ExistingContent_ReturnsDetailWithChildren`** -- Seed parent with two child content items. Assert both appear in `Children`.

3. **`Handle_NonExistentId_ReturnsNotFound`** -- Query random Guid. Assert NotFound result.

4. **`Handle_ExcludesSoftDeletedChildren`** -- Seed parent with two children, one with `IsDeleted = true`. Assert only non-deleted child in `Children`.

### CheckVoiceHandlerTests

File: `tests/PBA.Application.Tests/Features/Content/Queries/CheckVoiceHandlerTests.cs`

This handler depends on `ISidecarClient` -- uses Moq.

1. **`Handle_ValidContent_ReturnsScoreAndFeedback`** -- Mock sidecar returns `{"score": 85, "feedback": "Good match"}`. Assert `Score == 85` and `Feedback == "Good match"`.

2. **`Handle_ValidContent_UpdatesVoiceScoreOnEntity`** -- After handler returns, reload content and assert `VoiceScore == 85`.

3. **`Handle_NonExistentContent_ReturnsNotFound`** -- Assert NotFound result.

4. **`Handle_MissingBrandProfile_UsesDefaults`** -- No BrandProfile in DB. Assert handler succeeds (uses empty defaults in prompt).

5. **`Handle_SidecarPrompt_ContainsStructuredJsonInstruction`** -- Capture userPrompt via Moq callback. Assert prompt contains `Respond ONLY with JSON`.

---

## Implementation Details

### ListContent

File: `src/PBA.Application/Features/Content/Queries/ListContent.cs`

**Query record:**
```csharp
public record Query : IRequest<Result<PagedResult<ContentDto>>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public ContentStatus? Status { get; init; }
    public Platform? Platform { get; init; }
    public ContentType? ContentType { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? Search { get; init; }
}
```

**Handler logic:**
- Primary constructor injection: `IAppDbContext db`
- Start with `db.Contents.AsNoTracking().AsQueryable()`
- Always exclude children: `Where(c => c.ParentContentId == null)`
- Filter chain (each applied only when value is non-null): status, platform, contentType, dateFrom, dateTo, search (case-insensitive title contains)
- Soft-delete filtering handled by EF query filter
- Count total, order by `UpdatedAt` descending, paginate with Skip/Take
- Project to `ContentDto` via inline `.Select()`
- Return `PagedResult<ContentDto>` wrapped in `Result<T>.Success()`

### GetContent

File: `src/PBA.Application/Features/Content/Queries/GetContent.cs`

**Query record:**
```csharp
public record Query(Guid ContentId) : IRequest<Result<ContentDetailDto>>;
```

**Handler logic:**
- Load content with `.Include(c => c.CrossPosts)`. Return NotFound if null.
- Separate query for children: `db.Contents.AsNoTracking().Where(c => c.ParentContentId == request.ContentId && !c.IsDeleted)`
- Map to `ContentDetailDto` with:
  - `PlatformPublishes` from `content.CrossPosts` -> `PlatformPublishDto`
  - `Children` from children query -> `ChildContentDto`

Important: `content.CrossPosts` = `IReadOnlyList<ContentPlatformPublish>` (publish records). Children = separate `Content` entities via `ParentContentId` (platform adaptations). Two distinct concepts.

### CheckVoice

File: `src/PBA.Application/Features/Content/Queries/CheckVoice.cs`

**Query record:**
```csharp
public record Query(Guid ContentId) : IRequest<Result<VoiceCheckDto>>;
```

**Handler logic:**
- Primary constructor injection: `IAppDbContext db, ISidecarClient sidecar`
- Load content (tracking needed since we update `VoiceScore`). Return NotFound if missing.
- Load brand profile: `db.BrandProfiles.FirstOrDefaultAsync(ct)`. Use empty defaults if none.
- Build system prompt with BrandProfile fields (personality, tone, vocabulary, avoid words)
- Build user prompt: include content body + structured output instruction: `"Respond ONLY with JSON: {\"score\": <0-100>, \"feedback\": \"<explanation>\"}"`
- Call `sidecar.SendPromptAsync(systemPrompt, userPrompt, ct)`
- Parse JSON response into score + feedback
- Update `content.VoiceScore`, save changes
- Return `Result<VoiceCheckDto>.Success(new VoiceCheckDto { Score = score, Feedback = feedback })`

Note: Despite being called a "query," CheckVoice has a side effect (updating VoiceScore). This is an intentional pragmatic choice -- caching the score avoids re-running the sidecar for the same content.

---

## DTO Reference

These DTOs are defined in Section 03:

**ContentDto**: `Id`, `Title`, `ContentType`, `Status`, `PrimaryPlatform`, `VoiceScore`, `Tags`, `CreatedAt`, `UpdatedAt`, `ScheduledAt`, `PublishedAt`

**ContentDetailDto**: All ContentDto fields plus `Body`, `ViralityPrediction`, `SourceIdeaId`, `ParentContentId`, `PlatformPublishes` (IReadOnlyList<PlatformPublishDto>), `Children` (IReadOnlyList<ChildContentDto>)

**PlatformPublishDto**: `Id`, `Platform`, `PublishStatus`, `PublishedUrl`, `PublishedAt`

**ChildContentDto**: `Id`, `Title`, `ContentType`, `PrimaryPlatform`, `Status`, `UpdatedAt`

**VoiceCheckDto**: `Score` (decimal, 0-100), `Feedback` (string)

---

## Testing Infrastructure

All tests use the same pattern as the existing Idea Bank tests:

```csharp
private static ApplicationDbContext CreateContext()
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    return new ApplicationDbContext(options);
}
```

For `CheckVoiceHandlerTests`, additionally mock `ISidecarClient`:
```csharp
private readonly Mock<ISidecarClient> _sidecarMock = new();
```

## Blockers

- Section 01 must be complete (Content entity changes, IAppDbContext updates, soft-delete query filter)
- Section 03 must be complete (all DTO definitions)
- No dependency on Sections 02, 05-16

---

## Implementation Notes (Post-Build)

### Deviations from Plan

1. **GetContent children query**: Plan specified `!c.IsDeleted` explicit filter. Removed during code review since EF `HasQueryFilter` on Content entity handles soft-delete globally. Test `Handle_ExcludesSoftDeletedChildren` confirms behavior via InMemoryDatabase.

2. **CheckVoice JSON parsing**: Plan did not specify error handling for sidecar response. Added `TryParseVoiceResponse` method with `try/catch` around `JsonDocument.Parse`, `using var doc` for proper disposal, and score range validation (0-100). Returns `Result.Fail` on invalid JSON or out-of-range scores.

3. **Raw string literal syntax**: Used `$$"""` (double-dollar) raw string literals in `BuildUserPrompt` to allow literal `{}` braces in the JSON instruction while using `{{}}` for interpolation.

4. **Property mapping**: `ContentPlatformPublish.Status` (entity) maps to `PlatformPublishDto.PublishStatus` (DTO) — naming differs between layers.

### Additional Tests (Beyond Plan)

- `Handle_SidecarReturnsInvalidJson_ReturnsFailure` — verifies graceful handling of malformed sidecar output
- `Handle_ScoreOutOfRange_ReturnsFailure` — verifies score=150 is rejected

### Final Test Count

- ListContentHandlerTests: 9 tests
- GetContentHandlerTests: 4 tests
- CheckVoiceHandlerTests: 7 tests (5 planned + 2 added)
- **Total: 20 tests, all passing**
