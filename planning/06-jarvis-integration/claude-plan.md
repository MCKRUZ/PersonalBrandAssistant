# Implementation Plan: Jarvis <-> PBA Integration

## 1. Context & Goals

The Personal Brand Assistant (PBA) is a .NET 10 + Angular 19 application that manages content creation, scheduling, social engagement, and analytics across multiple platforms. Jarvis is a separate AI assistant ecosystem consisting of a monitoring service (Node.js + Redis), a dashboard (Next.js 15), a voice bridge (Python + Discord), and an orchestration layer (OpenClaw Gateway). Both systems run as independent Docker Compose stacks on local infrastructure.

This integration connects the two systems bidirectionally:
- **Jarvis > PBA:** Jarvis invokes PBA capabilities through MCP tools (voice commands, chat, automation)
- **PBA > Jarvis:** PBA surfaces status, alerts, and real-time events through Jarvis's dashboard and notification infrastructure
- **Shared intelligence:** PBA content status appears in Jarvis's morning briefings and alert feed

The integration preserves full isolation between stacks (communication over host LAN IP), respects PBA's existing autonomy dial for action approval, and follows the additive-only constraint on PBA's API.

## 2. Architecture Overview

```
+------------------------------------------------------------------+
|                        Jarvis Stack                               |
|                                                                    |
|  +-------------+    +--------------+    +----------------------+ |
|  | OpenClaw    |    | jarvis-      |    | jarvis-hud           | |
|  | Gateway     |<-->| monitor      |<-->| (Next.js 15)         | |
|  | (18789)     |    | (3100)       |    | (3200)               | |
|  +------+------+    +------+-------+    +------+---------------+ |
|         |                  |                    |                  |
|         | spawns           | HTTP poll          | BFF proxy        |
|         | (stdio)          | (LAN IP)           | (LAN IP + SSE)   |
+---------+------------------+--------------------+----------------+
          |                  |                    |
          v                  v                    v
+------------------------------------------------------------------+
|                        PBA Stack                                  |
|                                                                    |
|  +--------------------------------------------------------------+ |
|  | PersonalBrandAssistant.Api (.NET 10)                        | |
|  |                                                              | |
|  |  REST API (existing)     MCP Server (new)    SSE (new)      | |
|  |  +- /api/content/*       +- stdio transport  +- /api/events | |
|  |  +- /api/analytics/*     +- 13 MCP tools     |   /pipeline  | |
|  |  +- /api/trends/*        +- wraps REST API   |              | |
|  |  +- /api/social/*                             |              | |
|  |  +- /health                                   |              | |
|  |                                                              | |
|  |  New endpoints:                                              | |
|  |  +- /api/content/queue-status                               | |
|  |  +- /api/content/pipeline-health                            | |
|  |  +- /api/analytics/engagement-summary                       | |
|  |  +- /api/briefing/summary                                   | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  +----------+  +-------------+  +-----------+                   |
|  |PostgreSQL|  | Claude      |  | Angular   |                   |
|  |          |  | Sidecar     |  | Dashboard |                   |
|  +----------+  +-------------+  +-----------+                   |
+------------------------------------------------------------------+
```

Five integration components:
1. **MCP Server** -- dual-protocol in PBA API, exposes 13 tools via stdio
2. **PBA Monitor Endpoints** -- 4 new lightweight REST endpoints for jarvis-monitor
3. **PBA SSE Endpoint** -- real-time pipeline event stream for jarvis-hud
4. **Jarvis Monitor Custom Monitors** -- 4 new monitors consuming PBA endpoints
5. **Jarvis HUD PBA Integration** -- embedded metrics + dedicated `/content` page

## 3. MCP Server (PBA API -- Dual Protocol)

### 3.1 SDK Integration

Add the `ModelContextProtocol` NuGet package (v1.1.0+) to `PersonalBrandAssistant.Api`. Register MCP services in `Program.cs` with stdio transport. The MCP server discovers tools automatically via assembly scanning of `[McpServerToolType]` classes.

When OpenClaw Gateway spawns the PBA MCP server, it starts the .NET process with stdio transport. The MCP server internally calls the PBA API's own service layer (not HTTP -- direct DI injection) to execute operations.

### 3.2 Tool Classes

