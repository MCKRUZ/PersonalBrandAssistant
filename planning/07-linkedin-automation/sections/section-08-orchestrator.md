I now have all the context needed. Let me compose the section content.

# Section 08: Daily Content Orchestrator

## Overview

The `DailyContentOrchestrator` is the central pipeline service that chains all autonomous workflow steps into a single execution: AI-curated trend selection, primary content creation, per-platform content generation, image generation, brand validation, and workflow transitions. It executes synchronously within a single scoped lifetime and produces an `AutomationRunResult` tracking the outcome.

This is the highest-complexity service in the feature. It consumes every other service built in sections 01 through 07 and is the sole entry point for both the daily scheduler (section-09) and the manual trigger endpoint (section-10).

The orchestrator handles two autonomy modes:
- **Autonomous**: Auto-approves, transitions to Scheduled, sets immediate publish. Sends a success summary after completion.
- **SemiAuto**: Transitions to Review and stops. Overrides `CapturedAutonomyLevel = Manual` on all child Content entities to prevent the existing `WorkflowEngine.ShouldAutoApproveAsync()` from auto-approving children when a parent is approved.

The orchestrator also implements a ComfyUI circuit breaker: after N consecutive image generation failures (configurable, default 3), it disables image generation and sends a notification.

---

## Dependencies

**Must be completed first:**
- **section-01-foundation** -- provides `AutomationRun` entity, `AutomationRunStatus` enum, `ContentAutomationOptions`, `ImageGenerationOptions`, `AutomationRunResult`, `ImageGenerationResult`, `IDailyContentOrchestrator` interface, `IApplicationDbContext.AutomationRuns` DbSet, new `NotificationType` values, `Content.ImageFileId` and `Content.ImageRequired` properties
- **section-03-image-services** -- provides `IImageGenerationService` and `IImagePromptService` implementations
- **section-05-formatter-changes** -- provides image passthrough in platform formatters (not directly called by orchestrator, but required for the image data to flow through publishing)
- **section-06-linkedin-image-upload** -- provides image upload capability in `LinkedInPlatformAdapter`
- **section-07-platform-content-gen** -- provides `IContentPipeline.GeneratePlatformDraftAsync()` and the `ContentType?` override on `ITrendMonitor.AcceptSuggestionAsync()`

**Existing services consumed (no modifications needed):**
- `ITrendMonitor` at `src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs` -- `GetSuggestionsAsync(limit, ct)` and `AcceptSuggestionAsync(suggestionId, ct)` (with `ContentType?` parameter added by section-07)
- `IContentPipeline` at `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs` -- `GenerateOutlineAsync`, `GenerateDraftAsync`, `ValidateVoiceAsync`, `SubmitForReviewAsync`, `GeneratePlatformDraftAsync` (added by section-07)
- `IWorkflowEngine` at `src/PersonalBrandAssistant.Application/Common/Interfaces/IWorkflowEngine.cs` -- `TransitionAsync(contentId, targetStatus, reason, actor, ct)`
- `ISidecarClient` at `src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs` -- `SendTaskAsync(task, systemPrompt, sessionId, ct)` for AI-curated trend selection
- `IImageResizer` at `src/PersonalBrandAssistant.Application/Common/Interfaces/IImageResizer.cs` -- `ResizeForPlatformsAsync(sourceFileId, platforms, ct)`
- `INotificationService` at `src/PersonalBrandAssistant.Application/Common/Interfaces/INotificationService.cs` -- `SendAsync(type, title, message, contentId, ct)`
- `IApplicationDbContext` at `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` -- for querying `Contents`, `AutomationRuns`, `TrendSuggestions`

**Blocks:** section-09 (scheduler), section-10 (API endpoints)

---

## Files to Create or Modify

| File | Action |
|------|--------|
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/DailyContentOrchestratorTests.cs` | **Create** -- unit tests for all 7 pipeline steps |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/CircuitBreakerTests.cs` | **Create** -- circuit breaker logic tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AutomationPipelineIntegrationTests.cs` | **Create** -- end-to-end pipeline integration tests |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/DailyContentOrchestrator.cs` | **Create** -- implements `IDailyContentOrchestrator` |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | **Modify** -- replace the `IDailyContentOrchestrator` stub registration with the real `DailyContentOrchestrator` |

---

## Tests (Write First)

