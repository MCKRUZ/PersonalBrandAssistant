# Section 16: Sidecar Chat Panel

## Overview

This section builds the `SidecarChatComponent` -- a PrimeNG Sidebar-based chat panel that integrates with SignalR for real-time AI streaming. The panel slides in from the right side of the content editor, allowing the user to have a conversation with the AI about the content being edited. Tokens stream in via SignalR, and the user can apply AI-generated text to the editor or copy it.

The component is embedded inside the `ContentEditorComponent` (section 15) and communicates with the `ContentEditorStore` (section 13) for state management and the `SignalRService` (section 12) for SignalR transport.

## Dependencies

- **Section 08 (SignalR Hub):** Backend `ContentHub` at `/hubs/content` exposing `SendChatMessage`, `ReceiveToken`, `GenerationComplete`, `GenerationError`.
- **Section 12 (Angular Models and Services):** `SignalRService` with `connect()`, `disconnect()`, `sendChatMessage()`, `tokens$`, `generationComplete$`, `generationError$`. Also `ContentDetail` interface.
- **Section 13 (Angular Stores):** `ContentEditorStore` with `content`, `chatMessages`, `isStreaming`, `currentTokens`, `addChatMessage()`, `appendToken()`, `completeGeneration()`, `applyToEditor()`.
- **Section 15 (Content Editor Page):** `ContentEditorComponent` hosts this component. The editor component provides the content ID and controls sidebar visibility.

## Implementation Notes (Actual)

### Files Created
```
content-editor/
  sidecar-chat/
    sidecar-chat.component.ts              (inline template+styles, 341 lines)
    sidecar-chat.component.spec.ts         (15 tests)
```

### Deviations from Plan
1. **PrimeNG Drawer, not Sidebar** — PrimeNG v20 renamed Sidebar to Drawer. Uses `DrawerModule` from `primeng/drawer`.
2. **TextareaModule, not InputTextarea** — PrimeNG v20 uses `TextareaModule` from `primeng/textarea`, directive `pTextarea`.
3. **Component placed in subdirectory** — `sidecar-chat/` rather than flat in `content-editor/`, matching the pattern for editor-toolbar and markdown-editor.
4. **No separate .html template** — inline template, matching codebase pattern.
5. **store.addChatMessage(text: string)** — the actual store takes a plain string, not a `{ role, content, timestamp }` object as the plan described. The store constructs the ChatMessage internally.
6. **No retry UI for connection failure** — added `.catch()` on connect() that surfaces error via store, but no retry button. Deferred to backend integration.
7. **No Ctrl+Shift+C keyboard shortcut** — the plan mentioned a HostListener shortcut on ContentEditorComponent. Not implemented, toggle button is sufficient for now.

### Code Review Fixes Applied
- stopGeneration() made async with proper await on disconnect/connect
- connect() fire-and-forget fixed with .catch error handling
- sendChatMessage() unhandled promise fixed with .catch error handling
- Duplicate test removed, stopGeneration test added, generationError$ test added

### Test Coverage
- **15 test cases**: creation, send message, clear input, empty guard, streaming guard, streaming area, skeleton shimmer, action buttons, apply to editor, draft chips, refine chips, Enter/Shift+Enter, stopGeneration, generationError$
- Content editor tests updated with mock SignalRService
- **Total suite**: 225 tests, all passing

## Files to Create (Original Plan)

| File | Description |
|------|-------------|
| `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat.component.ts` | Chat panel component with template and styles |
| `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat.component.html` | Template (optional -- may inline) |
| `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat.component.spec.ts` | Component tests |

## Files to Modify

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.ts` | Add SidecarChatComponent to imports, wire toggle button and sidebar visibility |

---

## Tests First

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat.component.spec.ts`

### Test: Sends message via SignalR on submit

