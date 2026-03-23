# Section 08: MCP Social Engagement Tools

## Overview

An MCP tool class `SocialEngagementTools` exposing three tools that wrap the existing social engagement service. These tools allow Jarvis (via OpenClaw Gateway) to discover engagement opportunities, respond to social mentions and comments, and browse the social inbox through voice commands or chat.

Two of the three tools are read-only (`pba_get_opportunities`, `pba_get_inbox`). The write tool (`pba_respond_to_opportunity`) respects the autonomy dial -- if the autonomy level requires manual approval, the tool queues the response for review rather than sending it immediately. When `responseText` is omitted, the tool uses Claude to auto-generate a contextually appropriate response before sending or queuing.

## Dependencies

- **section-04-mcp-server-infrastructure**: The MCP server infrastructure must be in place. Tool classes are discovered via `[McpServerToolType]` assembly scanning. Tool methods follow the patterns defined in section 04.
- **section-09-mcp-idempotency-audit** (downstream): The write tool `pba_respond_to_opportunity` will gain idempotency and audit trail support in section 09. The initial implementation here does not include `clientRequestId` or audit logging -- those are cross-cutting concerns added later.

## Existing Services

The tools wrap these existing application-layer interfaces:

- `ISocialEngagementService` -- The primary service for engagement operations. Provides methods for fetching opportunities, responding to them, and managing the social inbox.
- `ISocialEngagementAdapter` -- Platform-specific adapters (Twitter, LinkedIn, Reddit) that the engagement service delegates to for actual API calls.
- `IApplicationDbContext` -- Direct queries for inbox items, engagement opportunities.
- `AutonomyConfiguration` -- Entity loaded from `IApplicationDbContext.AutonomyConfigurations` to check the current autonomy level for write operations.

Key domain models:

```csharp
// EngagementOpportunity entity has:
//   Guid Id, string AuthorName, string Content, PlatformType Platform,
//   EngagementOpportunityType Type (Reply, Mention, ThreadJoin),
//   double RelevanceScore, DateTimeOffset DiscoveredAt,
//   EngagementOpportunityStatus Status (New, Responded, Dismissed, Expired)

// InboxItem entity has:
//   Guid Id, PlatformType Platform, string AuthorName, string Content,
//   InboxItemType Type (Mention, DirectMessage, Comment, Reply),
//   bool IsRead, DateTimeOffset ReceivedAt
```

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/SocialEngagementToolsTests.cs`

Use xUnit + Moq. Mock `ISocialEngagementService`, `IApplicationDbContext`, and set up `AutonomyConfiguration` in the in-memory DbSet.

```csharp
// --- pba_get_opportunities ---

// Test: returns ranked opportunities
//   Mock ISocialEngagementService to return 5 opportunities with varying relevance scores
//   Call pba_get_opportunities without filters
//   Assert result JSON contains 5 items ordered by relevanceScore descending

// Test: filters by platform
//   Mock to return opportunities for Twitter, LinkedIn, Reddit
//   Call pba_get_opportunities(platform: "LinkedIn")
//   Assert result contains only LinkedIn opportunities

// Test: respects limit
//   Mock to return 10 opportunities
//   Call pba_get_opportunities(limit: 3)
//   Assert result JSON contains 3 items

// Test: returns empty when no opportunities available
//   Mock to return empty list
//   Assert result JSON contains empty array and count of 0

// Test: defaults limit when not specified
//   Call pba_get_opportunities without limit
//   Assert service was called with a sensible default (e.g., 10)


// --- pba_respond_to_opportunity ---

// Test: with responseText sends directly when autonomy allows
//   Set autonomy to AutonomyLevel.FullAuto
//   Call pba_respond_to_opportunity(opportunityId, responseText: "Great insight!")
//   Assert ISocialEngagementService response method was called
//   Assert result JSON contains status "sent"

// Test: without responseText generates response via Claude
//   Set autonomy to AutonomyLevel.FullAuto
//   Call pba_respond_to_opportunity(opportunityId, responseText: null)
//   Assert the service's auto-generate path was invoked
//   Assert result JSON contains the generated response text and status "sent"

// Test: queues when autonomy is manual
//   Set autonomy to AutonomyLevel.Manual
//   Call pba_respond_to_opportunity with valid opportunityId and responseText
//   Assert result JSON contains status "queued-for-approval"
//   Assert the response was NOT sent to the platform

// Test: returns error for non-existent opportunity
//   Call with a GUID that does not exist
//   Assert result JSON contains not-found error