All tests use xUnit + Moq, consistent with existing PBA test conventions. Follow the same mock setup patterns as `ContentPipelineTests` (mock `IApplicationDbContext` with `MockQueryable.Moq` for DbSet queries, mock `ISidecarClient` with `IAsyncEnumerable<SidecarEvent>` yields).

### Test File: `DailyContentOrchestratorTests.cs`

**Location:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/DailyContentOrchestratorTests.cs`

This is the largest test file. Organize tests into nested classes by pipeline step for readability.

**Mocked dependencies** (setup in constructor or shared fixture):
- `Mock<ITrendMonitor>` -- `GetSuggestionsAsync`, `AcceptSuggestionAsync`
- `Mock<IContentPipeline>` -- `GenerateOutlineAsync`, `GenerateDraftAsync`, `ValidateVoiceAsync`, `SubmitForReviewAsync`, `GeneratePlatformDraftAsync`
- `Mock<IWorkflowEngine>` -- `TransitionAsync`
- `Mock<ISidecarClient>` -- `SendTaskAsync` (for AI-curated trend selection)
- `Mock<IImagePromptService>` -- `GeneratePromptAsync`
- `Mock<IImageGenerationService>` -- `GenerateAsync`
- `Mock<IImageResizer>` -- `ResizeForPlatformsAsync`
- `Mock<INotificationService>` -- `SendAsync`
- `Mock<IApplicationDbContext>` -- `AutomationRuns`, `Contents`, `TrendSuggestions` DbSets
- `Mock<ILogger<DailyContentOrchestrator>>`

**Helper methods:**
- `CreateDefaultOptions()` returning a `ContentAutomationOptions` with sensible defaults for testing (single platform `LinkedIn`, `Enabled = true`, `AutonomyLevel = "SemiAuto"`, `TopTrendsToConsider = 5`)
- `SetupSidecarCurationResponse(string json)` configuring the mock `ISidecarClient.SendTaskAsync` to yield a `ChatEvent` with the given JSON text followed by a `TaskCompleteEvent`
- `CreatePendingSuggestions(int count)` returning a list of `TrendSuggestion` entities with `Status = Pending`

#### Step 1: AI-Curated Trend Selection Tests

```csharp
public class TrendSelectionTests
{
    [Fact]
    public async Task Fails_WithNoTrendsAvailable_WhenGetSuggestionsReturnsEmpty()
    /// Setup: GetSuggestionsAsync returns empty list.
    /// Assert: Result.Success is false, AutomationRun.ErrorDetails contains "No trends available".

    [Fact]
    public async Task FiltersSuggestions_ToPendingStatusOnly()
    /// Setup: GetSuggestionsAsync returns a mix of Pending and Accepted suggestions.
    /// Assert: Only Pending suggestions are passed to the sidecar curation prompt.
    /// Note: The current GetSuggestionsAsync already filters to Pending internally,
    /// but the orchestrator should validate this after receiving the results.

    [Fact]
    public async Task SendsTopSuggestions_ToSidecarWithStructuredJsonInstructions()
    /// Setup: GetSuggestionsAsync returns 5 pending suggestions.
    /// Assert: SendTaskAsync is called once. The task string contains the suggestion
    /// topics and a system prompt requesting structured JSON: {"suggestionId", "reasoning", "contentType"}.

    [Fact]
    public async Task ParsesValidJsonResponse_WithSuggestionIdReasoningContentType()
    /// Setup: Sidecar returns valid JSON: {"suggestionId":"<guid>","reasoning":"..","contentType":"SocialPost"}.
    /// Assert: AcceptSuggestionAsync is called with the correct suggestionId.

    [Fact]
    public async Task RetriesSidecarPrompt_OnceOnJsonParseFailure()
    /// Setup: First sidecar call returns invalid JSON, second returns valid JSON.
    /// Assert: SendTaskAsync is called exactly twice. Pipeline succeeds.

    [Fact]
    public async Task FailsRun_AfterRetryOnPersistentParseFailure()
    /// Setup: Both sidecar calls return invalid JSON.
    /// Assert: Result.Success is false. AutomationRun.Status is Failed.
    /// AutomationRun.ErrorDetails contains parse failure message.