Create a new directory `McpTools/` in the API project containing tool classes organized by domain:

**`McpTools/ContentPipelineTools.cs`**

Four tools wrapping the content pipeline service:
- `pba_create_content(topic, platform, contentType)` -- Initiates the content pipeline. Returns the new content ID and initial status. Respects the autonomy dial -- if set to manual, creates a draft for approval rather than auto-publishing.
- `pba_get_pipeline_status(contentId?)` -- Returns pipeline status for a specific item or lists all active items with their current stage (Ideation > Outline > Draft > VoiceCheck > Approval > Scheduled > Published).
- `pba_publish_content(contentId)` -- Publishes approved content to its target platform. Fails if content hasn't passed voice check or isn't in approved state.
- `pba_list_drafts(status?, platform?)` -- Lists content drafts with optional filtering by status and platform.

**`McpTools/CalendarTools.cs`**

Three tools wrapping the content calendar service:
- `pba_get_calendar(startDate, endDate, platform?)` -- Returns scheduled content for the date range. Includes content type, platform, time slot, and status.
- `pba_schedule_content(contentId, dateTime, platform)` -- Schedules approved content for a specific slot. Validates against calendar conflicts.
- `pba_reschedule_content(contentId, newDateTime)` -- Moves scheduled content to a new time slot. Validates the new slot is available.

**`McpTools/AnalyticsTools.cs`**

Three tools wrapping analytics and trend services:
- `pba_get_trends(limit?)` -- Returns current trending topics with relevance scores, source, and suggested content angles.
- `pba_get_engagement_stats(startDate, endDate, platform?)` -- Returns engagement metrics (likes, shares, comments, impressions) aggregated by platform and date.
- `pba_get_content_performance(contentId)` -- Returns detailed performance data for a specific published content item, including engagement trajectory and platform-specific metrics.

**`McpTools/SocialEngagementTools.cs`**

Three tools wrapping the social engagement service:
- `pba_get_opportunities(platform?, limit?)` -- Returns engagement opportunities (comments to reply to, mentions, conversation threads) ranked by relevance and recency.
- `pba_respond_to_opportunity(opportunityId, responseText?)` -- Drafts or sends a response. If `responseText` is null, uses Claude to generate a response. Autonomy dial determines auto-send vs. queue for approval.
- `pba_get_inbox(platform?, unreadOnly?)` -- Returns social inbox items (mentions, DMs, comments) with filtering options.

### 3.3 Tool Description Strategy

Each tool method and its parameters must have detailed `[Description]` attributes. These are the primary mechanism for LLM tool selection -- the LLM reads descriptions to decide which tool to invoke for a given user request. Descriptions should include:
- What the tool does (1 sentence)
- When to use it (trigger phrases like "schedule a post", "what's trending")
- What it returns (shape of the response)
- Any constraints (e.g., "content must be in Approved state to publish")

### 3.4 Autonomy Integration

MCP tools that perform write operations (create, publish, schedule, respond) check the current autonomy configuration before executing. The tool reads `AutonomyConfiguration` from the database:
- If autonomy level allows the action > execute immediately, return result
- If autonomy level requires approval > queue the action, return a pending status with an approval link/ID

This means Jarvis voice commands like "publish my LinkedIn post" will either execute or respond "Queued for your approval" depending on the dial setting.

### 3.5 Idempotency for Write Tools

All write MCP tools (`pba_create_content`, `pba_publish_content`, `pba_schedule_content`, `pba_reschedule_content`, `pba_respond_to_opportunity`) accept an optional `clientRequestId` parameter. If provided, the tool checks a short-lived cache (5-minute TTL, in-memory `IMemoryCache`) for a matching request. Duplicate requests return the cached result instead of re-executing. This prevents voice command retries and LLM tool retries from creating duplicate actions.

### 3.6 Audit Trail

All MCP write tool invocations log to PBA's existing audit trail with:
- `actor: "jarvis/openclaw"` (distinguishable from direct API or UI actions)
- Tool name and parameters (sensitive values redacted)
- Outcome (success/failure/queued-for-approval)
- Correlation ID (from OpenClaw request context)

### 3.7 Registration and Process Model

The MCP server is built as a **separate published executable** from the same API project, not a dual-mode replacement. `dotnet publish` produces a standalone binary that OpenClaw spawns via stdio.

