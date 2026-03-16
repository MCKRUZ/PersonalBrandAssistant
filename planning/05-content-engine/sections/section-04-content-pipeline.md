I now have all the context needed. Here is the section content:

# Section 04 — Content Pipeline

## Overview

This section implements the `IContentPipeline` interface and its `ContentPipeline` service, the `ContentCreationRequest` model, and the MediatR commands/queries that drive the content creation lifecycle: CreateFromTopic, GenerateOutline, GenerateDraft, ValidateVoice, and SubmitForReview. It also covers blog writing in full agent mode via the sidecar.

## Dependencies

- **section-01-domain-entities** -- The `Content` entity (already exists at `src/PersonalBrandAssistant.Domain/Entities/Content.cs`) and its `ContentMetadata` value object (already exists at `src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs`). The `Content.Create` factory method and `TransitionTo` state machine are used directly.
- **section-02-sidecar-integration** -- `ISidecarClient` and `SidecarEvent` types must exist. The pipeline calls `ISidecarClient.SendTaskAsync` to generate outlines and drafts.
- **section-03-agent-refactoring** -- The refactored `AgentCapabilityBase` and `IAgentOrchestrator` that use `ISidecarClient` instead of `IChatClient`. The pipeline may delegate to `IAgentOrchestrator.ExecuteAsync` or call `ISidecarClient` directly depending on the task.
- **section-07-brand-voice** (soft dependency) -- `IBrandVoiceService.ScoreContentAsync` is called by `ValidateVoiceAsync`. If implementing this section before section-07, mock `IBrandVoiceService` and return a placeholder score.

## Existing Codebase Context

The project uses the `Result<T>` pattern for all operation returns. Key types:

- `Result<T>.Success(value)`, `Result<T>.NotFound(message)`, `Result<T>.Failure(errorCode, errors)`, `Result<T>.Conflict(message)`, `Result<T>.ValidationFailure(errors)`
- `ErrorCode` enum: `None`, `ValidationFailed`, `NotFound`, `Conflict`, `Unauthorized`, `InternalError`
- `IApplicationDbContext` exposes `DbSet<Content> Contents` among other sets
- `IWorkflowEngine.TransitionAsync(contentId, targetStatus, reason, actor, ct)` handles status transitions
- `IWorkflowEngine.ShouldAutoApproveAsync(contentId, ct)` checks autonomy-driven auto-approval
- `Content.Create(type, body, title, targetPlatforms, capturedAutonomyLevel)` is the factory method
- `ContentMetadata.AiGenerationContext` stores AI-generated intermediate data (outlines, prompts)
- `ContentMetadata.PlatformSpecificData` is a `Dictionary<string, string>` for platform-specific fields
- `AutonomyLevel` enum: `Manual`, `Assisted`, `SemiAuto`, `Autonomous`
- Existing MediatR command pattern: command record implements `IRequest<Result<T>>`, separate handler class, separate FluentValidation validator class
- Test conventions: xUnit + Moq + MockQueryable, `Mock<DbSet<T>>` built via `BuildMockDbSet()`, AAA pattern, method naming `Handle_{Scenario}_{Expected}`

## Files to Create

### Application Layer

| File | Path |
|------|------|
| IContentPipeline | `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs` |
| ContentCreationRequest | `src/PersonalBrandAssistant.Application/Common/Models/ContentCreationRequest.cs` |
| BrandVoiceScore | `src/PersonalBrandAssistant.Application/Common/Models/BrandVoiceScore.cs` |
| CreateFromTopicCommand | `src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommand.cs` |
| CreateFromTopicCommandHandler | `src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandHandler.cs` |
| CreateFromTopicCommandValidator | `src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandValidator.cs` |
| GenerateOutlineCommand | `src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommand.cs` |
| GenerateOutlineCommandHandler | `src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommandHandler.cs` |
| GenerateDraftCommand | `src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommand.cs` |
| GenerateDraftCommandHandler | `src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommandHandler.cs` |
| ValidateVoiceCommand | `src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommand.cs` |
| ValidateVoiceCommandHandler | `src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommandHandler.cs` |
| SubmitForReviewCommand | `src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommand.cs` |
| SubmitForReviewCommandHandler | `src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommandHandler.cs` |
| GetContentTreeQuery | `src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContentTree/GetContentTreeQuery.cs` |
| GetContentTreeQueryHandler | `src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContentTree/GetContentTreeQueryHandler.cs` |