    [Fact]
    public async Task QueriesLast7DaysOfPublishedContent_ForDiversityCheck()
    /// Setup: Populate Contents DbSet with Published items from the last 7 days.
    /// Assert: The sidecar prompt includes the recent topic list for diversity consideration.
}
```

#### Step 2: Content Creation Tests

```csharp
public class ContentCreationTests
{
    [Fact]
    public async Task CallsAcceptSuggestionAsync_WithAiSelectedContentType()
    /// Setup: Sidecar curation picks contentType "Thread".
    /// Assert: AcceptSuggestionAsync is called with the correct suggestionId
    /// and the ContentType override parameter (ContentType.Thread).

    [Fact]
    public async Task CallsGenerateOutline_ThenGenerateDraft_InSequence()
    /// Setup: Standard happy path with one platform.
    /// Assert: GenerateOutlineAsync is called before GenerateDraftAsync.
    /// Use Moq.Sequence or callback counters to verify order.

    [Fact]
    public async Task FailsRun_WhenAcceptSuggestionAsyncReturnsFailure()
    /// Setup: AcceptSuggestionAsync returns Result.Failure.
    /// Assert: Result.Success is false. No further pipeline steps are called.
}
```

#### Step 3: Platform Content Generation Tests

```csharp
public class PlatformContentGenerationTests
{
    [Fact]
    public async Task CreatesChildContentEntity_PerTargetPlatform_WithParentContentIdSet()
    /// Setup: Options has TargetPlatforms = ["LinkedIn", "TwitterX"].
    /// Assert: Two child Content entities are created with ParentContentId
    /// pointing to the primary content. Each has a single platform in TargetPlatforms.

    [Fact]
    public async Task CallsGeneratePlatformDraftAsync_ForEachPlatform()
    /// Setup: Two target platforms.
    /// Assert: GeneratePlatformDraftAsync called twice, once per platform,
    /// with the correct contentId, platform, and parent body.

    [Fact]
    public async Task SetsCapturedAutonomyLevel_ToManual_OnChildren_WhenSemiAutoMode()
    /// Setup: Options.AutonomyLevel = "SemiAuto". One target platform.
    /// Assert: The child Content entity is created with CapturedAutonomyLevel = Manual.
    /// This prevents WorkflowEngine.ShouldAutoApproveAsync() from auto-approving
    /// children when the parent is approved.

    [Fact]
    public async Task EachChild_HasCorrectSinglePlatformInTargetPlatforms()
    /// Setup: Options has TargetPlatforms = ["LinkedIn", "TwitterX", "PersonalBlog"].
    /// Assert: Each child has exactly one platform in its TargetPlatforms array.
}
```

#### Step 4: Image Generation Tests

```csharp
public class ImageGenerationTests
{
    [Fact]
    public async Task CallsImagePromptService_ThenImageGenerationService_InSequence()
    /// Assert: GeneratePromptAsync is called with the primary content body.
    /// Then GenerateAsync is called with the returned prompt string.

    [Fact]
    public async Task OnImageFailure_TransitionsAllContent_ToReviewStatus()
    /// Setup: ImageGenerationService.GenerateAsync returns ImageGenerationResult(false, ...).
    /// Assert: WorkflowEngine.TransitionAsync is called for parent AND each child
    /// with targetStatus = ContentStatus.Review.

    [Fact]
    public async Task OnImageFailure_SendsNotification_ViaNotificationService()
    /// Setup: Image generation fails.
    /// Assert: NotificationService.SendAsync is called with type AutomationImageFailed.

    [Fact]
    public async Task OnImageFailure_DoesNotProceedToBrandValidationOrWorkflowTransitions()
    /// Setup: Image generation fails.
    /// Assert: ValidateVoiceAsync is never called. SubmitForReviewAsync is never called.

    [Fact]
    public async Task OnImageSuccess_CallsImageResizer_ForAllTargetPlatforms()
    /// Setup: Image generation succeeds with fileId = "abc". Two target platforms.
    /// Assert: ResizeForPlatformsAsync called with "abc" and both platforms.

    [Fact]
    public async Task AssociatesPlatformSpecificImageFileId_WithEachChildContent()
    /// Setup: Resizer returns {"LinkedIn": "img-li", "TwitterX": "img-tw"}.
    /// Assert: Each child Content entity's ImageFileId is set to the correct
    /// platform-specific cropped file ID.