- Arrange: Create component, provide mock `SignalRService` with `sendChatMessage` spy. Provide mock `ContentEditorStore` with content signal returning `{ id: 'content-1' }`. Set input value to "Refine the intro".
- Act: Trigger send (Enter key or send button click).
- Assert: `signalRService.sendChatMessage` called with `('content-1', 'Refine the intro')`. `contentEditorStore.addChatMessage` called with user message.

### Test: Displays streaming tokens

- Arrange: Component initialized. `contentEditorStore.currentTokens` returns `'Hello world'`. `contentEditorStore.isStreaming` returns `true`.
- Act: Fixture detect changes.
- Assert: Streaming message area displays "Hello world". Skeleton shimmer not visible (tokens have arrived).

### Test: Shows action buttons on generation complete

- Arrange: `contentEditorStore.chatMessages` returns array with one assistant message `{ role: 'assistant', content: 'Generated text', timestamp: '...' }`. `contentEditorStore.isStreaming` returns `false`.
- Act: Fixture detect changes.
- Assert: "Apply to Editor" button visible on the assistant message. "Copy" button visible on the assistant message.

### Test: Apply to Editor updates editor content

- Arrange: Component with completed assistant message containing "New body text".
- Act: Click "Apply to Editor" button.
- Assert: `contentEditorStore.applyToEditor` called with `'New body text'`.

### Test: Quick action chips adapt to editor state

- Arrange (empty editor): `contentEditorStore.content` returns content with empty body.
- Assert: Chips include "Draft from idea", "Draft from scratch".
- Arrange (editor has content): `contentEditorStore.content` returns content with non-empty body.
- Assert: Chips include "Refine", "Shorten", "Expand", "Change tone".

### Test: Stop button cancels generation

- Arrange: `contentEditorStore.isStreaming` returns `true`.
- Act: Click stop button.
- Assert: `signalRService.disconnect()` called (or a dedicated cancel mechanism). Streaming UI resets.

### Test: Input disabled during streaming

- Arrange: `contentEditorStore.isStreaming` returns `true`.
- Assert: Textarea is disabled. Send button is disabled.

### Test: Enter sends message, Shift+Enter adds newline

- Arrange: Component with input focused.
- Act: Press Enter key.
- Assert: Message sent (sendMessage called).
- Act: Press Shift+Enter.
- Assert: Newline added to input, message NOT sent.

### Test: Displays skeleton shimmer before first token

- Arrange: `contentEditorStore.isStreaming` returns `true`. `contentEditorStore.currentTokens` returns `''` (empty).
- Assert: Skeleton shimmer element (3-5 animated lines) is visible.

### Test: AI messages rendered as markdown

- Arrange: `contentEditorStore.chatMessages` includes assistant message with markdown content (`**bold** text`).
- Assert: Message content rendered via `ngx-markdown` (look for rendered `<strong>` tag or markdown component).

---

## Implementation Details

### SidecarChatComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat.component.ts`

Standalone Angular component using PrimeNG Sidebar.

**Inputs:**
- `visible: InputSignal<boolean>` -- controls sidebar open/close (two-way binding with parent)
- `contentId: InputSignal<string>` -- ID of the content being edited

**Outputs:**
- `visibleChange: OutputEmitter<boolean>` -- emits when sidebar closes

**Injected dependencies:**
- `ContentEditorStore` -- component-scoped (provided by parent `ContentEditorComponent`)
- `SignalRService` -- root-scoped
- `DestroyRef` -- for cleanup

**State (local signals):**
- `inputMessage = signal('')` -- current input text
- `messagesEnd` -- ViewChild reference to scroll anchor

**Lifecycle:**

`OnInit`:
1. Call `signalRService.connect()` to establish hub connection
2. Subscribe to `signalRService.tokens$` -- on each token, call `store.appendToken(token)`
3. Subscribe to `signalRService.generationComplete$` -- on complete, call `store.completeGeneration(fullText)`
4. Subscribe to `signalRService.generationError$` -- on error, add error message to chat
5. All subscriptions cleaned up via `takeUntilDestroyed(this.destroyRef)`