### Infrastructure Layer

| File | Path |
|------|------|
| ContentPipeline | `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs` |

### Test Files

| File | Path |
|------|------|
| ContentPipelineTests | `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs` |
| CreateFromTopicCommandHandlerTests | `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateFromTopicCommandHandlerTests.cs` |
| GenerateOutlineCommandHandlerTests | `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateOutlineCommandHandlerTests.cs` |
| GenerateDraftCommandHandlerTests | `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateDraftCommandHandlerTests.cs` |
| SubmitForReviewCommandHandlerTests | `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/SubmitForReviewCommandHandlerTests.cs` |

---

## Tests (Write First)

### ContentPipelineTests

Test file: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs`

This is the primary test class for the `ContentPipeline` service implementation. All dependencies are mocked.

**Test class setup:**
- `Mock<IApplicationDbContext>` with mock `DbSet<Content>` via `BuildMockDbSet()`
- `Mock<ISidecarClient>` -- returns predefined `IAsyncEnumerable<SidecarEvent>` streams
- `Mock<IBrandVoiceService>` -- returns predefined `BrandVoiceScore`
- `Mock<IWorkflowEngine>` -- for transition and auto-approve checks
- `Mock<ILogger<ContentPipeline>>`

**Test cases for `CreateFromTopicAsync`:**

1. `CreateFromTopicAsync_ValidRequest_CreatesContentInDraftStatus` -- Given a valid `ContentCreationRequest` with type `BlogPost` and topic "AI in branding", assert that a `Content` entity is added to `DbSet<Content>` with `Status == Draft`, the topic is stored in `ContentMetadata.AiGenerationContext`, and the result is `IsSuccess == true` with the new content's `Guid`.

2. `CreateFromTopicAsync_EmptyTopic_ReturnsValidationFailure` -- Given a request with an empty string topic, assert `result.IsSuccess == false` and `result.ErrorCode == ErrorCode.ValidationFailed`.

3. `CreateFromTopicAsync_WithParentContentId_SetsParentOnContent` -- Given a request with a `ParentContentId`, verify the created `Content` entity has `ParentContentId` set to the provided value.

4. `CreateFromTopicAsync_WithTargetPlatforms_SetsTargetPlatformsOnContent` -- Given a request with `TargetPlatforms = [TwitterX, LinkedIn]`, verify the created content has matching target platforms.

**Test cases for `GenerateOutlineAsync`:**

5. `GenerateOutlineAsync_ValidContentId_SendsOutlineTaskToSidecar` -- Given an existing content entity in Draft status, verify `ISidecarClient.SendTaskAsync` is called with a prompt containing the topic and "outline".

6. `GenerateOutlineAsync_ValidContentId_StoresOutlineInMetadata` -- Mock the sidecar to return `ChatEvent` events with outline text. Assert that `Content.Metadata.AiGenerationContext` is updated with the concatenated outline text and `SaveChangesAsync` is called.

7. `GenerateOutlineAsync_ContentNotFound_ReturnsNotFound` -- Given a non-existent content ID, assert `result.ErrorCode == ErrorCode.NotFound`.

8. `GenerateOutlineAsync_ReturnsOutlineText` -- Assert the `Result<string>.Value` contains the generated outline text from the sidecar events.

**Test cases for `GenerateDraftAsync`:**

9. `GenerateDraftAsync_SocialPost_UpdatesContentBody` -- Given content of type `SocialPost`, mock sidecar to return `ChatEvent` events with draft text. Assert `Content.Body` is updated with the generated text.

10. `GenerateDraftAsync_BlogPost_CapturesFilePathAndCommitHash` -- Given content of type `BlogPost`, mock sidecar to return `FileChangeEvent` and `ChatEvent` events. Assert `ContentMetadata.PlatformSpecificData` contains keys `"filePath"` and `"commitHash"` extracted from the events.

11. `GenerateDraftAsync_BlogPost_SendsPromptWithBrandVoiceContext` -- Verify the sidecar task prompt includes brand voice profile data, SEO keywords from metadata, and the outline from `AiGenerationContext`.

12. `GenerateDraftAsync_ContentNotFound_ReturnsNotFound` -- Given a non-existent content ID, assert `result.ErrorCode == ErrorCode.NotFound`.

**Test cases for `ValidateVoiceAsync`:**

13. `ValidateVoiceAsync_DelegatesToBrandVoiceService` -- Verify that `IBrandVoiceService.ScoreContentAsync(contentId, ct)` is called exactly once.

14. `ValidateVoiceAsync_ReturnsBrandVoiceScore` -- Mock `IBrandVoiceService` to return a `BrandVoiceScore` with `OverallScore = 85`. Assert the result value matches.

15. `ValidateVoiceAsync_ContentNotFound_ReturnsNotFound` -- Given a non-existent content ID, assert not found.

**Test cases for `SubmitForReviewAsync`:**

16. `SubmitForReviewAsync_TransitionsContentToReview` -- Verify `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Review, ...)` is called.

17. `SubmitForReviewAsync_AutonomousLevel_AutoApproves` -- Mock `IWorkflowEngine.ShouldAutoApproveAsync` to return `true`. Verify that a second `TransitionAsync` call is made with `ContentStatus.Approved`.

18. `SubmitForReviewAsync_ContentAlreadySubmitted_ReturnsConflict` -- Given content already in `Review` status, mock the workflow engine transition to fail. Assert `result.ErrorCode == ErrorCode.Conflict`.

### MediatR Command Handler Tests

**CreateFromTopicCommandHandlerTests** at `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateFromTopicCommandHandlerTests.cs`:

- `Handle_ValidCommand_DelegatesToContentPipeline` -- Verify `IContentPipeline.CreateFromTopicAsync` is called with a matching `ContentCreationRequest`.
- `Handle_PipelineReturnsFailure_ReturnsFailure` -- Mock pipeline to return validation failure. Assert handler propagates it.

**GenerateOutlineCommandHandlerTests** at `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateOutlineCommandHandlerTests.cs`:

- `Handle_ValidContentId_DelegatesToContentPipeline` -- Verify `IContentPipeline.GenerateOutlineAsync` is called.
- `Handle_ContentNotFound_ReturnsNotFound` -- Mock pipeline to return not found. Assert propagation.

**GenerateDraftCommandHandlerTests** at `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateDraftCommandHandlerTests.cs`:

- `Handle_ValidContentId_DelegatesToContentPipeline` -- Verify `IContentPipeline.GenerateDraftAsync` is called.
- `Handle_ContentNotFound_ReturnsNotFound` -- Mock pipeline to return not found. Assert propagation.

**SubmitForReviewCommandHandlerTests** at `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/SubmitForReviewCommandHandlerTests.cs`:

- `Handle_ValidContentId_DelegatesToContentPipeline` -- Verify `IContentPipeline.SubmitForReviewAsync` is called.
- `Handle_ContentAlreadySubmitted_ReturnsConflict` -- Mock pipeline to return conflict. Assert propagation.

---

## Implementation Details

### IContentPipeline Interface

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs`

