# Section 12: Angular Models and Services

## Overview

This section creates the TypeScript foundation for the Content Studio frontend: model interfaces that mirror backend DTOs, a ContentService HTTP client covering all 16 API endpoints, and a SignalRService for real-time sidecar chat streaming.

## Dependencies

- **Section 11 (API Endpoints):** The backend must expose all 16 `/api/content` routes.
- **Existing:** `@microsoft/signalr` already in `package.json`. `PagedResult<T>` in `src/PersonalBrandAssistant.Web/src/app/models/pagination.model.ts`.

**Blocks:** Sections 13, 14, 15, 16.

---

## npm Packages to Install

```bash
npm install @acrodata/code-editor @codemirror/lang-markdown ngx-markdown prismjs
```

Needed by editor page (section 15), installed now to avoid broken imports later.

---

## Tests First

### ContentService Tests

**File:** `content.service.spec.ts`

| Test | Description |
|------|-------------|
| `list() calls GET /api/content with correct query params` | Verify HttpParams for filters/pagination |
| `get() calls GET /api/content/{id}` | Verify correct URL, expect ContentDetail |
| `create() calls POST /api/content with body` | Verify POST body matches |
| `update() calls PUT /api/content/{id}` | Verify lastUpdatedAt in body |
| `delete() calls DELETE /api/content/{id}` | Verify DELETE |
| `draft() calls POST /api/content/{id}/draft` | Verify action/instructions in body |
| `approve() calls PUT /api/content/{id}/approve` | Verify PUT |
| `publish() calls POST /api/content/{id}/publish` | Verify POST |
| `voiceCheck() calls GET /api/content/{id}/voice-check` | Verify GET, expect VoiceCheckResult |

### SignalRService Tests

**File:** `signalr.service.spec.ts`

| Test | Description |
|------|-------------|
| `connect() establishes hub connection` | Verify HubConnection built with `/hubs/content` |
| `sendChatMessage invokes hub method` | Verify invoke('SendChatMessage', contentId, message) |
| `tokens$ emits received tokens` | Simulate ReceiveToken callback |
| `generationComplete$ emits on completion` | Simulate GenerationComplete callback |
| `Auto-reconnect configured` | Verify withAutomaticReconnect() |

---

## Implementation

### 1. TypeScript Models

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/models/content.model.ts`

**Enums:** `ContentStatus` (7 values), `ContentType` (8 values), `Platform` (6 values), `PublishStatus` (4 values) -- all string enums matching backend exactly.

**Interfaces:**
- `Content` -- list view: id, title, contentType, status, primaryPlatform, voiceScore, tags, timestamps
- `ContentDetail` extends `Content` -- adds body, viralityPrediction, sourceIdeaId, parentContentId, platformPublishes, children
- `PlatformPublish` -- id, platform, publishStatus, publishedUrl, publishedAt
- `ChildContent` -- id, title, contentType, primaryPlatform, status, updatedAt
- `VoiceCheckResult` -- score, feedback

**Request interfaces:** `CreateContentRequest`, `UpdateContentRequest` (with required lastUpdatedAt), `DraftContentRequest`, `ScheduleContentRequest`, `CrossPostRequest`

### 2. ContentService

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts`

`@Injectable({ providedIn: 'root' })`. Uses `HttpClient` directly (same as IdeaService). Base URL: `/api/content`.

16 methods mapping 1:1 to API endpoints. `list()` builds `HttpParams` from filter/pagination args. All return `Observable<T>`.

### 3. SignalRService

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/services/signalr.service.ts`

`@Injectable({ providedIn: 'root' })`. Manages SignalR hub connection for real-time chat.

**Public API:**
- `tokens$: Observable<string>` -- incoming tokens
- `generationComplete$: Observable<string>` -- completion events
- `generationError$: Observable<string>` -- error events
- `connect(): Promise<void>` -- builds HubConnection to `/hubs/content` with `withAutomaticReconnect()`, registers 3 handlers
- `disconnect(): Promise<void>` -- stops connection
- `sendChatMessage(contentId: string, message: string): Promise<void>` -- invokes hub method

---

## File Summary

| File | Action |
|------|--------|
| `features/content/models/content.model.ts` | Create |
| `features/content/services/content.service.ts` | Create |
| `features/content/services/content.service.spec.ts` | Create |
| `features/content/services/signalr.service.ts` | Create |
| `features/content/services/signalr.service.spec.ts` | Create |

---

## Patterns to Follow

- Model file in `features/content/models/` (co-located with feature)
- `@Injectable({ providedIn: 'root' })` for services
- `HttpClient` directly, not `ApiService` wrapper
- `HttpParams` for query parameters (only append non-null)
- String enums matching backend exactly
- Reuse `PagedResult<T>` from `src/app/models/pagination.model.ts`

---

## Implementation Notes (Actual)

### Deviations from Plan
- **SignalR testability:** Used injectable `HUB_CONNECTION_FACTORY` InjectionToken instead of direct `new HubConnectionBuilder()`. The ES module export is not writable, so `spyOn` fails. The factory token pattern provides clean DI-based testing.
- **Double-call guard:** Added `if (this.connection) await this.disconnect()` at the top of `connect()` to prevent leaked connections on re-connect (code review fix).
- **ContentFilterState added:** Created `ContentFilterState` interface for typed filter parameters on `list()`, not specified in original plan but needed for clean method signature.

### Files Created
| File | Lines | Tests |
|------|-------|-------|
| `features/content/models/content.model.ts` | 121 | — |
| `features/content/services/content.service.ts` | 97 | 19 tests |
| `features/content/services/content.service.spec.ts` | 261 | — |
| `features/content/services/signalr.service.ts` | 63 | 8 tests |
| `features/content/services/signalr.service.spec.ts` | 127 | — |

### npm Packages Installed
- `@acrodata/code-editor`, `@codemirror/lang-markdown`, `ngx-markdown`, `prismjs`

### Test Results
- **27 of 27 tests pass** (19 ContentService + 8 SignalRService)