Key design: The main PBA API process always runs HTTP (for monitors, HUD, Angular dashboard). The MCP binary is a separate process spawned by OpenClaw Gateway on demand. Both share the same codebase and service implementations but run as independent processes. The MCP binary connects to the same PostgreSQL database.

In `Program.cs`, detect the `--mcp` flag:
- If present: register MCP services with `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`, skip HTTP server setup
- If absent: start the normal HTTP API server (existing behavior)

The published MCP binary is produced by `dotnet publish` and placed at a known path that OpenClaw can reference.

## 4. PBA API -- New Endpoints

### 4.1 Monitor-Facing Endpoints

These are lightweight, read-only endpoints designed for jarvis-monitor to poll at intervals. They return pre-aggregated data to minimize database load.

**`GET /api/content/queue-status`**

Response shape:
```
{
  queueDepth: number,
  nextScheduledPost: { contentId, platform, scheduledAt } | null,
  postsLast24h: number,
  itemsByStage: { ideation: n, draft: n, review: n, approved: n, scheduled: n }
}
```

Service: Query `ContentItem` entities grouped by `WorkflowStatus`, count scheduled items, find next by `ScheduledAt`.

**`GET /api/content/pipeline-health`**

Response shape:
```
{
  stuckItems: [{ contentId, stage, stuckSince, hoursStuck }],
  failedGenerations24h: number,
  errorRate: number,
  activeCount: number
}
```

Service: Find items where `UpdatedAt` is older than 2 hours and `WorkflowStatus` is not terminal. Count failed pipeline executions in last 24h.

**`GET /api/analytics/engagement-summary`**

Response shape:
```
{
  rolling7DayEngagement: number,
  averageEngagement: number,
  anomalies: [{ contentId, platform, metric, value, average, multiplier, direction: "positive"|"negative", confidence: number }],
  platformBreakdown: { twitter: n, linkedin: n, ... }
}
```

Service: Aggregate from `EngagementMetric` entities. Flag anomalies where engagement is > 2x or < 0.5x the rolling average.

**`GET /api/briefing/summary`**

Response shape:
```
{
  scheduledToday: [{ contentId, platform, time, title }],
  engagementHighlights: [{ contentId, platform, metric, value }],
  trendingTopics: [{ topic, relevanceScore, source }],
  queueDepth: number,
  nextPublish: { contentId, platform, time } | null,
  pendingApprovals: number
}
```

Service: Aggregates data from content calendar, engagement analytics, trend monitoring, and pipeline status into a single briefing-ready payload.

### 4.2 SSE Endpoint

**`GET /api/events/pipeline`**

A Server-Sent Events endpoint that streams pipeline status changes in real-time. Uses .NET's `IAsyncEnumerable<T>` pattern with `text/event-stream` content type.

Events emitted:
- `pipeline:stage-change` -- when content moves between stages
- `pipeline:created` -- when new content enters the pipeline
- `pipeline:published` -- when content is published
- `pipeline:failed` -- when a pipeline step fails
- `pipeline:approval-needed` -- when content needs manual approval

Event shape:
```
event: pipeline:stage-change
data: { "contentId": "...", "previousStage": "Draft", "newStage": "VoiceCheck", "timestamp": "..." }
```

Implementation uses a broadcast hub pattern built on `Channel<PipelineEvent>` (System.Threading.Channels). A `PipelineEventBroadcaster` service maintains a list of subscriber channels -- one per SSE connection. The content pipeline service writes events to the broadcaster; the broadcaster fans out to all subscriber channels. Each SSE connection gets its own dedicated channel reader.

On connect, the SSE endpoint sends an initial `pipeline:snapshot` event containing the current state of all active pipeline items (from the database). After the snapshot, it streams live deltas from the broadcaster. This ensures clients that connect mid-flight see the full picture.

**Single-instance constraint (v1):** The broadcaster is in-process memory. This works for the single-instance deployment on the Synology NAS. If PBA ever scales to multiple instances, the broadcaster would need to move to Redis pub/sub or Postgres LISTEN/NOTIFY.

### 4.3 Endpoint Registration

All new endpoints are registered via a new mapper method `MapIntegrationEndpoints` in `Program.cs`, grouped under their respective route prefixes. They use the same API key middleware as existing endpoints.

