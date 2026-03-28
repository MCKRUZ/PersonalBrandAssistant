# Jarvis ↔ PBA Integration — Synthesized Specification

## Overview

Enable full bidirectional integration between the Personal Brand Assistant (PBA, .NET 10 + Angular 19) and the Jarvis AI assistant ecosystem (Node.js + Next.js + Python + OpenClaw Gateway). Jarvis becomes the control plane for PBA — invoking content operations via voice/chat, surfacing PBA status on its dashboard, receiving real-time alerts, and including PBA in morning briefings.

## Architecture Decisions

### MCP Server: Dual-Protocol in PBA API
The MCP server is built directly into `PersonalBrandAssistant.Api` as a dual-protocol server. The existing .NET 10 API adds MCP support using the official `ModelContextProtocol` SDK (v1.1.0) with stdio transport for OpenClaw Gateway spawning. This avoids a separate project while keeping the API as the single source of truth for PBA capabilities.

### Docker Networking: Host IP Isolation
PBA and Jarvis remain in separate Docker Compose stacks. Cross-stack communication uses the host's LAN IP (e.g., `192.168.50.x`). No shared Docker networks. This preserves full isolation and simplifies deployment on the Synology NAS.

### HUD Integration: Both Embedded + Dedicated
PBA data appears in existing jarvis-hud panels (MetricCards, AlertFeed, BriefingPanel) AND gets a dedicated `/content` page for deep-dive into content pipeline, calendar, engagement, and trends.

### Data Freshness: SSE Real-Time
The jarvis-hud PBA page receives real-time updates via Server-Sent Events from the PBA API, not polling. This gives instant feedback as content moves through the pipeline.

### Autonomy: Respect PBA's Dial
When Jarvis invokes PBA actions, the PBA autonomy dial determines whether the action executes immediately or queues for approval. No separate Jarvis-level confirmation layer.

## Integration Components

### 1. MCP Server (PBA API → OpenClaw Gateway)

**Purpose:** Expose PBA capabilities as MCP tools that OpenClaw's Jarvis agent can invoke via voice, chat, or automation.

**Tools to expose:**

Content Pipeline:
- `pba_create_content` — Start content pipeline (topic, platform, content type)
- `pba_get_pipeline_status` — Get status of content in pipeline (by ID or list all active)
- `pba_publish_content` — Publish approved content to target platform
- `pba_list_drafts` — List content drafts with status and metadata

Calendar:
- `pba_get_calendar` — Get scheduled content for date range
- `pba_schedule_content` — Schedule content for a specific date/time/platform
- `pba_reschedule_content` — Move scheduled content to a new slot

Trends & Analytics:
- `pba_get_trends` — Get current trending topics with relevance scores
- `pba_get_engagement_stats` — Get engagement metrics for date range and platform
- `pba_get_content_performance` — Get performance data for specific published content

Social Engagement:
- `pba_get_opportunities` — Get engagement opportunities (comments to reply to, mentions, etc.)
- `pba_respond_to_opportunity` — Draft/send response to an engagement opportunity
- `pba_get_inbox` — Get social inbox (mentions, DMs, comments)

**Transport:** Stdio (OpenClaw Gateway spawns the process). The MCP server connects to the PBA API via localhost/LAN IP using `IHttpClientFactory`.

**Auth:** The MCP server passes the PBA API key in the `x-api-key` header.

### 2. Jarvis Monitor — PBA Monitors

**Purpose:** Add PBA-specific monitors to jarvis-monitor that feed into the existing alert classification and routing system.

**New Monitors:**

`PbaHealthMonitor` (ServiceHealth type):
- Checks PBA API `/health/ready` endpoint
- Interval: 5 minutes
- Severity: critical if unreachable, degraded if slow (>2s)

`PbaContentQueueMonitor` (custom):
- Calls PBA API `/api/content/queue-status` (new endpoint)
- Reports: queue depth, next scheduled post, posts in last 24h
- Severity: medium if no content scheduled in next 48h, low if queue depth < 3
- Interval: 15 minutes

