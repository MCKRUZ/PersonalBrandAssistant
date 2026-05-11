# Section 05: Core Commands

## Overview

This section implements the core command handlers for Content Studio: **CreateContent**, **UpdateContent**, **DeleteContent**, **DraftContent**, and **GenerateCrossPost**. These are the primary write operations for content lifecycle management.

Status-only commands (ApproveContent, SubmitForReview, RequestChanges, UnpublishContent, RestoreContent) are covered in **section-06-status-commands**. Scheduling commands (ScheduleContent, UnscheduleContent) are covered in **section-10-hangfire-scheduling**. PublishContent is also in section-10 since it requires BlogConnector and Hangfire infrastructure.

## Dependencies

- **section-01-schema-updates**: Content entity must have `HangfireJobId`, `IsDeleted`, and `Children` navigation property.
- **section-02-state-machine**: `ContentStateMachine` helper and `ContentTrigger` enum must exist.
- **section-03-dtos-validators**: All request DTOs and response DTOs must exist.

## Existing Code Context

### MediatR Command Pattern

All commands follow this structure (see `CreateContentFromIdea.cs`, `DismissIdea.cs`):

```csharp
public static class CommandName
{
    public record Command(...) : IRequest<Result<T>> or IRequest<Result>;
    internal sealed class Handler(IAppDbContext db, ...) : IRequestHandler<Command, Result<T>>
    {
        public async Task<Result<T>> Handle(Command request, CancellationToken ct) { ... }
    }
}
```

### ContentStateMachine Usage Pattern (from section-02)

```csharp
var machine = ContentStateMachine.Create(content);
try { await machine.FireAsync(trigger); }
catch (InvalidOperationException) { return Result.Fail("Invalid status transition"); }
```

---

## Tests First

All test files go in `tests/PBA.Application.Tests/Features/Content/Commands/`.

### CreateContentHandlerTests.cs

1. **`Handle_CreatesContentWithProvidedFields`** -- Valid command with Title, ContentType, PrimaryPlatform, Tags. Assert matching entity in DB.
2. **`Handle_DefaultsStatusToIdea`** -- Command without SourceIdeaId. Assert `Status == ContentStatus.Idea`.
3. **`Handle_WhenSourceIdeaIdProvided_CopiesIdeaTitleAndDescription`** -- Idea in DB, command with SourceIdeaId. Assert Content copies from Idea.
4. **`Handle_WhenSourceIdeaIdProvided_SetsIdeaStatusToUsed`** -- Assert `Idea.Status == IdeaStatus.Used`.
5. **`Handle_ReturnsNewContentId`** -- Assert result.Value is valid non-empty Guid.
6. **`Handle_WhenSourceIdeaIdNotFound_ReturnsNotFound`** -- Non-existent SourceIdeaId. Assert NotFound.

### UpdateContentHandlerTests.cs

1. **`Handle_UpdatesOnlyProvidedFields`** -- Title="Updated", Body=null. Assert Title changed, Body unchanged.
2. **`Handle_SetsUpdatedAt`** -- Assert UpdatedAt is later than original.
3. **`Handle_RejectsWhenLastUpdatedAtDoesNotMatch_OptimisticConcurrency`** -- Stale LastUpdatedAt. Assert failure.
4. **`Handle_RejectsWhenStatusIsPublished`** -- Status=Published. Assert failure.
5. **`Handle_RejectsWhenStatusIsArchived`** -- Status=Archived. Assert failure.
6. **`Handle_AllowsSaveWhenStatusIsDraft`** -- Status=Draft. Assert success.
7. **`Handle_AllowsSaveWhenStatusIsIdea`** -- Status=Idea. Assert success.
8. **`Handle_AllowsSaveWhenStatusIsReview`** -- Status=Review. Assert success.
9. **`Handle_ReturnsNotFound_WhenContentDoesNotExist`** -- Assert NotFound.

### DeleteContentHandlerTests.cs

1. **`Handle_TransitionsToArchived`** -- Status=Draft. Assert Status=Archived after delete.
2. **`Handle_CascadesArchiveToUnpublishedChildren`** -- Parent Draft, children: one Draft, one Published. Assert Draft child archived, Published child unchanged.
3. **`Handle_DoesNotCascadeToPublishedChildren`** -- Published children remain Published.
4. **`Handle_ReturnsErrorForInvalidTransition`** -- Already Archived. Assert failure.
5. **`Handle_ReturnsNotFound_WhenContentDoesNotExist`** -- Assert NotFound.