`OnDestroy`:
1. Call `signalRService.disconnect()`

**Methods:**

`sendMessage()`:
1. Guard: return early if `inputMessage()` is empty or whitespace, or if `store.isStreaming()` is true
2. Call `store.addChatMessage({ role: 'user', content: inputMessage(), timestamp: new Date().toISOString() })`
3. Call `signalRService.sendChatMessage(contentId(), inputMessage())`
4. Clear `inputMessage` signal

`onKeydown(event: KeyboardEvent)`:
1. If `event.key === 'Enter' && !event.shiftKey`: prevent default, call `sendMessage()`
2. Shift+Enter: allow default (newline)

`applyToEditor(text: string)`:
1. Call `store.applyToEditor(text)`

`copyToClipboard(text: string)`:
1. `navigator.clipboard.writeText(text)`

`stopGeneration()`:
1. Call `signalRService.disconnect()` then `signalRService.connect()` (reconnect for future messages)
2. If `store.currentTokens()` is non-empty, finalize partial: `store.completeGeneration(store.currentTokens())`

`onChipClick(action: string)`:
1. Set `inputMessage` to action text (e.g., "Refine this content", "Shorten for the platform")
2. Call `sendMessage()`

**Computed signals:**

`quickActionChips`:
```typescript
computed(() => {
  const content = this.store.content();
  if (!content?.body) {
    return [
      { label: 'Draft from idea', action: 'Draft content based on the idea context' },
      { label: 'Draft from scratch', action: 'Draft this content from scratch' },
    ];
  }
  return [
    { label: 'Refine', action: 'Refine and improve this content' },
    { label: 'Shorten', action: 'Shorten this content for the platform' },
    { label: 'Expand', action: 'Expand this content with more detail' },
    { label: 'Change tone', action: 'Rewrite in a more conversational tone' },
  ];
});
```

### Template Structure

**File:** `sidecar-chat.component.html` (or inline template)

```
PrimeNG Sidebar (position="right", width ~360px, [visible], (visibleChange))
  Header: "AI Chat" with close button
  
  Scrollable message list (flex-grow, overflow-y auto):
    @for message of store.chatMessages()
      User messages: right-aligned, dark background
      Assistant messages: left-aligned, rendered via ngx-markdown
        Action bar below each assistant message:
          "Apply to Editor" button (pi-check icon)
          "Copy" button (pi-copy icon)
    
    Streaming area (visible when isStreaming or currentTokens not empty):
      If currentTokens empty: skeleton shimmer (3-5 animated lines)
      If currentTokens non-empty: render partial tokens via ngx-markdown
      Stop button (pi-stop-circle, p-button-danger)
    
    Scroll anchor div (messagesEnd ViewChild -- scrollIntoView on new messages)
  
  Quick action chips (above input):
    @for chip of quickActionChips()
      p-button (outlined, small, rounded) -- click calls onChipClick
  
  Input area (fixed bottom):
    p-inputTextarea (autoResize, rows=1, maxRows=4)
      [(ngModel)]="inputMessage"
      (keydown)="onKeydown($event)"
      [disabled]="store.isStreaming()"
    Send button (pi-send icon, disabled when empty or streaming)
```

### Styles

The component uses dark theme colors matching the PrimeNG Aura Dark palette used throughout the app:

- Sidebar background: `#0d1117` (matches app background)
- User message bubble: `#1f6feb` background, white text, right-aligned
- AI message bubble: `#161b22` background, `#e6edf3` text, left-aligned
- Input area: `#161b22` background with `#30363d` border
- Quick action chips: outlined style, small, `#58a6ff` accent
- Skeleton shimmer: animated gradient on `#21262d` background
- Action buttons on AI messages: ghost buttons, `#8b949e` text, hover to `#f0f6fc`
- Scrollbar: thin, matches dark theme