    [Fact]
    public async Task SetsImageRequired_True_OnAllContentVersions()
    /// Setup: Standard happy path with image generation enabled.
    /// Assert: Parent content and all children have ImageRequired = true.
}
```

#### Step 5: Brand Validation Tests

```csharp
public class BrandValidationTests
{
    [Fact]
    public async Task CallsValidateVoiceAsync_ForEachContentVersion()
    /// Setup: 1 parent + 2 children.
    /// Assert: ValidateVoiceAsync called 3 times (parent + 2 children).

    [Fact]
    public async Task LowBrandScore_DoesNotBlockPipeline()
    /// Setup: ValidateVoiceAsync returns a score below threshold.
    /// Assert: Pipeline continues. SubmitForReviewAsync is still called.
}
```

#### Step 6: Workflow Transition Tests

```csharp
public class WorkflowTransitionTests
{
    [Fact]
    public async Task AutonomousMode_CallsSubmitForReview_ThenTransitionToScheduled_ThenSetsScheduledAt()
    /// Setup: Options.AutonomyLevel = "Autonomous".
    /// Assert: SubmitForReviewAsync called for each content.
    /// Then TransitionAsync(contentId, Scheduled) called for each.
    /// Content.ScheduledAt is set to approximately DateTimeOffset.UtcNow.

    [Fact]
    public async Task SemiAutoMode_CallsSubmitForReviewOnly_StopsAtReview()
    /// Setup: Options.AutonomyLevel = "SemiAuto".
    /// Assert: SubmitForReviewAsync called. TransitionAsync to Scheduled NOT called.

    [Fact]
    public async Task SemiAutoMode_SendsReadyForReviewNotification()
    /// Setup: SemiAuto mode, 2 platforms.
    /// Assert: NotificationService.SendAsync called with type ContentReadyForReview
    /// and message containing the topic + platform count.

    [Fact]
    public async Task AutonomousMode_SendsCompletionNotification()
    /// Setup: Autonomous mode, pipeline completes.
    /// Assert: NotificationService.SendAsync called with type AutomationPipelineCompleted.
}
```

#### Step 7: Recording Tests

```csharp
public class RecordingTests
{
    [Fact]
    public async Task CreatesAutomationRun_WithCompletedStatus_OnSuccess()
    /// Assert: AutomationRun added to DbContext with Status == Completed.

    [Fact]
    public async Task CreatesAutomationRun_WithFailedStatus_OnAnyStepFailure()
    /// Setup: Trend selection fails (no trends).
    /// Assert: AutomationRun has Status == Failed, ErrorDetails populated.

    [Fact]
    public async Task RecordsDurationMs_Accurately()
    /// Assert: DurationMs in the AutomationRun is > 0 and represents elapsed time.

    [Fact]
    public async Task RecordsAllContentIds_AndImageFileIds()
    /// Assert: AutomationRun.PrimaryContentId matches the created content.
    /// AutomationRun.ImageFileId matches the generated image.
    /// AutomationRun.PlatformVersionCount matches the number of children created.
    /// AutomationRun.SelectionReasoning matches the sidecar's reasoning.
}
```

### Test File: `CircuitBreakerTests.cs`

**Location:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/CircuitBreakerTests.cs`

```csharp
public class CircuitBreakerTests
{
    [Fact]
    public async Task TripsAfterNConsecutiveComfyUiFailures()
    /// Setup: Query AutomationRuns to find N consecutive Failed runs where
    /// ErrorDetails contains "ComfyUI". N = CircuitBreakerThreshold (default 3).
    /// Assert: On the Nth failure, the orchestrator disables image generation.

    [Fact]
    public async Task SendsNotification_WhenCircuitBreakerTrips()
    /// Setup: Threshold reached.
    /// Assert: NotificationService.SendAsync called with AutomationConsecutiveFailure type.

    [Fact]
    public async Task PipelineSkipsImageGeneration_WhenCircuitBreakerIsTripped()
    /// Setup: ImageGeneration.Enabled = false (circuit breaker state).
    /// Assert: IImagePromptService and IImageGenerationService are never called.
    /// Content transitions to Review (since images are required but not generated).

    [Fact]
    public async Task CounterResets_OnSuccessfulImageGeneration()
    /// Setup: 2 consecutive failures, then a success.
    /// Assert: Counter is back to 0. Next failure does not trip the breaker.
}
```