### DraftContentHandlerTests.cs

Requires mocking `ISidecarClient`.

1. **`Handle_CallsSidecarWithDraftPrompt`** -- Action="draft". Assert sidecar called with correct prompts.
2. **`Handle_CallsSidecarWithRefinePrompt`** -- Action="refine". Assert prompt contains "Improve this".
3. **`Handle_CallsSidecarWithShortenPrompt`** -- Action="shorten". Assert prompt contains "Shorten this".
4. **`Handle_CallsSidecarWithExpandPrompt`** -- Action="expand". Assert prompt contains "Expand this".
5. **`Handle_CallsSidecarWithChangeTonePrompt`** -- Action="changeTone", ToneName="professional". Assert prompt contains tone.
6. **`Handle_SystemPromptIncludesBrandProfile`** -- Assert system prompt includes personality, tone, vocabulary.
7. **`Handle_TransitionsFromIdeaToDraft`** -- Status=Idea. Assert Status=Draft after.
8. **`Handle_DoesNotChangeStatusWhenAlreadyDraft`** -- Status=Draft. Assert Status remains Draft.
9. **`Handle_UpdatesBodyWithSidecarResponse`** -- Assert content.Body == sidecar response.
10. **`Handle_ReturnsUpdatedContentDetailDto`** -- Assert result.Value is ContentDetailDto with updated Body.
11. **`Handle_HandlesMissingBrandProfile`** -- No BrandProfile. Assert handler still works.
12. **`Handle_ReturnsNotFound_WhenContentDoesNotExist`** -- Assert NotFound.

### GenerateCrossPostHandlerTests.cs

Requires mocking `ISidecarClient`.

1. **`Handle_CreatesChildContentWithParentContentIdSet`** -- Assert child.ParentContentId == parent.Id.
2. **`Handle_ChildHasTargetPlatformAndDraftStatus`** -- Assert child.PrimaryPlatform and child.Status == Draft.
3. **`Handle_SidecarPromptIncludesPlatformConstraints`** -- Assert prompt includes platform name.
4. **`Handle_ReturnsChildContentId`** -- Assert result.Value matches child entity.
5. **`Handle_ChildBodyIsSidecarResponse`** -- Assert child.Body == sidecar response.
6. **`Handle_ReturnsNotFound_WhenParentDoesNotExist`** -- Assert NotFound.

---

## Implementation Details

### File Structure

All command files go in `src/PBA.Application/Features/Content/Commands/`:

```
CreateContent.cs
UpdateContent.cs
DeleteContent.cs
DraftContent.cs
GenerateCrossPost.cs
```

### CreateContent.cs

**Command:** `Title`, `ContentType`, `PrimaryPlatform`, `SourceIdeaId?`, `Tags` -> Returns `Result<Guid>`

**Logic:**
1. If `SourceIdeaId` provided: load Idea, return NotFound if missing, copy title/description, set `idea.Status = Used`.
2. Create Content with fields from request (overrides applied after copy).
3. Status defaults to `ContentStatus.Idea`.
4. Save and return `Result<Guid>.Success(content.Id)`.

### UpdateContent.cs

**Command:** `ContentId`, `Title?`, `Body?`, `Tags?`, `ContentType?`, `PrimaryPlatform?`, `LastUpdatedAt` -> Returns `Result`

**Logic:**
1. Load content. Return NotFound if missing.
2. **State guard:** Only allow edits when Status is Idea, Draft, or Review.
3. **Optimistic concurrency:** Compare `request.LastUpdatedAt` with `content.UpdatedAt`. Reject if mismatch.
4. Apply non-null fields only.
5. Set `content.UpdatedAt = DateTimeOffset.UtcNow`, save.

### DeleteContent.cs

**Command:** `ContentId` -> Returns `Result`

**Logic:**
1. Load content. Return NotFound if missing.
2. Fire `ContentTrigger.Archive` via state machine. Catch InvalidOperationException.
3. Set `content.IsDeleted = true`.
4. Load children where `Status != Published`. Archive each (best effort).
5. Save.

### DraftContent.cs

**Command:** `ContentId`, `Action`, `Instructions?`, `ToneName?` -> Returns `Result<ContentDetailDto>`

**Handler dependencies:** `IAppDbContext db`, `ISidecarClient sidecar`