// Test: returns error for already-responded opportunity
//   Seed opportunity with Status = Responded
//   Call pba_respond_to_opportunity
//   Assert result JSON contains error about opportunity already handled


// --- pba_get_inbox ---

// Test: returns items filtered by platform
//   Seed inbox items for Twitter, LinkedIn, Reddit
//   Call pba_get_inbox(platform: "Twitter")
//   Assert result contains only Twitter items

// Test: filters unread only
//   Seed 3 read and 2 unread inbox items
//   Call pba_get_inbox(unreadOnly: true)
//   Assert result contains only the 2 unread items

// Test: returns all items when no filters
//   Seed 5 inbox items across platforms, mix of read/unread
//   Call pba_get_inbox without filters
//   Assert result contains all 5 items

// Test: orders by receivedAt descending (newest first)
//   Seed items with different timestamps
//   Assert result JSON items are ordered newest to oldest

// Test: returns empty when inbox is empty
//   Assert result JSON contains empty array and count of 0
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Api/McpTools/SocialEngagementTools.cs` -- The tool class with 3 MCP tools.
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/SocialEngagementToolsTests.cs` -- Tests.

## Tool Definitions

### pba_get_opportunities

```csharp
[McpServerTool]
[Description("Returns engagement opportunities (comments to reply to, mentions, conversation threads) ranked by relevance and recency. Use when asked 'any comments to reply to', 'show engagement opportunities', 'what should I respond to', or 'check my mentions'. Returns opportunities with author, content preview, platform, relevance score, and type.")]
public static async Task<string> pba_get_opportunities(
    IServiceProvider serviceProvider,
    [Description("Optional platform filter: Twitter, LinkedIn, Reddit. Omit for all platforms.")] string? platform,
    [Description("Maximum number of opportunities to return (default: 10, max: 25)")] int? limit,
    CancellationToken ct)
```

Implementation logic:
1. Resolve `ISocialEngagementService` and `IApplicationDbContext` from a new DI scope.
2. If `platform` is provided, parse to `PlatformType` using `Enum.TryParse` with `ignoreCase: true`. Return validation error if parsing fails.
3. Clamp the limit: `Math.Clamp(limit ?? 10, 1, 25)`.
4. Query engagement opportunities from the database:
   - Filter to `Status == New` (not yet responded, dismissed, or expired).
   - If platform is specified, filter to that platform.
   - Order by `RelevanceScore` descending, then by `DiscoveredAt` descending.
   - Take the clamped limit.
   - Use `AsNoTracking()` for read performance.
5. Project each opportunity to a response shape: opportunityId, authorName, contentPreview (truncate `Content` to ~200 chars), platform, type (Reply/Mention/ThreadJoin), relevanceScore, discoveredAt.
6. Serialize and return.

Response shape:

```json
{
  "opportunities": [
    {
      "opportunityId": "guid",
      "authorName": "techleader42",
      "contentPreview": "Really interesting take on AI agents in the enterprise...",
      "platform": "Twitter",
      "type": "Mention",
      "relevanceScore": 0.89,
      "discoveredAt": "2026-03-23T10:30:00Z"
    }
  ],
  "count": 5,
  "asOf": "2026-03-23T14:00:00Z"
}
```

### pba_respond_to_opportunity

```csharp
[McpServerTool]
[Description("Drafts or sends a response to an engagement opportunity. Use when asked to 'reply to that comment', 'respond to the mention', or 'engage with this thread'. If responseText is not provided, uses Claude to generate a contextually appropriate response. Autonomy dial determines whether the response is sent immediately or queued for approval. Returns the response text and send status.")]
public static async Task<string> pba_respond_to_opportunity(
    IServiceProvider serviceProvider,
    [Description("The engagement opportunity ID to respond to (GUID format)")] string opportunityId,
    [Description("Optional response text. If omitted, Claude generates a response automatically.")] string? responseText,
    CancellationToken ct)
```

Implementation logic:
1. Parse `opportunityId` to GUID. Return validation error if invalid.
2. Resolve `ISocialEngagementService`, `IApplicationDbContext` from a new DI scope.
3. Load the engagement opportunity by ID. Return not-found error if missing.
4. Validate the opportunity status is `New`. Return error if already `Responded`, `Dismissed`, or `Expired`.
5. Load `AutonomyConfiguration` from the database.
6. If `responseText` is null, invoke the service's auto-generate path which uses Claude to craft a response based on the opportunity's content and context. Capture the generated text.
7. Check the autonomy level via `AutonomyConfiguration.ResolveLevel()`:
   - `FullAuto` or `SemiAuto`: Call the service to send the response to the platform. Update the opportunity status to `Responded`. Return the response text and status "sent".
   - `Manual`: Save the response as a draft on the opportunity without sending. Return the response text and status "queued-for-approval" with a message indicating the response needs manual review.