### Test File: `AutomationPipelineIntegrationTests.cs`

**Location:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AutomationPipelineIntegrationTests.cs`

These use a more complete setup (mock sidecar + ComfyUI, but real or in-memory DB). They verify the full pipeline flow.

```csharp
public class AutomationPipelineIntegrationTests
{
    [Fact]
    public async Task FullPipeline_HappyPath_CreatesContentAndRecordsRun()
    /// Setup: Mock sidecar returns valid curation JSON + content text.
    /// Mock image services succeed. Real DbContext (in-memory).
    /// Assert: AutomationRun has Status = Completed.
    /// Primary content exists. Children exist per platform.
    /// Image file IDs are populated on children.

    [Fact]
    public async Task FullPipeline_ImageFailure_NoContentPublished()
    /// Setup: Everything succeeds except image generation (returns failure).
    /// Assert: AutomationRun has Status = Failed.
    /// All content (parent + children) remains in Review status.
    /// No content reaches Scheduled or Published.

    [Fact]
    public async Task FullPipeline_SemiAutoMode_ContentStopsAtReview()
    /// Setup: Full happy path but AutonomyLevel = "SemiAuto".
    /// Assert: All content is in Review status.
    /// No content reaches Approved, Scheduled, or Published.
    /// Children have CapturedAutonomyLevel = Manual.
}
```

---

## Implementation Details

### 1. Class Structure

**New file:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/DailyContentOrchestrator.cs`

The orchestrator implements `IDailyContentOrchestrator` and takes all dependencies via constructor injection:

```csharp
namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

public sealed class DailyContentOrchestrator : IDailyContentOrchestrator
{
    /// Constructor dependencies:
    /// - ITrendMonitor
    /// - IContentPipeline
    /// - IWorkflowEngine
    /// - ISidecarClient (for AI-curated trend selection)
    /// - IImagePromptService
    /// - IImageGenerationService
    /// - IImageResizer
    /// - INotificationService
    /// - IApplicationDbContext
    /// - ILogger<DailyContentOrchestrator>

    public async Task<AutomationRunResult> ExecuteAsync(
        ContentAutomationOptions options, CancellationToken ct)
    {
        /// Implementation described below step by step
    }
}
```

Use `System.Diagnostics.Stopwatch` to track `DurationMs` for the `AutomationRun`.

### 2. Pipeline Step 1: AI-Curated Trend Selection

The orchestrator begins by creating an `AutomationRun` entity with `Status = Running` and saving it to the database. This is the first thing that happens so that concurrent runs can be detected.

Load the top N suggestions via `ITrendMonitor.GetSuggestionsAsync(limit: options.TopTrendsToConsider)`. The `TrendMonitor.GetSuggestionsAsync` already filters for `Status == Pending` internally, but the orchestrator should defensively filter the results again to be safe.

If zero pending suggestions are returned, fail the run: create/update `AutomationRun` with `Status = Failed`, `ErrorDetails = "No trends available"`, send a notification with `NotificationType.AutomationNoTrends`, and return an `AutomationRunResult` with `Success = false`.

Query the last 7 days of published content from the `Contents` DbSet:
```csharp
var recentTopics = await _dbContext.Contents
    .Where(c => c.Status == ContentStatus.Published
        && c.PublishedAt >= DateTimeOffset.UtcNow.AddDays(-7))
    .Select(c => c.Title)
    .ToListAsync(ct);
```

Build a sidecar task string that includes:
- The suggestion list (IDs, topics, relevance scores)
- Recent published topics (for diversity)
- Brand profile context (optional, if available)
- Explicit instructions to return JSON: `{"suggestionId": "<guid>", "reasoning": "...", "contentType": "SocialPost|Thread|BlogPost"}`

Send to sidecar via `ISidecarClient.SendTaskAsync(task, systemPrompt, null, ct)`. Consume the `IAsyncEnumerable<SidecarEvent>` stream. Collect the last `ChatEvent` with `EventType == "summary"` as the AI response text.

Parse the response with `JsonSerializer.Deserialize`. Use a small DTO record:

```csharp
private record TrendCurationResponse(Guid SuggestionId, string Reasoning, string ContentType);
```

On `JsonException`: retry once by sending the prompt again with explicit formatting instructions appended ("Your previous response was not valid JSON. Please respond with ONLY a JSON object..."). On second failure, fail the run with a clear error.