**Logic:**
1. Load content. Return NotFound if missing.
2. Load BrandProfile (use empty defaults if none).
3. Build system prompt with BrandProfile details + humanizer rules.
4. Build user prompt based on Action type:
   - "draft": `"Generate a {contentType} for {platform}. Topic: {title}. Context: {body}"`
   - "refine": `"Improve this {contentType}: {body}"`
   - "shorten": `"Shorten this to fit {platform} constraints: {body}"`
   - "expand": `"Expand this {contentType} with more detail: {body}"`
   - "changeTone": `"Rewrite in a {toneName} tone: {body}"`
5. Call `sidecar.SendPromptAsync`.
6. If Status is Idea, fire `ContentTrigger.StartDraft`.
7. Set `content.Body = response`, update timestamps, save.
8. Map to ContentDetailDto and return.

### GenerateCrossPost.cs

**Command:** `ContentId`, `TargetPlatform` -> Returns `Result<Guid>`

**Handler dependencies:** `IAppDbContext db`, `ISidecarClient sidecar`

**Logic:**
1. Load parent content. Return NotFound if missing.
2. Load BrandProfile.
3. Build prompt with platform constraints for target platform.
4. Call sidecar.
5. Create child Content with ParentContentId, TargetPlatform, Status=Draft, Body=response.
6. Save and return child ID.

### Shared Utilities

**PlatformConstraints.cs** (`src/PBA.Application/Features/Content/PlatformConstraints.cs`): Static helper returning character limits per platform. Used by DraftContent and GenerateCrossPost.

**ContentMappings.cs** (`src/PBA.Application/Features/Content/Mappings/ContentMappings.cs`): Static extension methods for Content -> ContentDetailDto mapping. Shared with GetContent query handler (section-04).

---

## Key Design Decisions

1. **CreateContent reimplements Idea-to-Content copy inline** rather than calling CreateContentFromIdea. Minimal duplication, avoids cross-feature coupling.

2. **UpdateContent uses status allowlist** (Idea, Draft, Review). New statuses blocked by default.

3. **DeleteContent sets IsDeleted=true explicitly** in addition to firing Archive trigger. Enables separate "Archived" view vs truly deleted content.

4. **DraftContent returns ContentDetailDto** so UI gets updated content immediately without extra GET.

5. **Optimistic concurrency uses DateTimeOffset comparison** with 500ms tolerance for serialization precision differences.

---

## Implementation Notes (post-review)

### Code Review Changes

- **ContentStateMachine updated**: Added `Permit(Archive, Archived)` to Idea state, allowing Ideas to be deleted/archived directly.
- **Sidecar error handling**: Both DraftContent and GenerateCrossPost now wrap sidecar calls in try/catch, returning `Result.Fail` on exception. Empty responses are also rejected.
- **Instructions parameter wired up**: DraftContent.BuildUserPrompt now appends `request.Instructions` when provided.
- **Same-platform guard**: GenerateCrossPost rejects when TargetPlatform == parent.PrimaryPlatform.
- **Duplicate cross-post guard**: GenerateCrossPost checks for existing non-deleted child with same ParentContentId + PrimaryPlatform.
- **Prompt injection mitigation**: User content wrapped in `<content>` XML tags in sidecar prompts.
- **Unknown action handling**: Default switch branch now throws `ArgumentOutOfRangeException` instead of passing raw action to LLM.

### Actual Files Created/Modified

**Created:**
- `src/PBA.Application/Features/Content/Commands/CreateContent.cs`
- `src/PBA.Application/Features/Content/Commands/UpdateContent.cs`
- `src/PBA.Application/Features/Content/Commands/DeleteContent.cs`
- `src/PBA.Application/Features/Content/Commands/DraftContent.cs`
- `src/PBA.Application/Features/Content/Commands/GenerateCrossPost.cs`
- `src/PBA.Application/Features/Content/PlatformConstraints.cs`
- `src/PBA.Application/Features/Content/Mappings/ContentMappings.cs`
- `tests/PBA.Application.Tests/Features/Content/Commands/CreateContentHandlerTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Commands/UpdateContentHandlerTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Commands/DeleteContentHandlerTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Commands/DraftContentHandlerTests.cs`
- `tests/PBA.Application.Tests/Features/Content/Commands/GenerateCrossPostHandlerTests.cs`

**Modified:**
- `src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs` (added Idea→Archive)

### Test Count: 41 (6 + 9 + 6 + 12 + 8)