8. Serialize and return.

Response shape (auto-send):

```json
{
  "opportunityId": "guid",
  "responseText": "Thanks for the mention! AI agents are...",
  "status": "sent",
  "platform": "Twitter",
  "message": "Response sent successfully"
}
```

Response shape (queued):

```json
{
  "opportunityId": "guid",
  "responseText": "Thanks for the mention! AI agents are...",
  "status": "queued-for-approval",
  "platform": "Twitter",
  "message": "Response drafted and queued for your approval"
}
```

### pba_get_inbox

```csharp
[McpServerTool]
[Description("Returns social inbox items (mentions, DMs, comments, replies) with filtering options. Use when asked 'check my inbox', 'any new messages', 'show unread mentions', or 'what did I miss on LinkedIn'. Returns items with author, content, platform, type, read status, and timestamp.")]
public static async Task<string> pba_get_inbox(
    IServiceProvider serviceProvider,
    [Description("Optional platform filter: Twitter, LinkedIn, Reddit. Omit for all platforms.")] string? platform,
    [Description("If true, returns only unread items. Default: false (all items).")] bool? unreadOnly,
    CancellationToken ct)
```

Implementation logic:
1. Resolve `IApplicationDbContext` from a new DI scope.
2. If `platform` is provided, parse to `PlatformType` using `Enum.TryParse` with `ignoreCase: true`. Return validation error if parsing fails.
3. Start with all inbox items, apply `AsNoTracking()`.
4. If `platform` is specified, filter to that platform.
5. If `unreadOnly` is true, filter to `IsRead == false`.
6. Order by `ReceivedAt` descending (newest first).
7. Limit to 50 items to avoid overwhelming the LLM context window.
8. Project to response shape: inboxItemId, authorName, contentPreview (truncate `Content` to ~200 chars), platform, type (Mention/DirectMessage/Comment/Reply), isRead, receivedAt.
9. Serialize and return.

Response shape:

```json
{
  "items": [
    {
      "inboxItemId": "guid",
      "authorName": "devconnector",
      "contentPreview": "Loved your article on content automation...",
      "platform": "LinkedIn",
      "type": "Comment",
      "isRead": false,
      "receivedAt": "2026-03-23T09:15:00Z"
    }
  ],
  "count": 12,
  "unreadCount": 5,
  "asOf": "2026-03-23T14:00:00Z"
}
```

## Autonomy Integration

The `AutonomyConfiguration` entity stores the current autonomy settings. It has a `ResolveLevel(ContentType, PlatformType?)` method that returns the effective `AutonomyLevel` considering global, platform, and content-type overrides.

Autonomy levels:
- `FullAuto` -- Execute immediately, no approval needed
- `SemiAuto` -- Execute but notify for review
- `Manual` -- Queue for approval, do not execute

For `pba_respond_to_opportunity`, the autonomy check determines whether the response is sent to the platform immediately (FullAuto/SemiAuto) or saved as a draft pending manual review (Manual). Both read tools (`pba_get_opportunities`, `pba_get_inbox`) do not require autonomy checks.

## Response Serialization

Same pattern as sections 05, 06, and 07 -- all tools return JSON strings using `System.Text.Json.JsonSerializer` with camelCase naming policy.

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};
```

Error responses follow the consistent shape:

```json
{
  "error": true,
  "message": "Description of what went wrong"
}
```

## Implementation Notes

- Each tool method creates its own DI scope: `using var scope = serviceProvider.CreateScope();`. This ensures scoped services have proper lifetimes.
- Tool methods are `static` -- they receive services via the `IServiceProvider` parameter, not via constructor injection.
- Enum parsing should use `Enum.TryParse<T>` with `ignoreCase: true` to be forgiving of casing from LLM-generated tool calls.
- Content preview truncation should use a helper that truncates at word boundaries and appends "..." when the content exceeds the limit.
- The `pba_get_inbox` tool returns both total `count` and `unreadCount` so the LLM can report both to the user (e.g., "You have 12 inbox items, 5 unread").
- The `pba_respond_to_opportunity` auto-generate path should pass the opportunity's content, author, platform, and type to the Claude-based response generator so the response is contextually appropriate and platform-appropriate in tone/length.
- Opportunity relevance scoring is handled upstream by the social engagement service during discovery. The MCP tool simply surfaces the pre-ranked results.