## 5. Jarvis Monitor -- PBA Custom Monitors

### 5.1 Monitor Implementations

Four new monitors in jarvis-monitor, all implementing the `Monitor` interface. They live in a new `monitors/pba/` directory.

**`PbaHealthMonitor`**

Extends the existing `ServiceHealthMonitor` pattern. Hits PBA's `/health/ready` endpoint via HTTP GET. Maps response to `MonitorResult`:
- 200 + healthy > `status: "healthy"`, `severity: "info"`
- 200 + degraded > `status: "degraded"`, `severity: "medium"`
- Timeout (>2s) > `status: "degraded"`, `severity: "medium"`
- Unreachable > `status: "unhealthy"`, `severity: "critical"`

Interval: 5 minutes.

**`PbaContentQueueMonitor`**

Calls `/api/content/queue-status`. Maps to `MonitorResult` using explicit truth table:
- HasScheduledIn48h AND queueDepth >= 3 > `healthy`, severity `info`
- HasScheduledIn48h AND queueDepth < 3 > `healthy`, severity `info` (details note: "low buffer")
- No scheduled in 48h AND queueDepth > 0 > `degraded`, severity `medium`
- No scheduled in 48h AND queueDepth == 0 > `degraded`, severity `medium`

Details include `queueDepth`, `nextScheduledPost`, `postsLast24h`, `itemsByStage` from the API response.

Interval: 15 minutes.

**`PbaEngagementMonitor`**

Calls `/api/analytics/engagement-summary`. Anomalies include `direction: "positive" | "negative"` and `confidence` score. Maps to `MonitorResult`:
- No anomalies > `healthy`, severity `info`
- Only positive anomalies (viral) > `healthy`, severity `info` (details include highlights for briefing)
- Negative anomalies present > `degraded`, severity `high`
- Details include the anomaly list with direction, platform breakdown, and confidence scores

Only negative anomalies (low engagement) trigger alerts. Positive anomalies (viral posts) are surfaced as highlights in the briefing panel, not as alerts.

Interval: 30 minutes.

**`PbaPipelineMonitor`**

Calls `/api/content/pipeline-health`. Maps to `MonitorResult`:
- No stuck items AND error rate < 10% > `healthy`
- Stuck items OR error rate > 10% > `degraded`, severity `high`
- Details include stuck item list, error rate, active count

Interval: 10 minutes.

### 5.2 Configuration

Add a `pba` section to `jarvis-monitors.json`:

```json
{
  "pba": {
    "level": "deep",
    "apiUrl": "http://192.168.50.x:5000",
    "apiKey": "${PBA_API_KEY}",
    "intervals": {
      "health": "5m",
      "contentQueue": "15m",
      "engagement": "30m",
      "pipeline": "10m"
    }
  }
}
```

### 5.3 Alert Classification

Add PBA-specific rules to the alert classifier (`alerts/classifier.ts`):
- `pba:health` unhealthy > `critical` (PBA is down)
- `pba:pipeline` degraded > `high` (content stuck or failing)
- `pba:engagement` degraded > `high` (engagement anomaly)
- `pba:content-queue` degraded > `medium` (empty queue)

Alert routing follows existing patterns: critical > Discord DM + dashboard + voice, high > Discord DM + dashboard, medium > dashboard only.

### 5.4 Monitor Registration

Register all four monitors in `src/index.ts` during startup. They load their config from the `pba` section of `jarvis-monitors.json`. The PBA API URL and key are passed to each monitor constructor.

## 6. Jarvis HUD -- Dashboard Integration

### 6.1 BFF Proxy Routes

New Next.js Route Handlers in `app/api/pba/`:

**`app/api/pba/status/route.ts`** (GET)
- Calls PBA `/api/briefing/summary` with API key server-side
- Returns aggregated PBA status for MetricCards and BriefingPanel
- Cached: 30 seconds (ISR)

**`app/api/pba/pipeline/route.ts`** (GET)
- Proxies the SSE stream from PBA `/api/events/pipeline`
- Passes through Server-Sent Events transparently
- Holds PBA API key server-side

**`app/api/pba/calendar/route.ts`** (GET)
- Calls PBA `/api/calendar` with date range query params
- Returns scheduled content for the HUD calendar view

