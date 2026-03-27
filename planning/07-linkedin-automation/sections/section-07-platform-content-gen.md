Now I have everything I need. Let me compose the section content.

# Section 07: Platform Content Generation

## Overview

This section adds the ability to generate unique, platform-specific content drafts from a single primary content body. Rather than reformatting the same text for each platform, the system sends the primary content to the Claude sidecar with a platform-specific system prompt that produces authentic, native-feeling content for LinkedIn, TwitterX, and PersonalBlog.

Two changes are required:
1. **Add `GeneratePlatformDraftAsync` to `IContentPipeline` and `ContentPipeline`** -- a new method that takes a content ID, target platform, and parent body, then generates a platform-tailored draft via the sidecar.
2. **Add a `ContentType?` override parameter to `ITrendMonitor.AcceptSuggestionAsync`** -- so the orchestrator (section-08) can override the suggestion's default content type with the AI-curated recommendation.

## Dependencies

- **section-01-foundation** must be completed first. It provides:
  - `ContentAutomationOptions` with `PlatformPrompts` dictionary (platform-specific system prompt overrides)
  - The `ContentType` enum already exists at `src/PersonalBrandAssistant.Domain/Enums/ContentType.cs`

No other sections are required. This section can be built in parallel with section-02, section-03, section-04, and section-05.

## Files to Create or Modify

| File | Action |
|------|--------|
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/PlatformContentGenerationTests.cs` | **Create** -- tests for `GeneratePlatformDraftAsync` |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/AcceptSuggestionContentTypeTests.cs` | **Create** -- tests for ContentType override on `AcceptSuggestionAsync` |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs` | **Modify** -- add `GeneratePlatformDraftAsync` method signature |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs` | **Modify** -- implement `GeneratePlatformDraftAsync` |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs` | **Modify** -- add optional `ContentType?` parameter to `AcceptSuggestionAsync` |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendMonitor.cs` | **Modify** -- implement ContentType override in `AcceptSuggestionAsync` |

## Tests (Write First)

### Test File: `PlatformContentGenerationTests.cs`

**Location:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/PlatformContentGenerationTests.cs`

This test class follows the existing `ContentPipelineTests` pattern: mock `IApplicationDbContext`, `ISidecarClient`, `IBrandVoiceService`, `IWorkflowEngine`, `IPipelineEventBroadcaster`, and `ILogger<ContentPipeline>`. Use `MockQueryable.Moq` for DbSet mocking.

Test stubs to implement:

```csharp
/// Tests for ContentPipeline.GeneratePlatformDraftAsync
public class PlatformContentGenerationTests
{
    // Setup: mock dependencies following ContentPipelineTests pattern
    // Create a Content entity with Body set (the "parent body") and
    // a child Content entity with ParentContentId set

    [Fact]
    public async Task GeneratePlatformDraftAsync_LoadsBrandVoiceContext()
    /// Verify that the method queries BrandProfiles for the active profile
    /// and includes persona/tone/style in the prompt sent to sidecar.

    [Fact]
    public async Task GeneratePlatformDraftAsync_UsesLinkedInSystemPrompt_ForLinkedInPlatform()
    /// When platform is PlatformType.LinkedIn, verify the sidecar receives
    /// a system prompt containing LinkedIn-specific guidance: professional tone,
    /// thought leadership, max 3000 chars, hashtags.

    [Fact]
    public async Task GeneratePlatformDraftAsync_UsesTwitterSystemPrompt_ForTwitterXPlatform()
    /// When platform is PlatformType.TwitterX, verify the sidecar receives
    /// a system prompt containing Twitter-specific guidance: punchy, opinionated,
    /// max 280 chars, dev-community credible.

    [Fact]
    public async Task GeneratePlatformDraftAsync_UsesBlogSystemPrompt_ForPersonalBlogPlatform()
    /// When platform is PlatformType.PersonalBlog, verify the sidecar receives
    /// a system prompt containing blog teaser guidance: drives traffic, SEO-conscious.

    [Fact]
    public async Task GeneratePlatformDraftAsync_StoresGeneratedBodyInContentEntity()
    /// Verify that the sidecar response text is written to content.Body
    /// and SaveChangesAsync is called.

    [Fact]
    public async Task GeneratePlatformDraftAsync_ContentNotFound_ReturnsNotFound()
    /// When the content ID does not exist, returns Result with ErrorCode.NotFound.

    [Fact]
    public async Task GeneratePlatformDraftAsync_SidecarError_ReturnsFailure()
    /// When the sidecar returns an ErrorEvent, returns Result with ErrorCode.InternalError.

    [Fact]
    public async Task GeneratePlatformDraftAsync_SystemPromptsReferenceHumanizerRules()
    /// Verify the system prompt sent to sidecar includes instructions for
    /// humanized writing: no em-dashes, natural language patterns.

