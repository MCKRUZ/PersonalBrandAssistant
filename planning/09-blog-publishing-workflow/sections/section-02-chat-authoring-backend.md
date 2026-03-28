# Section 02: Chat-Based Content Authoring Backend

## Overview

Implements `IBlogChatService` -- a Claude API proxy with blog-writer system prompt, SSE streaming, conversation persistence with windowing, and structured finalization extraction.

**Depends on:** Section 01 (ChatConversation entity, BlogChatOptions)
**Blocks:** Section 09 (Chat Authoring Frontend)

---

## Existing Codebase Context

- **Anthropic SDK**: Already referenced as `Anthropic` v12.8.0 in Infrastructure.csproj
- **SSE pattern**: `EventEndpoints.cs` demonstrates SSE: `text/event-stream`, `no-cache`, `keep-alive`, `X-Accel-Buffering: no`
- **Result pattern**: `Result<T>` with `Success`, `Failure`, `NotFound`, `ValidationFailure`
- **Endpoint convention**: Minimal APIs with `MapGroup`, `WithTags`
- **DI**: Services registered as Scoped in `DependencyInjection.cs`

---

## Tests (Write First)

### BlogChatService Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogChat/BlogChatServiceTests.cs`

```csharp
// Test: SendMessageAsync prepends system prompt to first message
// Test: SendMessageAsync includes conversation summary + last N messages (not full history)
// Test: SendMessageAsync persists assistant message only after stream completes
// Test: SendMessageAsync discards partial response on stream interruption
// Test: SendMessageAsync creates new ChatConversation on first message for content
// Test: SendMessageAsync appends to existing ChatConversation on subsequent messages
// Test: GetConversationAsync returns null for content with no conversation
// Test: GetConversationAsync returns full conversation with all messages
```

### Finalization Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogChat/BlogChatFinalizationTests.cs`

```csharp
// Test: ExtractFinalDraftAsync sends structured JSON extraction prompt
// Test: ExtractFinalDraftAsync validates response against schema (title, subtitle, body_markdown, seo_description, tags)
// Test: ExtractFinalDraftAsync retries with corrective prompt on validation failure
// Test: ExtractFinalDraftAsync fails after max retries (2) with invalid responses
// Test: ExtractFinalDraftAsync saves validated content to Content.Body and metadata
```

### Windowing Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogChat/ConversationWindowingTests.cs`

```csharp
// Test: Windowing keeps last N messages in full when conversation exceeds threshold
// Test: Windowing generates summary of older messages
// Test: Windowing persists both raw and summarized forms
// Test: Claude request includes [system] + [summary] + [recent messages] + [new message]
```

### Endpoint Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/BlogChatEndpointsTests.cs`

```csharp
// Test: POST /api/content/{id}/chat returns SSE stream with correct content-type
// Test: POST /api/content/{id}/chat returns 404 for non-existent content
// Test: POST /api/content/{id}/chat validates message max length
// Test: GET /api/content/{id}/chat/history returns conversation messages
// Test: GET /api/content/{id}/chat/history returns empty array for no conversation
// Test: POST /api/content/{id}/chat/finalize saves draft and returns finalized content
// Test: POST /api/content/{id}/chat/finalize returns 400 if no conversation exists
```

---

## Implementation Details

### 1. IBlogChatService Interface

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IBlogChatService.cs`

```csharp
public interface IBlogChatService
{
    IAsyncEnumerable<string> SendMessageAsync(Guid contentId, string userMessage, CancellationToken ct);
    Task<ChatConversation?> GetConversationAsync(Guid contentId, CancellationToken ct);
    Task<Result<FinalizedDraft>> ExtractFinalDraftAsync(Guid contentId, CancellationToken ct);
}

public record FinalizedDraft(string Title, string Subtitle, string BodyMarkdown, string SeoDescription, string[] Tags);
```

### 2. BlogChatService Implementation

File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BlogChatService.cs`

**Constructor**: `IAnthropicClient` (or thin wrapper), `IApplicationDbContext`, `IOptions<BlogChatOptions>`, `ILogger`

**Key patterns:**
- **System prompt**: Read from `BlogChatOptions.SystemPromptPath` at construction, incorporates matt-kruczek-blog-writer voice, humanizer rules, no em-dashes
- **Conversation windowing**: Keep last N messages (default 10) in full + periodically-updated summary. Construct Claude request as: `[system] + [summary] + [last N messages] + [new message]`
- **Stream-then-persist**: Add user message immediately, start streaming, yield text deltas, persist assistant message ONLY after stream completes. On interruption, discard partial response.
- **Finalization**: Send structured extraction prompt requesting JSON `{ title, subtitle, body_markdown, seo_description, tags }`. Validate schema. Retry up to 2 times with corrective prompt on failure. Save to `Content.Body`, `Content.Title`, and `Content.Metadata`.

### 3. API Endpoints

File: `src/PersonalBrandAssistant.Api/Endpoints/BlogChatEndpoints.cs`

```
POST /api/content/{id}/chat          → SSE stream (follow EventEndpoints.cs pattern)
GET  /api/content/{id}/chat/history  → JSON array of messages
POST /api/content/{id}/chat/finalize → Extract final draft, save to content
```

- POST chat: Validate content exists + is BlogPost + message length. Set SSE headers. Stream via `IAsyncEnumerable`. Write `event: done` on completion, `event: error` on failure.
- GET history: Return messages array or empty array
- POST finalize: Validate conversation exists. Return `FinalizedDraft` on success, 400 on failure.

### 4. System Prompt File

File: `src/PersonalBrandAssistant.Api/prompts/blog-writer-system.md`

Contains Matt Kruczek's voice, enterprise AI thought leadership, humanizer rules, no em-dashes, content structure patterns. Loaded at runtime.

### 5. Registration

- DI: `services.Configure<BlogChatOptions>(...)` + `services.AddScoped<IBlogChatService, BlogChatService>()`
- Endpoints: `app.MapBlogChatEndpoints()`

---

## Files

| File | Action |
|------|--------|
| `Application/Common/Interfaces/IBlogChatService.cs` | Create |
| `Infrastructure/Services/ContentServices/BlogChatService.cs` | Create |
| `Api/Endpoints/BlogChatEndpoints.cs` | Create |
| `Api/prompts/blog-writer-system.md` | Create |
| `Infrastructure/DependencyInjection.cs` | Modify (register) |
| `Api/Program.cs` | Modify (map endpoints) |

---

## Implementation Notes (Actual)

**Status:** COMPLETE
**Tests:** 12 passing (5 service + 5 finalization + 2 windowing)
**Test files:**
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogChat/BlogChatServiceTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogChat/BlogChatFinalizationTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogChat/ConversationWindowingTests.cs`

**Deviations from plan:**
- Used raw HttpClient + REST API instead of Anthropic SDK directly (isolated behind IClaudeChatClient interface for easy swap)
- Added IClaudeChatClient wrapper interface not in original plan (improves testability)
- JSON column mutations require list reassignment + MarkMessagesModified for EF Core change tracking
- Endpoint tests deferred (require CustomWebApplicationFactory setup)