**`app/api/pba/engagement/route.ts`** (GET)
- Calls PBA `/api/analytics/engagement-summary`
- Returns engagement metrics for the HUD engagement panel

**`app/api/pba/trends/route.ts`** (GET)
- Calls PBA `/api/trends`
- Returns trending topics for the HUD trends panel

### 6.2 Zustand Store

New `stores/pba-store.ts` with state slices:
- `pipelineItems: PipelineItem[]` -- current items in pipeline (updated via SSE)
- `calendarItems: CalendarItem[]` -- scheduled content
- `engagementMetrics: EngagementMetrics | null` -- latest engagement data
- `trendingTopics: TrendTopic[]` -- current trends
- `briefingSummary: BriefingSummary | null` -- aggregated status for cards

Actions:
- `updatePipelineItem(event)` -- process SSE events, update/add/remove items immutably
- `setCalendarItems(items)` -- replace calendar data
- `setEngagementMetrics(metrics)` -- replace engagement data
- `setTrendingTopics(topics)` -- replace trends
- `setBriefingSummary(summary)` -- replace briefing data

### 6.3 Existing Panel Integration

**MetricCards (main dashboard `page.tsx`):**

Add three PBA-derived metric cards to the existing dashboard:
- "Content Queue" -- `briefingSummary.queueDepth` with trend indicator
- "Scheduled Today" -- `briefingSummary.scheduledToday.length`
- "Engagement" -- `briefingSummary.engagementHighlights` with 7-day trend

These use the same `MetricCard` component, sourced from the `/api/pba/status` endpoint polled alongside monitor data.

**BriefingPanel:**

Extend the briefing template to include PBA section:
- "Content: N posts scheduled today, M drafts pending review"
- "Engagement: [highlights from engagement summary]"
- "Trends: N new trending topics matching your brand"
- "Queue: N items in pipeline, next publish at HH:MM"

Data comes from `/api/pba/status` (briefing summary endpoint).

**AlertFeed:**

PBA alerts from jarvis-monitor already flow through the standard alert system. They appear in the AlertFeed alongside infrastructure alerts. Add a `source` field to the alert display so users can filter by "pba" vs "infra".

### 6.4 Dedicated `/content` Page

New route at `app/(dashboard)/content/page.tsx` with four panels:

**Pipeline Panel (Client Component):**
- Connects to `/api/pba/pipeline` SSE stream on mount
- Renders a Kanban-style board showing content flowing through stages
- Each card shows: title, platform icon, time in stage, content type
- Updates in real-time as SSE events arrive
- Uses `usePbaStore` for pipeline items

**Calendar Panel (Client Component):**
- Fetches from `/api/pba/calendar` with date range
- Renders weekly/monthly grid view
- Platform icons and content type indicators on each slot
- Date range selector updates the view

**Engagement Panel (Client Component):**
- Fetches from `/api/pba/engagement`
- Platform-by-platform metric cards (total engagement, growth)
- Top-performing content list with engagement numbers
- Recharts line chart for engagement trend over time

**Trends Panel (Client Component):**
- Fetches from `/api/pba/trends`
- Trending topic cards with relevance score bars
- Source attribution (Reddit, RSS, etc.)
- "Create content" action button per trend (links to PBA dashboard or triggers MCP tool)

### 6.5 Navigation

Add "Content" to the sidebar navigation (currently: Command, Alerts, Projects, Infra, Briefings). Uses the same `NavItem` component pattern. Icon: document or pen icon from Lucide React.

### 6.6 Type Definitions

Add PBA-specific types to `lib/types.ts`:

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