Parse the `ContentType` string to `Domain.Enums.ContentType` via `Enum.TryParse`. Default to `ContentType.SocialPost` if the string is unrecognized.

### 3. Pipeline Step 2: Primary Content Creation

Call `ITrendMonitor.AcceptSuggestionAsync(suggestionId, contentType, ct)` where `contentType` is the AI-curated `ContentType` from Step 1. This creates the primary `Content` entity. The `ContentType?` override parameter is added by section-07.

If `AcceptSuggestionAsync` returns a failure, fail the run.

The returned `Guid` is the `primaryContentId`. Call:
1. `IContentPipeline.GenerateOutlineAsync(primaryContentId, ct)`
2. `IContentPipeline.GenerateDraftAsync(primaryContentId, ct)`

If either fails, fail the run and record the error. The content remains in `Draft` status.

After draft generation, reload the primary content to get the body text for platform generation:
```csharp
var primaryContent = await _dbContext.Contents.FindAsync([primaryContentId], ct);
var parentBody = primaryContent!.Body;
```

### 4. Pipeline Step 3: Platform-Specific Content Generation

For each platform in `options.TargetPlatforms`:

1. Parse the platform string to `PlatformType` via `Enum.TryParse`
2. Create a child Content entity using `ContentPipeline.CreateFromTopicAsync` with:
   - `Type` = same as primary content type (or a platform-appropriate type)
   - `Topic` = primary content's topic
   - `TargetPlatforms` = `[platform]` (single platform)
   - `ParentContentId` = `primaryContentId`
3. **Critical for SemiAuto mode:** The child must be created with `CapturedAutonomyLevel = Manual`. Since `Content.CapturedAutonomyLevel` has `private init`, it can only be set at construction time. Use `Content.Create(type, body, title, targetPlatforms, capturedAutonomyLevel: AutonomyLevel.Manual)` when `options.AutonomyLevel` is `"SemiAuto"`. Without this, `WorkflowEngine.ShouldAutoApproveAsync()` will auto-approve children when `ParentContentId` is set and the parent is Approved (see `WorkflowEngine.cs:135-137`).
4. Call `IContentPipeline.GeneratePlatformDraftAsync(childContentId, platform, parentBody, ct)`

Track all child content IDs in a `List<(Guid ContentId, PlatformType Platform)>` for use in subsequent steps.

### 5. Pipeline Step 4: Image Generation

Call `IImagePromptService.GeneratePromptAsync(parentBody, ct)` to get a FLUX-optimized prompt.

Call `IImageGenerationService.GenerateAsync(prompt, options.ImageGeneration, ct)`.

**On failure** (when `ImageGenerationResult.Success == false`):
1. Transition ALL content (parent + all children) to Review status via `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Review, "Image generation failed", ActorType.System, ct)`. Note: content is in Draft status, so the transition path is Draft -> Review which is valid per the state machine.
2. Send notification: `INotificationService.SendAsync(NotificationType.AutomationImageFailed, "Image generation failed", errorMessage, primaryContentId, ct)`
3. Record the failure in the `AutomationRun`: update with `Fail(errorDetails, durationMs)`
4. Check the circuit breaker (see section 7 below)
5. **Stop the pipeline.** Do NOT proceed to brand validation or workflow transitions.
6. Return `AutomationRunResult(Success: false, ...)`

**On success:**
1. Call `IImageResizer.ResizeForPlatformsAsync(result.FileId, parsedPlatforms, ct)` where `parsedPlatforms` is the array of `PlatformType` from the target platforms
2. The resizer returns `IReadOnlyDictionary<PlatformType, string>` mapping each platform to its cropped file ID
3. For each child content, set `ImageFileId` to the platform-specific cropped file ID: `childContent.ImageFileId = platformImages[platform]`
4. Also set `ImageFileId` on the parent content to the original source file ID: `primaryContent.ImageFileId = result.FileId`
5. Set `ImageRequired = true` on ALL content versions (parent + children)
6. Record `AutomationRun.ImageFileId = result.FileId` and `AutomationRun.ImagePrompt = prompt`
7. Save changes to the database

### 6. Pipeline Step 5: Brand Validation

For each content version (parent + all children):
- Call `IContentPipeline.ValidateVoiceAsync(contentId, ct)`
- Log the score but do NOT block the pipeline regardless of score

