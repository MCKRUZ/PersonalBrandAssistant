# Section 11: Jarvis HUD -- BFF Proxy Routes and Zustand Store

## Overview

Adds PBA integration to the Jarvis HUD (Next.js 15 dashboard) through two layers:

1. **BFF (Backend-For-Frontend) Proxy Routes**: Five Next.js Route Handlers under `app/api/pba/` that proxy requests to PBA's REST API. These routes hold the PBA API key server-side so it is never exposed to the browser. One route proxies the SSE stream transparently.

2. **Zustand Store**: A new `pba-store.ts` state store managing pipeline items, calendar data, engagement metrics, trending topics, and briefing summary. The store is updated by BFF fetch calls and real-time SSE events.

Together, these provide the data layer that the HUD panels and pages in section 12 consume.

## Dependencies

- **section-01-pba-monitor-endpoints**: The PBA REST endpoints that the BFF routes proxy to must be deployed:
  - `GET /api/briefing/summary`
  - `GET /api/content/queue-status`
  - `GET /api/analytics/engagement-summary`
  - `GET /api/trends`
  - Calendar endpoint (existing)
- **section-02-sse-broadcaster**: The PBA SSE endpoint must be deployed:
  - `GET /api/events/pipeline`

## Tests (Write First)

### BFF Proxy Route Tests

Test file: `app/api/pba/__tests__/pba-routes.test.ts`

Use Vitest. Mock the `fetch` calls to PBA's API.

```typescript
// --- GET /api/pba/status ---

// Test: proxies to PBA /api/briefing/summary with server-side API key
//   Mock global fetch to return a valid briefing summary JSON
//   Call the route handler
//   Assert fetch was called with PBA_API_URL + "/api/briefing/summary"
//   Assert fetch was called with headers including X-Api-Key
//   Assert response body matches the mocked PBA response

// Test: returns 502 when PBA unreachable
//   Mock fetch to throw a connection error
//   Assert response status is 502
//   Assert response body contains an error message

// Test: does not expose API key to client
//   Assert the response headers do not contain X-Api-Key
//   Assert the response body does not contain the API key value


// --- GET /api/pba/pipeline ---

// Test: proxies SSE stream from PBA with correct headers
//   Mock fetch to return a ReadableStream with SSE events
//   Call the route handler
//   Assert response Content-Type is "text/event-stream"
//   Assert response has Cache-Control: no-cache
//   Assert response has Connection: keep-alive

// Test: forwards events transparently
//   Mock SSE stream with pipeline:stage-change event
//   Assert the proxied response includes the same event data

// Test: handles PBA disconnect gracefully
//   Mock fetch to throw mid-stream
//   Assert the route handler closes the response stream cleanly


// --- GET /api/pba/calendar ---

// Test: proxies to PBA calendar endpoint with date range params
//   Mock fetch to return calendar items
//   Call with ?startDate=2026-03-23&endDate=2026-03-30
//   Assert fetch was called with the correct URL including query params
//   Assert API key passed server-side

// Test: passes API key server-side
//   Assert fetch headers include X-Api-Key


// --- GET /api/pba/engagement ---

// Test: proxies to PBA engagement summary
//   Mock fetch to return engagement metrics
//   Assert response matches mocked data
//   Assert API key passed server-side

// Test: passes API key server-side
//   Assert fetch headers include X-Api-Key


// --- GET /api/pba/trends ---

// Test: proxies to PBA trends endpoint
//   Mock fetch to return trending topics
//   Assert response matches mocked data
//   Assert API key passed server-side

// Test: passes API key server-side
//   Assert fetch headers include X-Api-Key
```

### Zustand Store Tests

Test file: `stores/__tests__/pba-store.test.ts`

Use Vitest. Test the store actions and state transitions.

```typescript
// Test: updatePipelineItem adds new item for pipeline:created event
//   Call updatePipelineItem with a pipeline:created event
//   Assert pipelineItems array contains the new item
//   Assert the original array reference was not mutated (immutability)

// Test: updatePipelineItem updates existing item for pipeline:stage-change
//   Seed store with a pipeline item at stage "Draft"
//   Call updatePipelineItem with pipeline:stage-change to "VoiceCheck"
//   Assert the item's stage was updated to "VoiceCheck"
//   Assert updatedAt was refreshed
//   Assert immutability (new array reference)

// Test: updatePipelineItem removes item for pipeline:published
//   Seed store with 3 pipeline items
//   Call updatePipelineItem with pipeline:published for one item
//   Assert pipelineItems has 2 items remaining

// Test: setCalendarItems replaces calendar data immutably
//   Seed store with initial calendar items
//   Call setCalendarItems with new data
//   Assert calendarItems matches the new data
//   Assert the array is a new reference (not mutated)

// Test: setEngagementMetrics replaces engagement data immutably
//   Call setEngagementMetrics with new metrics
//   Assert engagementMetrics matches the new data

// Test: setBriefingSummary replaces briefing data immutably
//   Call setBriefingSummary with new summary
//   Assert briefingSummary matches the new data

// Test: initial state has empty arrays and null metrics
//   Create a fresh store
//   Assert pipelineItems is []
//   Assert calendarItems is []
//   Assert engagementMetrics is null
//   Assert trendingTopics is []
//   Assert briefingSummary is null
```