    [Fact]
    public async Task GeneratePlatformDraftAsync_IncludesParentBodyAsContext()
    /// Verify the parentBody string is included in the task prompt sent to sidecar,
    /// giving the AI the primary content to rewrite.

    [Fact]
    public async Task GeneratePlatformDraftAsync_UsesConfigurablePromptOverride()
    /// When ContentAutomationOptions.PlatformPrompts has a custom prompt for the
    /// target platform, that prompt is used as the system prompt instead of the default.
}
```

### Test File: `AcceptSuggestionContentTypeTests.cs`

**Location:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/AcceptSuggestionContentTypeTests.cs`

```csharp
/// Tests for the ContentType override parameter on AcceptSuggestionAsync
public class AcceptSuggestionContentTypeTests
{
    // Setup: mock IApplicationDbContext with TrendSuggestions DbSet containing
    // a suggestion with SuggestedContentType = ContentType.BlogPost

    [Fact]
    public async Task AcceptSuggestionAsync_WithContentTypeOverride_UsesOverrideType()
    /// Call AcceptSuggestionAsync(suggestionId, ct, ContentType.SocialPost).
    /// Verify the created Content entity has ContentType == SocialPost,
    /// not the suggestion's default BlogPost.

    [Fact]
    public async Task AcceptSuggestionAsync_WithoutContentTypeOverride_UsesSuggestionType()
    /// Call AcceptSuggestionAsync(suggestionId, ct) without the optional parameter.
    /// Verify the created Content entity has ContentType == BlogPost (the suggestion's default).

    [Fact]
    public async Task AcceptSuggestionAsync_WithNullContentTypeOverride_UsesSuggestionType()
    /// Call AcceptSuggestionAsync(suggestionId, ct, null).
    /// Verify fallback to suggestion's SuggestedContentType.
}
```

## Implementation Details

### 1. Add `GeneratePlatformDraftAsync` to `IContentPipeline`

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs`

Add the following method to the interface:

```csharp
Task<Result<string>> GeneratePlatformDraftAsync(
    Guid contentId, PlatformType platform, string parentBody, CancellationToken ct);
```

This method takes:
- `contentId` -- the child Content entity (already created with `ParentContentId` set and `TargetPlatforms = [platform]`)
- `platform` -- which platform to generate for (determines the system prompt)
- `parentBody` -- the primary content body to rewrite for the target platform
- `ct` -- cancellation token

Returns `Result<string>` with the generated platform-specific body on success.

### 2. Implement `GeneratePlatformDraftAsync` in `ContentPipeline`

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs`

The implementation needs an additional constructor dependency: `IOptions<ContentAutomationOptions>` to read configurable platform prompts. Store as `_automationOptions`.

**Updated constructor signature:**

```csharp
public ContentPipeline(
    IApplicationDbContext dbContext,
    ISidecarClient sidecarClient,
    IBrandVoiceService brandVoiceService,
    IWorkflowEngine workflowEngine,
    IPipelineEventBroadcaster broadcaster,
    IOptions<ContentAutomationOptions> automationOptions,
    ILogger<ContentPipeline> logger)
```

**Method logic:**

1. Load the Content entity by `contentId`. Return `NotFound` if missing.
2. Load brand voice context (reuse existing `LoadBrandContextAsync` private method).
3. Determine the system prompt:
   - Check `_automationOptions.PlatformPrompts` dictionary for the platform key (e.g., `"LinkedIn"`, `"TwitterX"`, `"PersonalBlog"`). If a custom prompt exists, use it.
   - Otherwise, use the built-in default prompt for that platform (see below).
4. Build the task prompt: include the `parentBody` as context, the brand voice context, and instructions to rewrite for the target platform.
5. Call `_sidecarClient.SendTaskAsync(taskPrompt, systemPrompt, null, ct)` and consume the event stream (reuse existing `ConsumeEventStreamAsync` pattern).
6. On success: store the generated text in `content.Body`, update `content.Metadata.TokensUsed`, call `SaveChangesAsync`.
7. Return `Result<string>.Success(generatedText)`.

### 3. Default Platform System Prompts

These prompts are the fallback when `ContentAutomationOptions.PlatformPrompts` does not have an override for the platform. They should be defined as `private static readonly` strings in `ContentPipeline`.

**LinkedIn default prompt:**
> You are a professional LinkedIn content writer for a tech thought leader. Rewrite the provided content as a LinkedIn post. Use an authoritative, insightful tone. Structure for readability with short paragraphs and line breaks. Include 3-5 relevant hashtags at the end. Maximum 3000 characters. Write in a humanized, conversational style. Never use em-dashes. Reference real experiences and practical insights.