```csharp
public interface IContentPipeline
{
    Task<Result<Guid>> CreateFromTopicAsync(ContentCreationRequest request, CancellationToken ct);
    Task<Result<string>> GenerateOutlineAsync(Guid contentId, CancellationToken ct);
    Task<Result<string>> GenerateDraftAsync(Guid contentId, CancellationToken ct);
    Task<Result<BrandVoiceScore>> ValidateVoiceAsync(Guid contentId, CancellationToken ct);
    Task<Result<Unit>> SubmitForReviewAsync(Guid contentId, CancellationToken ct);
}
```

### ContentCreationRequest Model

File: `src/PersonalBrandAssistant.Application/Common/Models/ContentCreationRequest.cs`

```csharp
public record ContentCreationRequest(
    ContentType Type,
    string Topic,
    string? Outline,
    PlatformType[]? TargetPlatforms,
    Guid? ParentContentId,
    Dictionary<string, string>? Parameters);
```

### BrandVoiceScore Model

File: `src/PersonalBrandAssistant.Application/Common/Models/BrandVoiceScore.cs`

```csharp
public record BrandVoiceScore(
    int OverallScore,
    int ToneAlignment,
    int VocabularyConsistency,
    int PersonaFidelity,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> RuleViolations);
```

This record is shared with section-07 (Brand Voice). If section-07 is implemented first, this file will already exist.