`PbaEngagementMonitor` (custom):
- Calls PBA API `/api/analytics/engagement-summary` (new endpoint)
- Detects: viral posts (engagement > 2x average), low engagement (< 50% of average)
- Severity: high for anomalies (both viral and low)
- Interval: 30 minutes

`PbaPipelineMonitor` (custom):
- Calls PBA API `/api/content/pipeline-health` (new endpoint)
- Reports: stuck items (in same stage > 2h), failed generations, error rate
- Severity: high if items stuck or error rate > 10%
- Interval: 10 minutes

**Config:** Added to `jarvis-monitors.json` under a new `pba` section with PBA API URL and key.

### 3. Jarvis HUD — PBA Dashboard Integration

**Purpose:** Surface PBA status and data in the Jarvis dashboard with both overview integration and a dedicated deep-dive page.

**Existing Panel Integration:**

MetricCards (on main dashboard):
- "Content Queue" — count of items in pipeline (from PBA monitor)
- "Scheduled Posts" — count of upcoming scheduled content
- "Engagement Score" — rolling 7-day engagement trend

AlertFeed:
- PBA alerts appear alongside infrastructure alerts (same severity system)
- Tagged with `source: "pba"` for filtering

BriefingPanel (morning briefing):
- "Content: 3 posts scheduled today, 2 drafts pending review"
- "Engagement: LinkedIn post from yesterday at 2.1x average, 47 new comments"
- "Trends: 2 new trending topics matching your brand"
- "Queue: 5 items in pipeline, next publish in 3 hours"

**Dedicated `/content` Page:**

Content Pipeline Panel:
- Real-time pipeline status (SSE) showing items flowing through stages
- Stage breakdown: Ideation → Outline → Draft → Voice Check → Approval → Scheduled → Published
- Click-through to content details

Calendar Panel:
- Weekly/monthly view of scheduled content across platforms
- Platform icons and content type indicators
- Drag-to-reschedule (calls PBA API via BFF)

Engagement Panel:
- Platform-by-platform engagement metrics
- Top-performing content with trend lines
- Engagement opportunities list

Trends Panel:
- Current trending topics with relevance scores
- Trend-to-content suggestions

**BFF Proxy Routes (Next.js):**
- `GET /api/pba/status` — aggregated PBA status (pipeline, calendar, engagement)
- `GET /api/pba/pipeline` — SSE stream of pipeline events
- `GET /api/pba/calendar` — scheduled content for date range
- `GET /api/pba/engagement` — engagement metrics
- `GET /api/pba/trends` — trending topics

**State Management:** New `pba-store.ts` Zustand store for PBA-specific client state.

### 4. PBA API — New Endpoints for Integration

**SSE Endpoint:**
- `GET /api/events/pipeline` — Server-Sent Events stream for pipeline status changes

**Monitor-Facing Endpoints (lightweight, for jarvis-monitor):**
- `GET /api/content/queue-status` — queue depth, next scheduled, recent publishes
- `GET /api/content/pipeline-health` — stuck items, error rate, stage breakdown
- `GET /api/analytics/engagement-summary` — rolling metrics, anomaly flags

**Briefing Endpoint:**
- `GET /api/briefing/summary` — pre-formatted briefing data for Jarvis

### 5. OpenClaw Configuration

**`openclaw.json` additions:**
- Register PBA MCP server under `mcpServers.pba`
- Add PBA-related skills (optional, for workflow orchestration)

**`jarvis-persona/tools.md` updates:**
- Document PBA capabilities and when to use them
- Add PBA context to Jarvis's tool awareness

## Technical Constraints

- PBA API key stored as environment variable, never hardcoded
- All new PBA endpoints follow existing Minimal API patterns
- MCP tools use `[Description]` attributes extensively for LLM tool selection
- jarvis-monitor custom monitors implement the `Monitor` interface
- jarvis-hud components follow Server Component + Client Component composition
- SSE endpoint uses .NET's `IAsyncEnumerable<T>` pattern
- No changes to existing PBA API contracts (additive only)
- Cross-stack HTTP calls include timeout and retry logic

## Non-Goals

- Merging Docker Compose stacks
- Replacing PBA's Angular dashboard
- Moving PBA data to Redis
- Changing existing PBA API contracts
- Building a custom protocol (use MCP standard)
