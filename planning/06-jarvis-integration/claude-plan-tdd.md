# TDD Plan: Jarvis <-> PBA Integration

Mirrors the structure of `claude-plan.md`. For each section, defines test stubs to write BEFORE implementing.

## Testing Infrastructure

**PBA (.NET):** xUnit + Moq + `WebApplicationFactory<Program>` + in-memory EF Core DB. Tests in `tests/PersonalBrandAssistant.*.Tests/`.

**Jarvis Monitor (Node.js):** Vitest. Tests in `src/__tests__/` or colocated `*.test.ts`.

**Jarvis HUD (Next.js):** Vitest + React Testing Library. Tests colocated with components.

---

## 3. MCP Server

### 3.2 ContentPipelineTools

```csharp
// Test: pba_create_content creates content item and returns ID when autonomy allows
// Test: pba_create_content queues for approval when autonomy is manual
// Test: pba_create_content validates platform is a known enum value
// Test: pba_create_content validates contentType is a known enum value
// Test: pba_get_pipeline_status returns all active items when no contentId specified
// Test: pba_get_pipeline_status returns specific item when contentId provided
// Test: pba_get_pipeline_status returns empty when contentId not found
// Test: pba_publish_content succeeds for approved content
// Test: pba_publish_content fails for content not in Approved state
// Test: pba_publish_content fails for content that hasn't passed voice check
// Test: pba_list_drafts filters by status
// Test: pba_list_drafts filters by platform
// Test: pba_list_drafts returns all drafts when no filters
```

### 3.2 CalendarTools

```csharp
// Test: pba_get_calendar returns items in date range
// Test: pba_get_calendar filters by platform
// Test: pba_get_calendar returns empty for range with no content
// Test: pba_schedule_content creates schedule for available slot
// Test: pba_schedule_content fails for conflicting time slot
// Test: pba_schedule_content validates contentId exists
// Test: pba_reschedule_content moves to new time slot
// Test: pba_reschedule_content fails if new slot conflicts
```

### 3.2 AnalyticsTools

```csharp
// Test: pba_get_trends returns topics sorted by relevance
// Test: pba_get_trends respects limit parameter
// Test: pba_get_engagement_stats aggregates by platform
// Test: pba_get_engagement_stats filters by date range
// Test: pba_get_engagement_stats filters by platform
// Test: pba_get_content_performance returns metrics for published content
// Test: pba_get_content_performance returns error for unpublished content
```

### 3.2 SocialEngagementTools

```csharp
// Test: pba_get_opportunities returns ranked opportunities
// Test: pba_get_opportunities filters by platform
// Test: pba_get_opportunities respects limit
// Test: pba_respond_to_opportunity with responseText sends directly (if autonomy allows)
// Test: pba_respond_to_opportunity without responseText generates response via Claude
// Test: pba_respond_to_opportunity queues when autonomy is manual
// Test: pba_get_inbox returns items filtered by platform
// Test: pba_get_inbox filters unread only
```

### 3.5 Idempotency

```csharp
// Test: write tool with clientRequestId caches result
// Test: duplicate clientRequestId returns cached result without re-executing
// Test: different clientRequestId executes independently
// Test: cached result expires after 5 minutes
// Test: write tool without clientRequestId always executes
```

### 3.6 Audit Trail

```csharp
// Test: write tool creates audit log entry with actor "jarvis/openclaw"
// Test: audit log includes tool name and redacted parameters
// Test: audit log includes outcome (success/failure/queued)
// Test: read-only tools do not create audit entries
```

### 3.7 Process Model

```csharp
// Test: --mcp flag starts MCP server (stdio transport)
// Test: without --mcp flag starts HTTP server (normal API)
// Test: MCP server discovers all tool classes via assembly scanning
// Test: MCP server tool count matches expected (13 tools)
```

---

## 4. PBA API -- New Endpoints

### 4.1 Monitor-Facing Endpoints

```csharp
// GET /api/content/queue-status
// Test: returns correct queueDepth from active content items
// Test: returns nearest nextScheduledPost
// Test: returns null nextScheduledPost when nothing scheduled
// Test: returns correct postsLast24h count
// Test: returns correct itemsByStage breakdown
// Test: requires API key authentication
// Test: readonly key scope is sufficient

// GET /api/content/pipeline-health
// Test: returns stuck items (UpdatedAt > 2h in non-terminal state)
// Test: returns zero stuck items when all active items are recent
// Test: returns correct failedGenerations24h count
// Test: calculates error rate correctly
// Test: requires API key authentication

// GET /api/analytics/engagement-summary
// Test: calculates rolling 7-day engagement correctly
// Test: detects positive anomaly (engagement > 2x average)
// Test: detects negative anomaly (engagement < 0.5x average)
// Test: includes direction and confidence in anomaly objects
// Test: returns platform breakdown
// Test: requires API key authentication

// GET /api/briefing/summary
// Test: returns scheduledToday items for current date
// Test: returns engagement highlights
// Test: returns trending topics
// Test: returns correct queueDepth
// Test: returns pendingApprovals count
// Test: requires API key authentication
```

### 4.2 SSE Endpoint

```csharp
// GET /api/events/pipeline
// Test: returns text/event-stream content type
// Test: sends pipeline:snapshot event on connect with current state
// Test: streams pipeline:stage-change events when content moves stages
// Test: streams pipeline:created events for new content
// Test: streams pipeline:published events
// Test: streams pipeline:failed events
// Test: multiple concurrent connections each receive all events (broadcast)
// Test: requires API key authentication
// Test: readonly key scope is sufficient
```

