# Section 05: MCP Content Pipeline Tools

## Overview

An MCP tool class `ContentPipelineTools` exposing four tools that wrap the existing content pipeline and content management services. These tools allow Jarvis (via OpenClaw Gateway) to create content, check pipeline status, publish approved content, and list drafts through voice commands or chat.

All write operations respect the autonomy dial -- if the autonomy level requires manual approval, the tool queues the action and returns a pending status rather than executing immediately.

## Dependencies

- **section-04-mcp-server-infrastructure**: The MCP server infrastructure must be in place. Tool classes are discovered via `[McpServerToolType]` assembly scanning. Tool methods follow the patterns defined in section 04.
- **section-09-mcp-idempotency-audit** (downstream): Write tools in this section (`pba_create_content`, `pba_publish_content`) will gain idempotency and audit trail support in section 09. The initial implementation here does not include `clientRequestId` or audit logging -- those are cross-cutting concerns added later.

## Existing Services

The tools wrap these existing application-layer interfaces:

- `IContentPipeline` -- `CreateFromTopicAsync`, `GenerateOutlineAsync`, `GenerateDraftAsync`, `ValidateVoiceAsync`, `SubmitForReviewAsync`
- `IApplicationDbContext` -- Direct queries for listing content, checking status
- `IContentCalendarService` -- For scheduling-related queries (used by calendar tools, but referenced here for pipeline status)
- `AutonomyConfiguration` -- Entity loaded from `IApplicationDbContext.AutonomyConfigurations` to check the current autonomy level

The existing `ContentCreationRequest` model is used by `CreateFromTopicAsync`:

```csharp
// Located in PersonalBrandAssistant.Application.Common.Models
public record ContentCreationRequest(
    string Topic,
    ContentType ContentType,
    PlatformType[] TargetPlatforms,
    string? Title = null);
```

Content statuses follow this pipeline: `Draft > Review > Approved > Scheduled > Publishing > Published`. Failed and Archived are terminal/error states.

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/ContentPipelineToolsTests.cs`

Use xUnit + Moq. Mock `IContentPipeline`, `IApplicationDbContext`, and set up `AutonomyConfiguration` in the in-memory DbSet.

```csharp
// --- pba_create_content ---

// Test: creates content item and returns ID when autonomy allows
//   Set autonomy to AutonomyLevel.FullAuto
//   Call pba_create_content with valid topic, platform, contentType
//   Assert IContentPipeline.CreateFromTopicAsync was called
//   Assert result JSON contains the new content ID and status "Draft"

// Test: queues for approval when autonomy is manual
//   Set autonomy to AutonomyLevel.Manual
//   Call pba_create_content
//   Assert result JSON contains status "queued-for-approval"
//   Assert IContentPipeline.CreateFromTopicAsync was still called (draft created)
//   but the content is not auto-progressed through the pipeline

// Test: validates platform is a known enum value
//   Call with platform = "InvalidPlatform"
//   Assert result JSON contains an error message about invalid platform

// Test: validates contentType is a known enum value
//   Call with contentType = "InvalidType"
//   Assert result JSON contains an error message about invalid content type


// --- pba_get_pipeline_status ---

// Test: returns all active items when no contentId specified
//   Seed 5 content items in various non-terminal statuses
//   Call pba_get_pipeline_status(contentId: null)
//   Assert result JSON contains 5 items with correct statuses

// Test: returns specific item when contentId provided
//   Seed 5 content items
//   Call pba_get_pipeline_status(contentId: specificGuid)
//   Assert result JSON contains exactly 1 item matching the ID

// Test: returns empty when contentId not found
//   Call with a non-existent GUID
//   Assert result JSON indicates not found or empty result


// --- pba_publish_content ---

// Test: succeeds for approved content
//   Seed content in Approved status
//   Call pba_publish_content
//   Assert content transitions toward Publishing/Published
//   Assert result JSON indicates success

// Test: fails for content not in Approved state
//   Seed content in Draft status
//   Call pba_publish_content
//   Assert result JSON contains error "content must be in Approved state"

// Test: fails for content that hasn't passed voice check
//   Seed content in Review status (voice check not yet done)
//   Call pba_publish_content
//   Assert result JSON contains error about voice check requirement


// --- pba_list_drafts ---

// Test: filters by status
//   Seed content in Draft, Review, and Approved statuses
//   Call pba_list_drafts(status: "Draft")
//   Assert result contains only Draft items

// Test: filters by platform
//   Seed content for LinkedIn and Twitter
//   Call pba_list_drafts(platform: "LinkedIn")
//   Assert result contains only LinkedIn items

// Test: returns all drafts when no filters
//   Seed content in various statuses
//   Call pba_list_drafts without filters
//   Assert result contains all non-terminal items
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Api/McpTools/ContentPipelineTools.cs` -- The tool class with 4 MCP tools.
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/ContentPipelineToolsTests.cs` -- Tests.

## Tool Definitions

### pba_create_content

```csharp
[McpServerTool]
[Description("Creates new content in the PBA pipeline from a topic. Use when asked to 'write a post', 'create content about X', or 'draft something for LinkedIn'. Returns the new content ID and initial status. If autonomy is set to manual, creates a draft for approval rather than auto-publishing.")]
public static async Task<string> pba_create_content(
    IServiceProvider serviceProvider,
    [Description("The topic or subject to create content about")] string topic,
    [Description("Target platform: Twitter, LinkedIn, Reddit, or Blog")] string platform,
    [Description("Content type: Post, Article, Thread, or Comment")] string contentType,
    CancellationToken ct)
```