### ContentPipeline Service

File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs`

Constructor dependencies:
- `IApplicationDbContext _dbContext`
- `ISidecarClient _sidecarClient`
- `IBrandVoiceService _brandVoiceService`
- `IWorkflowEngine _workflowEngine`
- `ILogger<ContentPipeline> _logger`

**CreateFromTopicAsync implementation:**

1. Validate `request.Topic` is not empty/whitespace. Return `ValidationFailure` if invalid.
2. Create a `Content` entity via `Content.Create(request.Type, body: string.Empty, title: null, request.TargetPlatforms)`.
3. Set `content.ParentContentId = request.ParentContentId` if provided.
4. Store the topic in `content.Metadata.AiGenerationContext` as a JSON object: `{"topic": "...", "outline": null}`.
5. If `request.Parameters` is not null, merge into `content.Metadata.PlatformSpecificData`.
6. Add to `_dbContext.Contents`, call `SaveChangesAsync`.
7. Return `Result<Guid>.Success(content.Id)`.

**GenerateOutlineAsync implementation:**

1. Load content by ID from `_dbContext.Contents`. Return `NotFound` if missing.
2. Extract the topic from `Content.Metadata.AiGenerationContext`.
3. Build a prompt string: include the topic, content type, and any outline hints from the creation request.
4. Call `_sidecarClient.SendTaskAsync(prompt, sessionId: null, ct)`.
5. Iterate the `IAsyncEnumerable<SidecarEvent>`, collecting text from `ChatEvent` events where `EventType` is text/content.
6. Concatenate collected text into the outline string.
7. Update `Content.Metadata.AiGenerationContext` to include the generated outline (update the JSON to set the `"outline"` field).
8. Call `SaveChangesAsync`.
9. Return `Result<string>.Success(outlineText)`.

**GenerateDraftAsync implementation:**

1. Load content by ID. Return `NotFound` if missing.
2. Extract topic and outline from `Content.Metadata.AiGenerationContext`.
3. Build the draft generation prompt. For all content types, include: topic, outline, brand voice context (load `BrandProfile` from DB), SEO keywords from `Content.Metadata.SeoKeywords`.
4. For `ContentType.BlogPost`: add blog-specific instructions (HTML structure, matthewkruczek.ai patterns, commit instructions). The sidecar will write files and commit to git.
5. Call `_sidecarClient.SendTaskAsync(prompt, sessionId: null, ct)`.
6. Iterate events:
   - Collect text from `ChatEvent` into the draft body.
   - For `FileChangeEvent`: store `FilePath` in `PlatformSpecificData["filePath"]`.
   - For `SessionUpdateEvent`: store session ID for potential resumption.
   - For `TaskCompleteEvent`: record token usage in `Content.Metadata.TokensUsed` and `EstimatedCost`.
7. Update `Content.Body` with the generated draft text.
8. For blog posts, also extract commit hash from sidecar events or `PlatformSpecificData`.
9. Call `SaveChangesAsync`.
10. Return `Result<string>.Success(draftBody)`.

**ValidateVoiceAsync implementation:**

1. Load content by ID. Return `NotFound` if missing.
2. Delegate to `_brandVoiceService.ScoreContentAsync(contentId, ct)`.
3. If the brand voice service returns a failure, propagate it.
4. Store the score in `Content.Metadata.PlatformSpecificData["brandVoiceScore"]` as serialized JSON.
5. Call `SaveChangesAsync`.
6. Return the `BrandVoiceScore`.

**SubmitForReviewAsync implementation:**

1. Load content by ID. Return `NotFound` if missing.
2. Call `_workflowEngine.TransitionAsync(contentId, ContentStatus.Review, reason: "Submitted via content pipeline", ActorType.System, ct)`.
3. If transition fails (e.g., content already in Review), return the error (likely `Conflict`).
4. Check `_workflowEngine.ShouldAutoApproveAsync(contentId, ct)`. If true, call `_workflowEngine.TransitionAsync(contentId, ContentStatus.Approved, reason: "Auto-approved by autonomy policy", ActorType.System, ct)`.
5. Return `Result<Unit>.Success(Unit.Value)`.

### MediatR Commands

Each command follows the established pattern in the codebase.

**CreateFromTopicCommand:**

```csharp
public sealed record CreateFromTopicCommand(
    ContentType ContentType,
    string Topic,
    string? Outline = null,
    PlatformType[]? TargetPlatforms = null,
    Guid? ParentContentId = null,
    Dictionary<string, string>? Parameters = null) : IRequest<Result<Guid>>;
