<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-pba-monitor-endpoints
section-02-sse-broadcaster
section-03-scoped-api-keys
section-04-mcp-server-infrastructure
section-05-mcp-content-pipeline-tools
section-06-mcp-calendar-tools
section-07-mcp-analytics-tools
section-08-mcp-social-tools
section-09-mcp-idempotency-audit
section-10-jarvis-monitor-pba-monitors
section-11-jarvis-hud-bff-store
section-12-jarvis-hud-panels-page
section-13-openclaw-persona-config
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-pba-monitor-endpoints | - | 10, 11 | Yes |
| section-02-sse-broadcaster | - | 11, 12 | Yes |
| section-03-scoped-api-keys | - | 04-09, 10, 11 | Yes |
| section-04-mcp-server-infrastructure | 03 | 05-09 | No |
| section-05-mcp-content-pipeline-tools | 04 | 09, 13 | Yes |
| section-06-mcp-calendar-tools | 04 | 09, 13 | Yes |
| section-07-mcp-analytics-tools | 04 | 09, 13 | Yes |
| section-08-mcp-social-tools | 04 | 09, 13 | Yes |
| section-09-mcp-idempotency-audit | 05, 06, 07, 08 | 13 | No |
| section-10-jarvis-monitor-pba-monitors | 01 | 12 | Yes |
| section-11-jarvis-hud-bff-store | 01, 02 | 12 | No |
| section-12-jarvis-hud-panels-page | 10, 11 | - | No |
| section-13-openclaw-persona-config | 05, 06, 07, 08 | - | No |

## Execution Order

1. **Batch 1** (no dependencies): section-01-pba-monitor-endpoints, section-02-sse-broadcaster, section-03-scoped-api-keys
2. **Batch 2** (after 03): section-04-mcp-server-infrastructure
3. **Batch 3** (after 04): section-05-mcp-content-pipeline-tools, section-06-mcp-calendar-tools, section-07-mcp-analytics-tools, section-08-mcp-social-tools
4. **Batch 4** (after 05-08): section-09-mcp-idempotency-audit
5. **Batch 5** (after 01, 02): section-10-jarvis-monitor-pba-monitors, section-11-jarvis-hud-bff-store
6. **Batch 6** (after 10, 11): section-12-jarvis-hud-panels-page
7. **Batch 7** (after 05-09): section-13-openclaw-persona-config

## Section Summaries

### section-01-pba-monitor-endpoints
New lightweight REST endpoints in PBA API for jarvis-monitor consumption: `/api/content/queue-status`, `/api/content/pipeline-health`, `/api/analytics/engagement-summary`, `/api/briefing/summary`. Includes service layer logic for aggregation and anomaly detection.

### section-02-sse-broadcaster
PipelineEventBroadcaster service with broadcast hub pattern. SSE endpoint at `/api/events/pipeline` with initial snapshot on connect and real-time delta streaming. Pipeline service integration to emit events.

### section-03-scoped-api-keys
Scoped API key middleware supporting `pba-readonly` and `pba-write` scopes. Endpoint scope enforcement. Key validation and scope checking.

### section-04-mcp-server-infrastructure
MCP SDK integration in PBA API project. `--mcp` flag detection for dual-mode startup. Stdio transport registration. Assembly scanning for tool discovery. Published binary build configuration.

### section-05-mcp-content-pipeline-tools
MCP tool class `ContentPipelineTools` with 4 tools: `pba_create_content`, `pba_get_pipeline_status`, `pba_publish_content`, `pba_list_drafts`. Autonomy dial integration for write operations.

### section-06-mcp-calendar-tools
MCP tool class `CalendarTools` with 3 tools: `pba_get_calendar`, `pba_schedule_content`, `pba_reschedule_content`. Calendar conflict validation.

### section-07-mcp-analytics-tools
MCP tool class `AnalyticsTools` with 3 tools: `pba_get_trends`, `pba_get_engagement_stats`, `pba_get_content_performance`.

### section-08-mcp-social-tools
MCP tool class `SocialEngagementTools` with 3 tools: `pba_get_opportunities`, `pba_respond_to_opportunity`, `pba_get_inbox`. Autonomy dial for response sending.

### section-09-mcp-idempotency-audit
Cross-cutting concerns for all MCP write tools: `clientRequestId`-based idempotency with 5-minute TTL cache, audit trail logging with actor/tool/outcome/correlation tracking.

### section-10-jarvis-monitor-pba-monitors
Four new monitors in jarvis-monitor: `PbaHealthMonitor`, `PbaContentQueueMonitor`, `PbaEngagementMonitor`, `PbaPipelineMonitor`. Alert classification rules. Configuration in `jarvis-monitors.json`.

### section-11-jarvis-hud-bff-store
Next.js BFF proxy routes (`/api/pba/*`). Zustand `pba-store.ts` with pipeline items, calendar, engagement, trends, and briefing state. SSE client hook for real-time pipeline updates.

### section-12-jarvis-hud-panels-page
Existing panel integration (MetricCards, BriefingPanel, AlertFeed with PBA data). Dedicated `/content` page with Pipeline, Calendar, Engagement, and Trends panels. Navigation sidebar update.

### section-13-openclaw-persona-config
OpenClaw `openclaw.json` MCP server registration for PBA. Jarvis persona `tools.md` update documenting PBA capabilities. Heartbeat briefing schedule update.