**TwitterX default prompt:**
> You are a sharp, opinionated tech Twitter writer. Rewrite the provided content as a single tweet (max 280 characters) or a thread if the content requires it. Be punchy, direct, and credible to the dev community. No fluff, no corporate-speak. Write in a humanized, natural voice. Never use em-dashes. If creating a thread, number each tweet.

**PersonalBlog default prompt:**
> You are a blog content writer. Rewrite the provided content as a blog teaser/excerpt that drives readers to the full article. Include a compelling hook, key takeaways preview, and a call-to-action. Be SEO-conscious with natural keyword placement. Write in a humanized, conversational style. Never use em-dashes.

These prompts encode the content writing rules: humanized voice, no em-dashes, platform-authentic patterns.

### 4. Add ContentType Override to `AcceptSuggestionAsync`

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs`

Change the signature to add an optional parameter:

```csharp
Task<Result<Guid>> AcceptSuggestionAsync(
    Guid suggestionId, CancellationToken ct, ContentType? contentTypeOverride = null);
```

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendMonitor.cs`

In the `AcceptSuggestionAsync` method, change the `Content.Create` call:

```csharp
var content = Content.Create(
    contentTypeOverride ?? suggestion.SuggestedContentType,
    body: "",
    title: suggestion.Topic,
    targetPlatforms: suggestion.SuggestedPlatforms);
```

The `contentTypeOverride ?? suggestion.SuggestedContentType` pattern means:
- When the orchestrator provides a `ContentType` (from AI curation), use that.
- When called without the override (existing callers), fall back to the suggestion's default type.

This is backward compatible -- all existing callers that omit the parameter get the same behavior as before.

### 5. Existing Code That Calls AcceptSuggestionAsync

The following callers exist and must continue to compile without changes:

- `TrendEndpoints.cs` -- calls `AcceptSuggestionAsync(suggestionId, ct)` (no override, keeps default behavior)

Since the new parameter is optional with a default of `null`, no changes to existing callers are needed.

### 6. ContentPipeline Constructor Change -- Impact on Existing Tests

Adding `IOptions<ContentAutomationOptions>` to the `ContentPipeline` constructor will break the existing `ContentPipelineTests.CreatePipeline()` factory method. Update it to pass a mock `IOptions<ContentAutomationOptions>`:

```csharp
private readonly Mock<IOptions<ContentAutomationOptions>> _automationOptions = new();

// In constructor or setup:
_automationOptions.Setup(o => o.Value).Returns(new ContentAutomationOptions());
```

And pass `_automationOptions.Object` into the `ContentPipeline` constructor. The `ContentAutomationOptions` class (created in section-01) should have sensible defaults so existing tests pass without configuring platform prompts.

### 7. ContentAutomationOptions.PlatformPrompts Structure

Section-01 creates this class. For this section to work, it must have at minimum:

```csharp
public class ContentAutomationOptions
{
    // ... other properties from section-01 ...
    public Dictionary<string, string> PlatformPrompts { get; set; } = new();
}
```

The dictionary key is the `PlatformType` name as a string (e.g., `"LinkedIn"`, `"TwitterX"`, `"PersonalBlog"`). The value is the full system prompt override. When the key is absent, the default built-in prompt is used.

### 8. Configuration in appsettings.json

The platform prompts are optional overrides. The default prompts baked into the code are sufficient. Custom prompts can be provided via:

```json
{
  "ContentAutomation": {
    "PlatformPrompts": {
      "LinkedIn": "Custom LinkedIn system prompt...",
      "TwitterX": "Custom Twitter system prompt..."
    }
  }
}
```

## Key Design Decisions

1. **System prompts as configuration, not hardcoded** -- Prompts can be tuned in `appsettings.json` without redeploying. The defaults are still in code for zero-config operation.

2. **System prompt vs. task prompt** -- The platform voice instructions go in the `systemPrompt` parameter of `SendTaskAsync`. The actual content to rewrite (parent body + brand context) goes in the `task` parameter. This matches how `ISidecarClient` separates concerns: system prompt sets the persona, task prompt provides the work.

3. **Reuse of existing `ConsumeEventStreamAsync`** -- The private helper in `ContentPipeline` already handles the sidecar event stream pattern (summary extraction, error handling, token tracking). `GeneratePlatformDraftAsync` reuses this exact pattern.

4. **Optional parameter on `AcceptSuggestionAsync`** -- Adding `ContentType? contentTypeOverride = null` is the minimal interface change. All existing callers compile unchanged. The orchestrator (section-08) is the only caller that will use the override.

5. **`Content.ContentType` has `private init`** -- This is why the content type must be determined before entity creation. The override parameter on `AcceptSuggestionAsync` ensures the correct type is set at construction time. There is no setter to change it after the fact.