### Skeleton Shimmer CSS

```css
.skeleton-line {
  height: 14px;
  background: linear-gradient(90deg, #21262d 25%, #30363d 50%, #21262d 75%);
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
  border-radius: 4px;
  margin-bottom: 8px;
}
.skeleton-line:nth-child(2) { width: 85%; }
.skeleton-line:nth-child(3) { width: 70%; }

@keyframes shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
```

### Auto-scroll Behavior

After each message addition or token accumulation, scroll the messages container to the bottom. Use a `ViewChild` on a scroll anchor `<div>` at the bottom of the message list and call `scrollIntoView({ behavior: 'smooth' })`. Debounce scroll during token streaming to avoid excessive reflows (every 5th token or ~100ms interval).

### PrimeNG Components Used

- `Sidebar` from `primeng/sidebar` -- the slide-in panel
- `Button` from `primeng/button` -- send, stop, apply, copy, chips
- `InputTextarea` from `primeng/inputtextarea` -- chat input
- `ngx-markdown` `MarkdownComponent` -- rendering AI messages as formatted markdown

### Integration with Content Editor

The `ContentEditorComponent` (section 15) hosts this component:

```html
<!-- In content-editor.component.html -->
<app-sidecar-chat
  [(visible)]="isChatOpen"
  [contentId]="contentId()"
/>

<!-- Toggle button in bottom-right -->
<p-button
  icon="pi pi-comments"
  [rounded]="true"
  class="chat-toggle-fab"
  (onClick)="isChatOpen.set(!isChatOpen())"
  pTooltip="AI Chat (Ctrl+Shift+C)"
/>
```

The editor component also handles the `Ctrl+Shift+C` keyboard shortcut via `@HostListener('document:keydown', ['$event'])` to toggle `isChatOpen`.

---

## Key Design Decisions

1. **PrimeNG Sidebar over custom panel** -- built-in animation, overlay behavior, accessibility, and responsive handling. Width fixed at ~360px to avoid crowding the editor.

2. **SignalR subscriptions in component, not store** -- the store provides pure state mutations (`appendToken`, `completeGeneration`). The component owns the SignalR lifecycle (connect on init, disconnect on destroy, subscribe to observables). This keeps the store testable without mocking SignalR.

3. **Chat history is in-memory only** -- stored in `ContentEditorStore.chatMessages` signal. Navigating away from the editor destroys the component-scoped store, losing chat history. Persistence would require a `ChatMessage` entity with ContentId FK -- deferred as non-essential for MVP.

4. **Stop generation via disconnect/reconnect** -- calling `signalRService.disconnect()` triggers `Context.ConnectionAborted` on the server hub, which propagates the CancellationToken to `ISidecarClient.StreamPromptAsync`, which kills the sidecar process. Reconnecting immediately after allows the user to continue chatting.

5. **Quick action chips are context-adaptive** -- when the editor body is empty, chips suggest drafting actions. When content exists, chips suggest refinement actions. This reduces the friction of knowing what to ask the AI.

6. **Markdown rendering for AI messages** -- AI responses often contain markdown formatting (headers, lists, code blocks, bold/italic). Using `ngx-markdown` ensures proper rendering. User messages are plain text (no markdown parsing).

7. **Scroll debouncing during streaming** -- without debouncing, `scrollIntoView` fires on every token (potentially 100+ times per second), causing layout thrashing. Throttle to every ~100ms or every 5th token.

---

## Error Handling

- **SignalR connection failure:** Show inline error message in chat area with "Retry" button that calls `signalRService.connect()` again.
- **Generation error (from `generationError$`):** Add error message to chat with red styling and "Retry" button that re-sends the last user message.
- **Clipboard API failure (copy):** Fall back to `document.execCommand('copy')` or show toast "Copy failed".
- **Empty content ID:** Guard `sendMessage()` -- if `contentId()` is falsy, don't send (content not yet created/loaded).