interface BriefingSummary {
  scheduledToday: CalendarItem[];
  engagementHighlights: EngagementHighlight[];
  trendingTopics: TrendTopic[];
  queueDepth: number;
  pendingApprovals: number;
}
```

## 7. OpenClaw Configuration

### 7.1 MCP Server Registration

Add PBA MCP server to the OpenClaw workspace configuration (`openclaw.json` or equivalent):

```json
{
  "mcpServers": {
    "pba": {
      "command": "dotnet",
      "args": ["/path/to/published/PersonalBrandAssistant.Api.dll", "--mcp"],
      "env": {
        "ConnectionStrings__DefaultConnection": "${PBA_DB_CONNECTION}",
        "ApiKey": "${PBA_API_KEY}"
      }
    }
  }
}
```

The `--mcp` argument triggers MCP mode in PBA's `Program.cs`, starting the stdio transport instead of the HTTP server. The binary is produced by `dotnet publish` and placed at a path accessible to the Jarvis stack.

### 7.2 Jarvis Persona Updates

Update `jarvis-persona/jarvis/tools.md` to document PBA capabilities:
- List all 13 MCP tools with descriptions and example invocations
- Note the autonomy dial behavior (some actions may queue for approval)
- Include PBA API URL in the infrastructure reference

### 7.3 Heartbeat Integration

Update `jarvis-persona/jarvis/heartbeat.md` to include PBA in the morning briefing schedule:
- Add a briefing step that calls `pba_get_calendar` and `pba_get_engagement_stats`
- Format PBA status into the briefing template

## 8. Cross-Cutting Concerns

### 8.1 Authentication

PBA uses **scoped API keys** to enforce least-privilege access:
- `pba-readonly` key: used by jarvis-monitor and jarvis-hud BFF. Can access read endpoints, SSE, and briefing data. Cannot invoke write operations.
- `pba-write` key: used by MCP tools (via OpenClaw). Can invoke all operations including create, publish, schedule, and respond.

The API key middleware checks the key's scope against the endpoint's required scope. Write endpoints require `pba-write`; read endpoints accept either scope.

Key storage:
- jarvis-monitor: `PBA_API_KEY` env var (readonly key)
- jarvis-hud: `.env.local` as `PBA_API_KEY` (readonly key), accessed only in Route Handlers
- OpenClaw Gateway: MCP server env configuration (write key)
- Keys are never sent to the browser

### 8.2 Error Handling

- jarvis-monitor: If PBA is unreachable, monitors return `status: "error"` with the connection error in details. The alert classifier routes this as critical.
- jarvis-hud: If PBA BFF proxy fails, the component shows a degraded state (skeleton + "PBA unavailable" message). SSE reconnects automatically with exponential backoff.
- MCP tools: Return structured error messages that the LLM can relay to the user. Never expose stack traces or internal details.

### 8.3 Timeouts and Retries

- jarvis-monitor HTTP calls: 5-second timeout, no retry (next tick will retry)
- jarvis-hud BFF proxy: 10-second timeout, 1 retry
- SSE connection: auto-reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s)
- MCP tools: 30-second timeout (matches OpenClaw's default)

### 8.4 Configuration

New environment variables across stacks:

PBA Stack:
- No new env vars (MCP mode detected by `--mcp` argument)

Jarvis Stack (`.env`):
- `PBA_API_URL` -- PBA API base URL (e.g., `http://192.168.50.x:5000`)
- `PBA_API_KEY` -- PBA API authentication key

### 8.5 Testing Strategy

**PBA (new endpoints + MCP tools):**
- Unit tests for each MCP tool class using mocked services
- Integration tests for new endpoints using `WebApplicationFactory<Program>` + in-memory DB
- SSE endpoint test verifying event stream format and reconnection behavior

**Jarvis Monitor (new monitors):**
- Unit tests for each monitor with mocked HTTP responses
- Tests for alert classification rules (PBA-specific severity mapping)
- Integration test with a mock PBA server

**Jarvis HUD (new components + routes):**
- Component tests for PBA panels with mocked store data
- Route Handler tests with mocked PBA API responses
- SSE client test verifying reconnection and state updates

## 9. Deployment Sequence

1. **PBA API changes first** -- deploy new endpoints + SSE + MCP support. Existing functionality unaffected (additive only).
2. **Jarvis Monitor** -- add PBA monitors and alert rules. Requires PBA to be reachable.
3. **Jarvis HUD** -- add BFF routes, store, and components. Requires PBA for data, monitor for alerts.
4. **OpenClaw Config** -- register MCP server. Requires PBA MCP support deployed.
5. **Jarvis Persona** -- update tools.md and heartbeat.md. Documentation-only change.

Each step is independently deployable and testable. If PBA API is down, Jarvis gracefully degrades (monitors report error, HUD shows unavailable state, MCP tools return error messages).