```

The handler constructs a `ContentCreationRequest` from the command and delegates to `IContentPipeline.CreateFromTopicAsync`.

**CreateFromTopicCommandValidator:** Validate that `Topic` is not empty and does not exceed 500 characters. Validate that `ContentType` is a defined enum value.

**GenerateOutlineCommand:**

```csharp
public sealed record GenerateOutlineCommand(Guid ContentId) : IRequest<Result<string>>;
```

Handler delegates to `IContentPipeline.GenerateOutlineAsync`.

**GenerateDraftCommand:**

```csharp
public sealed record GenerateDraftCommand(Guid ContentId) : IRequest<Result<string>>;
```

Handler delegates to `IContentPipeline.GenerateDraftAsync`.

**ValidateVoiceCommand:**

```csharp
public sealed record ValidateVoiceCommand(Guid ContentId) : IRequest<Result<BrandVoiceScore>>;
```

Handler delegates to `IContentPipeline.ValidateVoiceAsync`.

**SubmitForReviewCommand:**

```csharp
public sealed record SubmitForReviewCommand(Guid ContentId) : IRequest<Result<Unit>>;
```

Handler delegates to `IContentPipeline.SubmitForReviewAsync`.

### Blog Writing -- Full Agent Mode

For `ContentType.BlogPost`, the sidecar operates in full agent mode. Key details for the draft generation prompt:

- The sidecar task prompt instructs Claude Code to write HTML files matching the matthewkruczek.ai blog structure.
- The sidecar's Claude Code session has access to the blog repo directory (mounted via Docker volume in the sidecar container).
- Claude Code writes HTML files and commits to the blog git repo.
- The content pipeline captures the file path from `FileChangeEvent` and the commit hash from `TaskCompleteEvent` or session metadata.

**Source of truth:** The database is authoritative. The sidecar writes files and commits, then the API persists body + metadata (commit hash, file path, slug) to the `Content` entity. If the DB save fails, the commit is orphaned but content is not considered "published."

The `Content.Body` stores the generated HTML. `ContentMetadata.PlatformSpecificData` stores blog-specific data:
- `"filePath"` -- path to the generated HTML file
- `"commitHash"` -- git commit hash from the sidecar
- `"slug"` -- URL slug derived from the title

### Sidecar Event Stream Consumption Pattern

The pipeline consumes `IAsyncEnumerable<SidecarEvent>` from the sidecar client. The general pattern used in both `GenerateOutlineAsync` and `GenerateDraftAsync`:

```csharp
var textBuilder = new StringBuilder();
string? sessionId = null;
int inputTokens = 0, outputTokens = 0;