Implementation logic:
1. Parse `platform` to `PlatformType` enum and `contentType` to `ContentType` enum. Return a validation error JSON if parsing fails.
2. Resolve `IContentPipeline` and `IApplicationDbContext` from a new scope.
3. Load `AutonomyConfiguration` from the database.
4. Call `pipeline.CreateFromTopicAsync()` with a `ContentCreationRequest`.
5. If autonomy level is `FullAuto` or `SemiAuto`, return the content ID and status.
6. If autonomy level is `Manual`, return the content ID with status "queued-for-approval" and a message indicating the draft needs manual review.
7. Serialize the response as JSON and return.

### pba_get_pipeline_status

```csharp
[McpServerTool]
[Description("Returns pipeline status for a specific content item or lists all active items with their current stage. Use when asked 'what's in the pipeline', 'status of my post', or 'show content progress'. Returns content ID, title, platform, stage, content type, and last updated time.")]
public static async Task<string> pba_get_pipeline_status(
    IServiceProvider serviceProvider,
    [Description("Optional content ID to check a specific item. Omit to list all active items.")] string? contentId,
    CancellationToken ct)
```

Implementation logic:
1. Resolve `IApplicationDbContext` from a new scope.
2. If `contentId` is provided, parse to GUID and query for the specific content item.
3. If `contentId` is null, query all content items where `Status` is not Published, Archived, or Failed.
4. Project results into a response shape with: contentId, title, platform(s), current stage (Status enum name), contentType, updatedAt.
5. Use `AsNoTracking()` for read performance.
6. Serialize and return.

### pba_publish_content

```csharp
[McpServerTool]
[Description("Publishes approved content to its target platform. Use when asked to 'publish my post', 'push the LinkedIn article live', or 'send it out'. Content must be in Approved state and must have passed voice check. Returns success or error with reason.")]
public static async Task<string> pba_publish_content(
    IServiceProvider serviceProvider,
    [Description("The content ID to publish (GUID format)")] string contentId,
    CancellationToken ct)
```

Implementation logic:
1. Parse `contentId` to GUID. Return error if invalid.
2. Resolve services from a new scope.
3. Load the content item. Return not-found error if missing.
4. Validate the content is in `Approved` status. Return error with current status if not.
5. Check that voice validation has been performed (check for a `BrandVoiceScore` or a workflow transition log indicating voice check passed). Return error if not.
6. Transition content to `Scheduled` then `Publishing` status via `Content.TransitionTo()`.
7. Trigger the publishing pipeline.
8. Return success with the content ID and new status.

### pba_list_drafts

```csharp
[McpServerTool]
[Description("Lists content drafts with optional filtering. Use when asked 'show my drafts', 'what posts are pending', or 'list LinkedIn content'. Returns content items with their ID, title, status, platform, content type, and creation date.")]
public static async Task<string> pba_list_drafts(
    IServiceProvider serviceProvider,
    [Description("Optional status filter: Draft, Review, Approved, Scheduled. Omit for all non-terminal.")] string? status,
    [Description("Optional platform filter: Twitter, LinkedIn, Reddit, Blog. Omit for all platforms.")] string? platform,
    CancellationToken ct)
```

Implementation logic:
1. Resolve `IApplicationDbContext` from a new scope.
2. Start with all content items, apply `AsNoTracking()`.
3. If `status` is provided, parse to `ContentStatus` and filter. If invalid, return validation error.
4. If `status` is not provided, filter to non-terminal statuses (Draft, Review, Approved, Scheduled).
5. If `platform` is provided, parse to `PlatformType` and filter where `TargetPlatforms` contains it.
6. Order by `CreatedAt` descending.
7. Project to response shape: contentId, title, status, platforms, contentType, createdAt, updatedAt.
8. Serialize and return.

## Autonomy Integration

The `AutonomyConfiguration` entity stores the current autonomy settings. It has a `ResolveLevel(ContentType, PlatformType?)` method that returns the effective `AutonomyLevel` considering global, platform, and content-type overrides.

Autonomy levels:
- `FullAuto` -- Execute immediately, no approval needed
- `SemiAuto` -- Execute but notify for review
- `Manual` -- Queue for approval, do not execute

For `pba_create_content`, the autonomy check determines whether the tool just creates a draft (Manual) or also progresses the content through the pipeline stages automatically (FullAuto/SemiAuto).

For `pba_publish_content`, the tool always requires the content to be in `Approved` state regardless of autonomy level. The autonomy dial affects how content gets TO the Approved state (auto vs manual review), not the publish action itself.

## Response Serialization

All tools return JSON strings. Use `System.Text.Json.JsonSerializer.Serialize()` with camelCase naming policy to match the API's JSON conventions:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};
```

Error responses follow a consistent shape:

```json
{
  "error": true,
  "message": "Content must be in Approved state to publish. Current state: Draft"
}
```

Success responses include the relevant data:

```json
{
  "contentId": "guid",
  "status": "Draft",
  "title": "AI Trends in 2026",
  "message": "Content created successfully"
}
```

## Implementation Notes

- Each tool method creates its own DI scope: `using var scope = serviceProvider.CreateScope();`. This ensures `IApplicationDbContext` and other scoped services have proper lifetimes.
- Tool methods are `static` -- they receive services via the `IServiceProvider` parameter, not via constructor injection.
- Enum parsing should use `Enum.TryParse<T>` with `ignoreCase: true` to be forgiving of casing from LLM-generated tool calls.
- The `pba_get_pipeline_status` tool returning all active items should limit to a reasonable count (e.g., 50) to avoid overwhelming the LLM context window.
- Content items have `TargetPlatforms` as a `PlatformType[]`, so platform filtering uses `Any()` on that array.
