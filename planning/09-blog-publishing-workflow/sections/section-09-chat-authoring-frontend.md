# Section 09: Chat Authoring Frontend

## Overview

Angular standalone component for blog post authoring via conversational Claude interaction. Displays message list with sanitized markdown, streams SSE responses, and provides finalization to extract structured draft.

**Depends on:** Section 02 (Chat API endpoints)
**Blocks:** Section 13 (Pipeline Integration)

---

## Tests (Write First)

File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/blog-chat/blog-chat.component.spec.ts`

```typescript
// Test: renders message list with user and assistant messages
// Test: sanitizes markdown rendering (DOMPurify prevents XSS)
// Test: sends message via input field and send button
// Test: consumes SSE stream and displays streaming response
// Test: shows typing indicator during Claude response
// Test: "Finalize Draft" button calls finalize endpoint
// Test: "Finalize Draft" button disabled during active chat
// Test: displays error state on stream failure
// Test: message input disabled during streaming
```

---

## Implementation Details

### Component
File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/blog-chat/blog-chat.component.ts`

Angular standalone component with:
- **Input**: `contentId: string` (the content being authored)
- **Message list**: Scrollable container rendering user + assistant messages. Assistant messages rendered as markdown using a sanitized renderer (Angular's `DomSanitizer` or a markdown pipe with DOMPurify).
- **Input field**: Text area with send button. Disabled during streaming.
- **SSE consumption**: Use `EventSource` or `fetch()` with `ReadableStream` to consume the `POST /api/content/{id}/chat` SSE stream. Accumulate text chunks into the current assistant message in real-time.
- **Typing indicator**: Show animated dots while waiting for/during Claude response.
- **Finalize button**: Calls `POST /api/content/{id}/chat/finalize`. On success, emits `(finalized)` output event with the `FinalizedDraft` data. Parent component handles navigation to next pipeline step.
- **Error handling**: Display error toast/inline message on stream failure. Allow retry.

### Service
File: `src/PersonalBrandAssistant.Web/src/app/features/content/services/blog-chat.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class BlogChatService {
    sendMessage(contentId: string, message: string): Observable<string>; // SSE stream chunks
    getHistory(contentId: string): Observable<ChatMessage[]>;
    finalize(contentId: string): Observable<FinalizedDraft>;
}
```

SSE streaming: Use `fetch()` with `getReader()` to parse the SSE text/event-stream. Map `data:` lines to Observable emissions. Complete on `event: done`. Error on `event: error`.

### Models
File: `src/PersonalBrandAssistant.Web/src/app/features/content/models/blog-chat.models.ts`

```typescript
export interface ChatMessage { role: 'user' | 'assistant'; content: string; timestamp: string; }
export interface FinalizedDraft { title: string; subtitle: string; bodyMarkdown: string; seoDescription: string; tags: string[]; }
```

### Markdown Rendering

Use `marked` or `ngx-markdown` library with sanitization. Configure to strip raw HTML. If using `marked`, pipe through DOMPurify before rendering with `[innerHTML]`.

---

## Files
| File | Action |
|------|--------|
| `Web/src/app/features/content/components/blog-chat/blog-chat.component.ts` | Create |
| `Web/src/app/features/content/components/blog-chat/blog-chat.component.html` | Create |
| `Web/src/app/features/content/components/blog-chat/blog-chat.component.scss` | Create |
| `Web/src/app/features/content/services/blog-chat.service.ts` | Create |
| `Web/src/app/features/content/models/blog-chat.models.ts` | Create |