await foreach (var evt in _sidecarClient.SendTaskAsync(prompt, null, ct))
{
    switch (evt)
    {
        case ChatEvent chat when chat.Text is not null:
            textBuilder.Append(chat.Text);
            break;
        case FileChangeEvent file:
            // Store file path in metadata
            break;
        case SessionUpdateEvent session:
            sessionId = session.SessionId;
            break;
        case TaskCompleteEvent complete:
            inputTokens = complete.InputTokens;
            outputTokens = complete.OutputTokens;
            break;
        case ErrorEvent error:
            _logger.LogError("Sidecar error: {Message}", error.Message);
            return Result<string>.Failure(ErrorCode.InternalError, error.Message);
    }
}
```

This pattern should be extracted into a private helper method within `ContentPipeline` to avoid duplication between outline and draft generation.

### DbContext Changes Required

The `IApplicationDbContext` interface does not need modification for this section. Content entities are accessed via the existing `Contents` DbSet. The `BrandProfile` entity is accessed via the existing `BrandProfiles` DbSet for loading brand voice context during draft generation.

### DI Registration

Register in `DependencyInjection.cs` (done in section-12, but noted here for awareness):

```csharp
services.AddScoped<IContentPipeline, ContentPipeline>();
```

---

## Implementation Notes (Actual vs Planned)

**Date implemented:** 2026-03-16

### Files actually created/modified

All planned Application and Infrastructure files were created. Additional files:
- `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs` — Placeholder returning perfect score (100) until section-07
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs` — Created here (soft dependency from section-07)
- `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/ValidateVoiceCommandHandlerTests.cs` — Added during review
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` — Modified to register IContentPipeline + IBrandVoiceService (pulled forward from section-12)

### Deviations from plan

1. **GetContentTreeQuery/Handler deferred** — Query and handler not implemented; deferred to section-11 (API endpoints) where queries are consumed
2. **commitHash not captured** — Sidecar event model doesn't carry commit hashes yet; deferred to blog integration work
3. **Blog-specific prompt differentiation deferred** — All content types use the same prompt template; content-type-specific prompting is better addressed as a feature, not pipeline infrastructure
4. **SessionUpdateEvent not handled** — Not needed in ConsumeEventStreamAsync; session management is handled at the sidecar client level
5. **EstimatedCost not set** — ContentMetadata doesn't have separate input/output token fields; only `TokensUsed` (sum) is stored
6. **slug derivation not implemented** — Deferred to blog integration

### Code review fixes applied

1. Auto-approve failure in SubmitForReviewAsync now logs a warning instead of silently swallowing
2. ConsumeEventStreamAsync returns error message (5-tuple) so callers propagate sidecar errors
3. ParseGenerationContext catch narrowed from bare `catch` to `catch (JsonException)`
4. DI registration added for IContentPipeline and IBrandVoiceService (StubBrandVoiceService)
5. Missing ErrorCode import added to ContentPipelineTests.cs

### Test counts

- ContentPipelineTests: 17 tests (infrastructure)
- CreateFromTopicCommandHandlerTests: 2 tests
- GenerateOutlineCommandHandlerTests: 2 tests
- GenerateDraftCommandHandlerTests: 2 tests
- SubmitForReviewCommandHandlerTests: 2 tests
- ValidateVoiceCommandHandlerTests: 2 tests (added during review)
- **Total: 27 tests for section-04**