Brand validation is informational at this stage. Low scores will be visible to the reviewer in SemiAuto mode.

### 7. Pipeline Step 6: Workflow Transitions

Determine the autonomy level by parsing `options.AutonomyLevel` to `AutonomyLevel` enum.

**Autonomous mode:**
For each content version (parent + children):
1. Call `IContentPipeline.SubmitForReviewAsync(contentId, ct)` -- this transitions Draft -> Review -> Approved (via `WorkflowEngine`'s auto-approval chain, since `CapturedAutonomyLevel` is `Autonomous`)
2. Call `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Scheduled, "Auto-scheduled by automation pipeline", ActorType.System, ct)` -- the existing `SubmitForReviewAsync` only auto-approves to `Approved`, it does not schedule
3. Set `content.ScheduledAt = DateTimeOffset.UtcNow` for immediate pickup by the `ScheduledPublishProcessor`
4. Save changes

After all transitions, send a completion notification:
`INotificationService.SendAsync(NotificationType.AutomationPipelineCompleted, "Content published", "Published {topic} to {platforms}. Run duration: {duration}s", primaryContentId, ct)`

**SemiAuto mode:**
For each content version:
1. Call `IContentPipeline.SubmitForReviewAsync(contentId, ct)` -- this transitions Draft -> Review. Because children have `CapturedAutonomyLevel = Manual`, the auto-approval chain in `WorkflowEngine.ShouldAutoApproveAsync()` will return `false`, preventing unintended auto-approval.
2. Stop. Do not transition to Approved, Scheduled, or Published.

Send a review notification:
`INotificationService.SendAsync(NotificationType.ContentReadyForReview, "Content ready for review", "{topic} -- {count} platform versions", primaryContentId, ct)`

### 8. Pipeline Step 7: Record Run

Update the `AutomationRun` entity:
- `Complete(durationMs)` on success
- Set `SelectedSuggestionId`, `PrimaryContentId`, `ImageFileId`, `ImagePrompt`, `SelectionReasoning`, `PlatformVersionCount`
- Call `_dbContext.SaveChangesAsync(ct)`

Return `AutomationRunResult` with all relevant data.

### 9. Circuit Breaker Logic

Implement as a private method in the orchestrator, called after each image generation failure.

Query the `AutomationRuns` table for the most recent N runs (where N = `options.ImageGeneration.CircuitBreakerThreshold`):

```csharp
private async Task CheckCircuitBreakerAsync(ContentAutomationOptions options, CancellationToken ct)
{
    var threshold = options.ImageGeneration.CircuitBreakerThreshold;
    var recentFailures = await _dbContext.AutomationRuns
        .Where(r => r.Status == AutomationRunStatus.Failed
            && r.ErrorDetails != null
            && r.ErrorDetails.Contains("ComfyUI"))
        .OrderByDescending(r => r.TriggeredAt)
        .Take(threshold)
        .ToListAsync(ct);

    if (recentFailures.Count >= threshold)
    {
        // Trip the circuit breaker
        // Note: The orchestrator itself does not modify appsettings.
        // Instead, it sends a notification and the image generation
        // Enabled check at the start of Step 4 handles the skip.
        await _notificationService.SendAsync(
            NotificationType.AutomationConsecutiveFailure,
            "ComfyUI circuit breaker tripped",
            $"ComfyUI has been unreachable for {threshold} consecutive days. Image generation should be disabled.",
            null, ct);
    }
}
```

At the start of Step 4, check `options.ImageGeneration.Enabled`. If false, skip image generation entirely. Since `ImageRequired` will not be set to true, the content will be held in Review status when a formatter encounters `ImageRequired == true && ImageFileId == null`.

The counter effectively resets when a successful run occurs because the most recent N runs will no longer all be failures.

### 10. Error Handling Strategy

Wrap the entire pipeline in a `try-catch`:

```csharp
public async Task<AutomationRunResult> ExecuteAsync(
    ContentAutomationOptions options, CancellationToken ct)
{
    var stopwatch = Stopwatch.StartNew();
    var run = AutomationRun.Create();
    _dbContext.AutomationRuns.Add(run);
    await _dbContext.SaveChangesAsync(ct);

    try
    {
        // Steps 1-7...

        stopwatch.Stop();
        run.Complete(stopwatch.ElapsedMilliseconds);
        // set other fields...
        await _dbContext.SaveChangesAsync(ct);
        return new AutomationRunResult(true, run.Id, ...);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        _logger.LogError(ex, "Automation pipeline failed");
        run.Fail(ex.Message, stopwatch.ElapsedMilliseconds);
        await _dbContext.SaveChangesAsync(ct);
        return new AutomationRunResult(false, run.Id, ..., Error: ex.Message, ...);
    }
}
```

Each individual step failure (e.g., no trends, sidecar parse failure, image failure) should be handled gracefully within the step using `Result<T>.IsSuccess` checks rather than throwing exceptions. The outer try-catch is a safety net for unexpected errors.

When an intermediate step fails:
1. Transition any created content to an appropriate state (Review if partially generated, leave as Draft if generation failed)
2. Record the error in the `AutomationRun`
3. Send a notification
4. Return early with a failed `AutomationRunResult`

### 11. Sidecar Interaction for Trend Curation

The sidecar interaction follows the same pattern as `ContentPipeline.ConsumeEventStreamAsync()`. Build a similar private helper method:

```csharp
private async Task<(string? Text, string? Error)> ConsumeSidecarResponseAsync(
    string task, string? systemPrompt, CancellationToken ct)
{
    string? lastSummary = null;

    await foreach (var evt in _sidecarClient.SendTaskAsync(task, systemPrompt, null, ct))
    {
        switch (evt)
        {
            case ChatEvent { EventType: "summary", Text: not null } chat:
                lastSummary = chat.Text;
                break;
            case ErrorEvent error:
                _logger.LogError("Sidecar error: {Message}", error.Message);
                return (null, error.Message);
        }
    }

    return (lastSummary, null);
}
```

The system prompt for trend curation should instruct Claude to:
- Consider engagement potential for the target audience (tech professionals, developers)
- Check topic diversity against the provided recent topics list
- Evaluate brand alignment
- Return ONLY a JSON object with the exact schema specified

### 12. DI Registration Update

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Replace the `IDailyContentOrchestrator` stub registration (from section-01) with the real implementation:

```csharp
services.AddScoped<IDailyContentOrchestrator, DailyContentOrchestrator>();
```

Remove the corresponding stub from `NotImplementedStubs.cs` if it exists.

---

## Key Design Decisions

1. **Child content autonomy override**: The most critical implementation detail. In SemiAuto mode, children MUST be created with `CapturedAutonomyLevel = Manual`. Otherwise, `WorkflowEngine.ShouldAutoApproveAsync()` at line 135 will auto-approve children because they have a `ParentContentId` and the condition `SemiAuto when content.ParentContentId is not null => IsParentPublishedOrApproved(...)` will evaluate to true once the parent reaches Approved status.

2. **Image failure blocks everything**: When image generation fails, the orchestrator does NOT publish any content. All content moves to Review status. This matches the stakeholder requirement that images are essential, not optional.

3. **AutomationRun is created first**: The `AutomationRun` entity is saved to the database with `Status = Running` before any pipeline work begins. This enables the idempotency check in the scheduler (section-09) and prevents concurrent duplicate runs.

4. **Single transaction scope**: The orchestrator runs within a single scoped lifetime. All database operations use the same `IApplicationDbContext` instance. SaveChanges is called at multiple checkpoints (after run creation, after content creation, after image association, after completion) rather than a single final save, so that partial progress is recorded even on failure.

5. **Platform string parsing**: The `ContentAutomationOptions.TargetPlatforms` is `string[]` to match appsettings.json binding. The orchestrator must parse each string to `PlatformType` via `Enum.TryParse`. Invalid platform strings should be logged and skipped, not fail the entire pipeline.

---

## Verification

After implementation, run:

```bash
cd C:/Users/kruz7/OneDrive/Documents/Code\ Repos/MCKRUZ/personal-brand-assistant
dotnet build
dotnet test --filter "FullyQualifiedName~DailyContentOrchestrator|FullyQualifiedName~CircuitBreaker|FullyQualifiedName~AutomationPipelineIntegration"
```

All tests should pass. The orchestrator should compile and be resolvable from DI (replacing the stub from section-01). The existing `ContentPipelineTests` and `WorkflowEngineStatelessIntegrationTests` should continue to pass unchanged.