### 4.2 PipelineEventBroadcaster

```csharp
// Test: subscriber receives events written after subscription
// Test: unsubscribed client does not receive events
// Test: multiple subscribers each receive every event
// Test: subscriber channel is cleaned up on disconnect
// Test: broadcast does not block on slow subscribers
```

### 8.1 Scoped API Keys

```csharp
// Test: readonly key can access GET endpoints
// Test: readonly key can access SSE endpoint
// Test: readonly key cannot access write endpoints (returns 403)
// Test: write key can access all endpoints
// Test: invalid key returns 401
// Test: missing key returns 401
```

---

## 5. Jarvis Monitor -- PBA Custom Monitors

### 5.1 PbaHealthMonitor

```typescript
// Test: returns healthy when PBA responds 200 with healthy status
// Test: returns degraded when PBA responds 200 but degraded
// Test: returns degraded when response takes > 2s
// Test: returns unhealthy/critical when PBA is unreachable
// Test: returns error when connection times out
// Test: includes latency in details
```

### 5.1 PbaContentQueueMonitor

```typescript
// Test: returns healthy when hasScheduledIn48h and queueDepth >= 3
// Test: returns healthy with info note when hasScheduledIn48h and queueDepth < 3
// Test: returns degraded/medium when no scheduled in 48h and queueDepth > 0
// Test: returns degraded/medium when no scheduled in 48h and queueDepth == 0
// Test: includes queueDepth, nextScheduledPost, itemsByStage in details
// Test: returns error when PBA API unreachable
```

### 5.1 PbaEngagementMonitor

```typescript
// Test: returns healthy when no anomalies
// Test: returns healthy when only positive anomalies (viral)
// Test: returns degraded/high when negative anomalies present
// Test: includes anomaly direction and confidence in details
// Test: includes platform breakdown in details
// Test: returns error when PBA API unreachable
```

### 5.1 PbaPipelineMonitor

```typescript
// Test: returns healthy when no stuck items and error rate < 10%
// Test: returns degraded/high when stuck items present
// Test: returns degraded/high when error rate >= 10%
// Test: includes stuck item list in details
// Test: includes error rate and active count in details
// Test: returns error when PBA API unreachable
```

### 5.3 Alert Classification

```typescript
// Test: pba:health unhealthy maps to critical severity
// Test: pba:pipeline degraded maps to high severity
// Test: pba:engagement degraded with negative anomaly maps to high severity
// Test: pba:content-queue degraded maps to medium severity
// Test: pba:engagement healthy with positive anomaly does NOT trigger alert
```

---

## 6. Jarvis HUD -- Dashboard Integration

### 6.1 BFF Proxy Routes

```typescript
// GET /api/pba/status
// Test: proxies to PBA /api/briefing/summary with server-side API key
// Test: returns 502 when PBA unreachable
// Test: does not expose API key to client

// GET /api/pba/pipeline
// Test: proxies SSE stream from PBA with correct headers
// Test: forwards events transparently
// Test: handles PBA disconnect gracefully

// GET /api/pba/calendar
// Test: proxies to PBA calendar endpoint with date range params
// Test: passes API key server-side

// GET /api/pba/engagement
// Test: proxies to PBA engagement summary
// Test: passes API key server-side

// GET /api/pba/trends
// Test: proxies to PBA trends endpoint
// Test: passes API key server-side
```

### 6.2 Zustand Store (pba-store)

```typescript
// Test: updatePipelineItem adds new item for pipeline:created event
// Test: updatePipelineItem updates existing item for pipeline:stage-change
// Test: updatePipelineItem removes item for pipeline:published
// Test: setCalendarItems replaces calendar data immutably
// Test: setEngagementMetrics replaces engagement data immutably
// Test: setBriefingSummary replaces briefing data immutably
// Test: initial state has empty arrays and null metrics
```

### 6.3 Existing Panel Integration

```typescript
// MetricCards
// Test: renders Content Queue card with queueDepth from briefingSummary
// Test: renders Scheduled Today card with count from briefingSummary
// Test: renders Engagement card with highlights from briefingSummary
// Test: renders degraded state when briefingSummary is null

// BriefingPanel
// Test: renders PBA section with scheduled posts count
// Test: renders engagement highlights
// Test: renders trending topic count
// Test: renders queue depth
// Test: omits PBA section when data unavailable

// AlertFeed
// Test: renders PBA alerts with source tag
// Test: PBA alerts sorted by severity alongside infra alerts
```

### 6.4 Dedicated /content Page

```typescript
// Pipeline Panel
// Test: renders kanban board with items from pba-store
// Test: updates in real-time when SSE events arrive
// Test: shows stage name, platform icon, time in stage per card

// Calendar Panel
// Test: renders weekly view with scheduled items
// Test: platform icons display correctly
// Test: date range selector updates fetched data

// Engagement Panel
// Test: renders platform-by-platform metric cards
// Test: renders top-performing content list
// Test: renders engagement trend chart

// Trends Panel
// Test: renders trending topic cards with relevance bars
// Test: renders source attribution per topic
```

### 6.5 Navigation

```typescript
// Test: sidebar includes "Content" nav item
// Test: clicking Content navigates to /content route
// Test: Content nav item shows active state when on /content
```