## File Paths

### New Files

- `app/api/pba/status/route.ts` -- BFF proxy for briefing summary.
- `app/api/pba/pipeline/route.ts` -- BFF proxy for SSE pipeline stream.
- `app/api/pba/calendar/route.ts` -- BFF proxy for calendar data.
- `app/api/pba/engagement/route.ts` -- BFF proxy for engagement metrics.
- `app/api/pba/trends/route.ts` -- BFF proxy for trending topics.
- `stores/pba-store.ts` -- Zustand store for PBA state.
- `lib/types.ts` -- PBA-specific type definitions (added to existing types file).
- `hooks/use-pba-sse.ts` -- Custom hook for SSE connection management.
- `app/api/pba/__tests__/pba-routes.test.ts` -- BFF route tests.
- `stores/__tests__/pba-store.test.ts` -- Store tests.

### Modified Files

- `.env.local` -- Add `PBA_API_URL` and `PBA_API_KEY` environment variables.

## BFF Proxy Routes

### GET /api/pba/status (route.ts)

Proxies to PBA's `/api/briefing/summary` endpoint. Returns the aggregated PBA status used by MetricCards and BriefingPanel on the main dashboard.

```typescript
// app/api/pba/status/route.ts
export async function GET() {
  // 1. Read PBA_API_URL and PBA_API_KEY from process.env (server-side only)
  // 2. Fetch PBA_API_URL + "/api/briefing/summary" with X-Api-Key header
  // 3. On success: return the JSON response with 30-second cache (ISR)
  // 4. On failure: return 502 with { error: "PBA unavailable" }
  // 5. Timeout: 10 seconds, 1 retry on failure
}
```

Response caching: Set `Cache-Control: s-maxage=30, stale-while-revalidate=60` for ISR behavior. This means the dashboard polls at most every 30 seconds, with stale data served while revalidating.

### GET /api/pba/pipeline (route.ts)

Proxies the SSE stream from PBA's `/api/events/pipeline` endpoint. This is a streaming response that holds the connection open.

```typescript
// app/api/pba/pipeline/route.ts
export async function GET() {
  // 1. Fetch PBA_API_URL + "/api/events/pipeline" with X-Api-Key header
  // 2. Set response headers: Content-Type: text/event-stream, Cache-Control: no-cache
  // 3. Pipe the PBA response body (ReadableStream) through to the client
  // 4. On PBA disconnect: close the client stream cleanly
  // 5. No caching -- real-time stream
}
```

The SSE proxy must handle PBA disconnection gracefully. If the PBA connection drops, the proxy closes the client stream, and the client-side SSE hook reconnects with exponential backoff.

### GET /api/pba/calendar (route.ts)

Proxies to PBA's calendar endpoint with date range query parameters.

```typescript
// app/api/pba/calendar/route.ts
export async function GET(request: Request) {
  // 1. Extract startDate and endDate from URL search params
  // 2. Fetch PBA_API_URL + "/api/calendar?startDate=...&endDate=..." with API key
  // 3. Return calendar items JSON
  // 4. Timeout: 10 seconds, 1 retry
}
```

### GET /api/pba/engagement (route.ts)

Proxies to PBA's `/api/analytics/engagement-summary` endpoint.

```typescript
// app/api/pba/engagement/route.ts
export async function GET() {
  // 1. Fetch PBA_API_URL + "/api/analytics/engagement-summary" with API key
  // 2. Return engagement metrics JSON
  // 3. Timeout: 10 seconds, 1 retry
}
```

### GET /api/pba/trends (route.ts)

Proxies to PBA's `/api/trends` endpoint.

```typescript
// app/api/pba/trends/route.ts
export async function GET() {
  // 1. Fetch PBA_API_URL + "/api/trends" with API key
  // 2. Return trending topics JSON
  // 3. Timeout: 10 seconds, 1 retry
}
```

## Zustand Store

### State Shape

```typescript
// stores/pba-store.ts
interface PbaState {
  pipelineItems: PipelineItem[];
  calendarItems: CalendarItem[];
  engagementMetrics: EngagementMetrics | null;
  trendingTopics: TrendTopic[];
  briefingSummary: BriefingSummary | null;
}
```

### Actions

```typescript
interface PbaActions {
  updatePipelineItem: (event: PipelineEvent) => void;
  setCalendarItems: (items: CalendarItem[]) => void;
  setEngagementMetrics: (metrics: EngagementMetrics) => void;
  setTrendingTopics: (topics: TrendTopic[]) => void;
  setBriefingSummary: (summary: BriefingSummary) => void;
}
```

### updatePipelineItem Logic

This action processes SSE events and updates the pipeline items array immutably:

- `pipeline:created` -- Spread a new item into the array: `[...items, newItem]`
- `pipeline:stage-change` -- Map over items, replacing the matching item with updated stage and timestamp: `items.map(item => item.contentId === event.contentId ? { ...item, stage: event.newStage, updatedAt: event.timestamp } : item)`
- `pipeline:published` -- Filter out the published item: `items.filter(item => item.contentId !== event.contentId)`
- `pipeline:failed` -- Map over items, updating the matching item's stage to "Failed"
- `pipeline:approval-needed` -- Map over items, adding an approval indicator to the matching item
- `pipeline:snapshot` -- Replace the entire array with the snapshot data

All operations produce new array references (never mutate).

## Type Definitions

Add to `lib/types.ts`:

```typescript
interface PipelineItem {
  contentId: string;
  title: string;
  platform: string;
  stage: string;
  contentType: string;
  updatedAt: string;
}

interface CalendarItem {
  contentId: string;
  platform: string;
  scheduledAt: string;
  title: string;
  contentType: string;
}

interface EngagementMetrics {
  rolling7Day: number;
  average: number;
  platformBreakdown: Record<string, number>;
  anomalies: EngagementAnomaly[];
}

interface EngagementAnomaly {
  contentId: string;
  platform: string;
  metric: string;
  value: number;
  average: number;
  multiplier: number;
  direction: "positive" | "negative";
  confidence: number;
}

interface TrendTopic {
  topic: string;
  relevanceScore: number;
  source: string;
}

interface BriefingSummary {
  scheduledToday: CalendarItem[];
  engagementHighlights: EngagementHighlight[];
  trendingTopics: TrendTopic[];
  queueDepth: number;
  pendingApprovals: number;
}

interface EngagementHighlight {
  contentId: string;
  platform: string;
  metric: string;
  value: number;
}

interface PipelineEvent {
  type: string;       // "pipeline:created" | "pipeline:stage-change" | etc.
  contentId: string;
  previousStage?: string;
  newStage?: string;
  title?: string;
  platform?: string;
  contentType?: string;
  timestamp: string;
}
```

## SSE Client Hook

A custom React hook `usePbaSse` manages the SSE connection lifecycle and feeds events into the Zustand store.

File: `hooks/use-pba-sse.ts`

Behavior:
- On mount: Open an `EventSource` connection to `/api/pba/pipeline`
- Parse each SSE event and call `usePbaStore.getState().updatePipelineItem(event)`
- On connection error: Reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s)
- On unmount: Close the EventSource connection
- Expose connection status (`connected`, `reconnecting`, `disconnected`) for UI indicators

```typescript
// hooks/use-pba-sse.ts
export function usePbaSse() {
  // Returns { status: "connected" | "reconnecting" | "disconnected" }
  // Internally manages EventSource lifecycle
  // Feeds events into pba-store via updatePipelineItem
}
```

## Environment Configuration

Add to `.env.local`:

```
PBA_API_URL=http://192.168.50.x:5000
PBA_API_KEY=<pba-readonly-key>
```

These variables are only accessible in Route Handlers (server-side). They are never exposed to the browser runtime. The `pba-readonly` key is sufficient for all BFF routes since they only read data and stream events.

## Error Handling

- **BFF routes**: On PBA failure, return HTTP 502 with a JSON error body: `{ "error": "PBA unavailable", "message": "..." }`. The frontend components receiving a 502 show a degraded state (skeleton UI + "PBA unavailable" message).
- **SSE proxy**: On PBA disconnect, the proxy closes the response stream. The client-side `usePbaSse` hook detects the close and reconnects with exponential backoff.
- **Store**: No error state in the store itself. Components check for null/empty state and render accordingly. A null `briefingSummary` means data hasn't loaded yet or PBA is unavailable.

## Timeouts and Retries

- BFF proxy HTTP calls: 10-second timeout, 1 retry on failure.
- SSE connection: Auto-reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s). The backoff resets on successful connection.
- ISR caching on `/api/pba/status`: 30-second `s-maxage`, 60-second `stale-while-revalidate`.

## Implementation Notes

- All BFF routes follow the same pattern: read env vars, fetch from PBA with API key, return response or 502. Extract a shared helper `fetchPba(path: string, options?: RequestInit)` to avoid duplication.
- The SSE proxy route is the only one that holds a long-lived connection. It must be a streaming response, not a buffered one. Use `TransformStream` or pipe the PBA response body directly.
- The Zustand store uses Zustand's `create` function with the `immer` middleware for convenient immutable updates, or plain spread operators to maintain immutability.
- The `usePbaSse` hook should be mounted in the `/content` page layout or the dashboard layout, depending on where real-time pipeline data is needed. It can be conditionally mounted to avoid unnecessary connections when the user is not viewing content-related pages.
- The API key in `.env.local` is the readonly-scoped key. The HUD never needs write access to PBA -- all mutations go through Jarvis voice commands via MCP tools.
