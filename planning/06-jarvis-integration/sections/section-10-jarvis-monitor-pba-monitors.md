# Section 10: Jarvis Monitor -- PBA Custom Monitors

## Overview

Four new monitors added to the jarvis-monitor service (Node.js + Redis) that poll PBA's REST API endpoints to track health, content queue status, engagement anomalies, and pipeline health. These monitors follow jarvis-monitor's existing `Monitor` interface pattern and feed results into the standard alert system.

The monitors consume the PBA endpoints built in section 01 (monitor-facing endpoints). They communicate over the host LAN IP since PBA and Jarvis run as independent Docker Compose stacks on the same Synology NAS.

Alert classification rules map PBA monitor results to the existing severity system, which routes alerts through Discord DM, dashboard, and voice channels based on severity level.

## Dependencies

- **section-01-pba-monitor-endpoints**: The four PBA REST endpoints must be deployed and reachable:
  - `GET /api/content/queue-status`
  - `GET /api/content/pipeline-health`
  - `GET /api/analytics/engagement-summary`
  - `GET /api/briefing/summary` (used indirectly for health check context)
  - `GET /health/ready` (existing health endpoint)

## Jarvis Monitor Architecture Context

jarvis-monitor is a Node.js service that runs monitor classes on configurable intervals. Each monitor implements a `Monitor` interface returning a `MonitorResult` with status, severity, and details. Results are stored in Redis and trigger the alert classifier when status changes. Configuration lives in `jarvis-monitors.json`.

The existing `Monitor` interface:

```typescript
interface Monitor {
  name: string;
  check(): Promise<MonitorResult>;
}

interface MonitorResult {
  status: "healthy" | "degraded" | "unhealthy" | "error";
  severity: "info" | "medium" | "high" | "critical";
  details: Record<string, unknown>;
  timestamp: string;
}
```

## Tests (Write First)

### PbaHealthMonitor Tests

Test file: `src/monitors/pba/__tests__/pba-health-monitor.test.ts`

Use Vitest. Mock HTTP responses from the PBA `/health/ready` endpoint.

```typescript
// Test: returns healthy when PBA responds 200 with healthy status
//   Mock fetch to return 200 with { status: "Healthy" }
//   Assert result.status === "healthy"
//   Assert result.severity === "info"

// Test: returns degraded when PBA responds 200 but degraded
//   Mock fetch to return 200 with { status: "Degraded" }
//   Assert result.status === "degraded"
//   Assert result.severity === "medium"

// Test: returns degraded when response takes > 2s
//   Mock fetch to resolve after 2100ms with 200/healthy
//   Assert result.status === "degraded"
//   Assert result.severity === "medium"
//   Assert result.details includes latency value

// Test: returns unhealthy/critical when PBA is unreachable
//   Mock fetch to throw a connection error
//   Assert result.status === "unhealthy"
//   Assert result.severity === "critical"

// Test: returns error when connection times out
//   Mock fetch to abort after 5s timeout
//   Assert result.status === "error"
//   Assert result.details includes timeout information

// Test: includes latency in details
//   Mock fetch to return 200 in ~150ms
//   Assert result.details.latencyMs is a number > 0
```

### PbaContentQueueMonitor Tests

Test file: `src/monitors/pba/__tests__/pba-content-queue-monitor.test.ts`

```typescript
// Test: returns healthy when hasScheduledIn48h and queueDepth >= 3
//   Mock /api/content/queue-status to return:
//     { queueDepth: 5, nextScheduledPost: { scheduledAt: "tomorrow" }, postsLast24h: 2, itemsByStage: {...} }
//   Assert result.status === "healthy"
//   Assert result.severity === "info"

// Test: returns healthy with info note when hasScheduledIn48h and queueDepth < 3
//   Mock response with queueDepth: 1, nextScheduledPost within 48h
//   Assert result.status === "healthy"
//   Assert result.severity === "info"
//   Assert result.details includes "low buffer" note

// Test: returns degraded/medium when no scheduled in 48h and queueDepth > 0
//   Mock response with queueDepth: 2, nextScheduledPost: null
//   Assert result.status === "degraded"
//   Assert result.severity === "medium"

// Test: returns degraded/medium when no scheduled in 48h and queueDepth == 0
//   Mock response with queueDepth: 0, nextScheduledPost: null
//   Assert result.status === "degraded"
//   Assert result.severity === "medium"

// Test: includes queueDepth, nextScheduledPost, itemsByStage in details
//   Assert all fields from the API response are passed through in result.details

// Test: returns error when PBA API unreachable
//   Mock fetch to throw connection error
//   Assert result.status === "error"
//   Assert result.details includes connection error message
```

### PbaEngagementMonitor Tests

Test file: `src/monitors/pba/__tests__/pba-engagement-monitor.test.ts`

```typescript
// Test: returns healthy when no anomalies
//   Mock /api/analytics/engagement-summary to return { anomalies: [], ... }
//   Assert result.status === "healthy"
//   Assert result.severity === "info"

// Test: returns healthy when only positive anomalies (viral)
//   Mock with anomalies: [{ direction: "positive", confidence: 0.85, ... }]
//   Assert result.status === "healthy"
//   Assert result.severity === "info"
//   Assert result.details includes anomaly highlights for briefing

// Test: returns degraded/high when negative anomalies present
//   Mock with anomalies: [{ direction: "negative", confidence: 0.9, ... }]
//   Assert result.status === "degraded"
//   Assert result.severity === "high"

// Test: includes anomaly direction and confidence in details
//   Mock with mixed anomalies (positive and negative)
//   Assert result.details.anomalies contains direction and confidence fields

// Test: includes platform breakdown in details
//   Mock with platformBreakdown: { twitter: 500, linkedin: 300 }
//   Assert result.details.platformBreakdown matches

// Test: returns error when PBA API unreachable
//   Mock fetch to throw connection error
//   Assert result.status === "error"
```

### PbaPipelineMonitor Tests

Test file: `src/monitors/pba/__tests__/pba-pipeline-monitor.test.ts`

```typescript
// Test: returns healthy when no stuck items and error rate < 10%
//   Mock /api/content/pipeline-health to return:
//     { stuckItems: [], failedGenerations24h: 1, errorRate: 0.05, activeCount: 10 }
//   Assert result.status === "healthy"

// Test: returns degraded/high when stuck items present
//   Mock with stuckItems: [{ contentId: "...", stage: "Draft", hoursStuck: 4 }]
//   Assert result.status === "degraded"
//   Assert result.severity === "high"

// Test: returns degraded/high when error rate >= 10%
//   Mock with stuckItems: [], errorRate: 0.15
//   Assert result.status === "degraded"
//   Assert result.severity === "high"

// Test: includes stuck item list in details
//   Mock with 2 stuck items
//   Assert result.details.stuckItems has length 2 and correct fields

// Test: includes error rate and active count in details
//   Assert result.details.errorRate and result.details.activeCount match API response

// Test: returns error when PBA API unreachable
//   Mock fetch to throw connection error
//   Assert result.status === "error"
```

### Alert Classification Tests

Test file: `src/alerts/__tests__/pba-alert-classification.test.ts`

```typescript
// Test: pba:health unhealthy maps to critical severity
//   Feed a MonitorResult with name "pba:health", status "unhealthy"
//   Assert classified severity === "critical"

// Test: pba:pipeline degraded maps to high severity
//   Feed MonitorResult with name "pba:pipeline", status "degraded"
//   Assert classified severity === "high"

// Test: pba:engagement degraded with negative anomaly maps to high severity
//   Feed MonitorResult with name "pba:engagement", status "degraded"
//   Assert classified severity === "high"

// Test: pba:content-queue degraded maps to medium severity
//   Feed MonitorResult with name "pba:content-queue", status "degraded"
//   Assert classified severity === "medium"

// Test: pba:engagement healthy with positive anomaly does NOT trigger alert
//   Feed MonitorResult with name "pba:engagement", status "healthy"
//   Assert no alert is generated (result is informational only)
```

## File Paths

### New Files

- `src/monitors/pba/pba-health-monitor.ts` -- PbaHealthMonitor class.
- `src/monitors/pba/pba-content-queue-monitor.ts` -- PbaContentQueueMonitor class.
- `src/monitors/pba/pba-engagement-monitor.ts` -- PbaEngagementMonitor class.
- `src/monitors/pba/pba-pipeline-monitor.ts` -- PbaPipelineMonitor class.
- `src/monitors/pba/__tests__/pba-health-monitor.test.ts` -- Tests.
- `src/monitors/pba/__tests__/pba-content-queue-monitor.test.ts` -- Tests.
- `src/monitors/pba/__tests__/pba-engagement-monitor.test.ts` -- Tests.
- `src/monitors/pba/__tests__/pba-pipeline-monitor.test.ts` -- Tests.
- `src/alerts/__tests__/pba-alert-classification.test.ts` -- Alert classification tests.

### Modified Files

- `src/index.ts` -- Register all four PBA monitors during startup.
- `src/alerts/classifier.ts` -- Add PBA-specific alert classification rules.
- `jarvis-monitors.json` -- Add `pba` configuration section.

## Monitor Implementations

### PbaHealthMonitor

Extends the existing `ServiceHealthMonitor` pattern. Hits PBA's `/health/ready` endpoint.

Poll interval: 5 minutes.

Status mapping:

| PBA Response | Monitor Status | Severity |
|---|---|---|
| 200 + healthy | `healthy` | `info` |
| 200 + degraded | `degraded` | `medium` |
| 200 but latency > 2s | `degraded` | `medium` |
| Unreachable / connection refused | `unhealthy` | `critical` |
| Timeout (> 5s) | `error` | `critical` |

Details always include `latencyMs` (the round-trip time in milliseconds). When degraded due to latency, the details also include `reason: "slow_response"`.

### PbaContentQueueMonitor

Calls PBA's `/api/content/queue-status` endpoint.

Poll interval: 15 minutes.

The "hasScheduledIn48h" check is derived from the `nextScheduledPost` field. If `nextScheduledPost` is non-null and its `scheduledAt` is within the next 48 hours, the condition is true.

Status mapping (explicit truth table):

| HasScheduledIn48h | QueueDepth | Status | Severity | Notes |
|---|---|---|---|---|
| true | >= 3 | `healthy` | `info` | Good buffer |
| true | < 3 | `healthy` | `info` | Details note: "low buffer" |
| false | > 0 | `degraded` | `medium` | Items exist but nothing scheduled |
| false | 0 | `degraded` | `medium` | Empty queue and nothing scheduled |

Details include all fields from the API response: `queueDepth`, `nextScheduledPost`, `postsLast24h`, `itemsByStage`.

### PbaEngagementMonitor

Calls PBA's `/api/analytics/engagement-summary` endpoint.

Poll interval: 30 minutes.

The anomaly objects from the API include `direction: "positive" | "negative"` and a `confidence` score (0.0 to 1.0). Only negative anomalies (low engagement) trigger degraded status. Positive anomalies (viral posts) are surfaced as highlights in the details for the briefing panel, not as alerts.

Status mapping:

| Anomalies | Status | Severity | Action |
|---|---|---|---|
| None | `healthy` | `info` | No alert |
| Only positive (viral) | `healthy` | `info` | Highlights in details for briefing |
| Negative present | `degraded` | `high` | Alert generated |

Details include: `rolling7DayEngagement`, `averageEngagement`, `platformBreakdown`, and the full `anomalies` array with direction, confidence, platform, metric, value, and average.

### PbaPipelineMonitor

Calls PBA's `/api/content/pipeline-health` endpoint.

Poll interval: 10 minutes.

Status mapping:

| Stuck Items | Error Rate | Status | Severity |
|---|---|---|---|
| 0 | < 10% | `healthy` | `info` |
| > 0 | any | `degraded` | `high` |
| 0 | >= 10% | `degraded` | `high` |
| > 0 | >= 10% | `degraded` | `high` |

Details include: `stuckItems` array (with contentId, stage, stuckSince, hoursStuck), `failedGenerations24h`, `errorRate`, `activeCount`.

## Configuration

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

The `apiUrl` points to PBA's HTTP API on the LAN. The `apiKey` uses the `pba-readonly` scoped key (sufficient for all read endpoints). The `${PBA_API_KEY}` token is resolved from the environment variable at startup.

Each monitor constructor receives the `apiUrl` and `apiKey` from this configuration block.

## Alert Classification Rules

Add PBA-specific rules to `src/alerts/classifier.ts`:

| Monitor Name | Status | Classified Severity | Routing |
|---|---|---|---|
| `pba:health` | unhealthy | `critical` | Discord DM + dashboard + voice |
| `pba:pipeline` | degraded | `high` | Discord DM + dashboard |
| `pba:engagement` | degraded | `high` | Discord DM + dashboard |
| `pba:content-queue` | degraded | `medium` | Dashboard only |

Alert routing follows the existing jarvis-monitor patterns. No new routing logic is needed -- just new classification entries.

## Monitor Registration

In `src/index.ts`, register all four monitors during startup. They load their config from the `pba` section of `jarvis-monitors.json`:

```typescript
// Pseudocode for registration pattern:
const pbaConfig = config.pba;
if (pbaConfig) {
  registerMonitor(new PbaHealthMonitor(pbaConfig.apiUrl, pbaConfig.apiKey), pbaConfig.intervals.health);
  registerMonitor(new PbaContentQueueMonitor(pbaConfig.apiUrl, pbaConfig.apiKey), pbaConfig.intervals.contentQueue);
  registerMonitor(new PbaEngagementMonitor(pbaConfig.apiUrl, pbaConfig.apiKey), pbaConfig.intervals.engagement);
  registerMonitor(new PbaPipelineMonitor(pbaConfig.apiUrl, pbaConfig.apiKey), pbaConfig.intervals.pipeline);
}
```

If the `pba` section is absent from the config, the monitors are simply not registered. This keeps the integration opt-in.

## Error Handling

All four monitors handle PBA unreachability the same way:

- If the HTTP request fails (connection refused, DNS failure), the monitor returns `status: "error"` with the connection error message in details.
- If the HTTP request times out (> 5 seconds), the monitor returns `status: "error"` with timeout information.
- If the HTTP response is non-200, the monitor returns `status: "error"` with the status code in details.

The alert classifier routes `pba:health` errors as `critical`. Other monitor errors are treated as `high` severity since they likely indicate PBA is partially or fully down.

## HTTP Client Configuration

Each monitor uses a shared HTTP client (or fetch wrapper) configured with:
- 5-second timeout
- No retry (the next poll interval will retry automatically)
- API key passed in the `X-Api-Key` header
- User-Agent: `jarvis-monitor/1.0`

## Implementation Notes

- The monitors are in a new `monitors/pba/` directory to keep them grouped and isolated from infrastructure monitors.
- The `pba:` prefix in monitor names distinguishes PBA alerts from infrastructure alerts in the alert feed and dashboard.
- The 48-hour window for the content queue check is a heuristic. It surfaces a warning early enough to create and schedule new content before a gap appears.
- Engagement anomaly detection thresholds (>2x and <0.5x) are defined in the PBA API's engagement summary endpoint (section 01), not in the monitor. The monitor simply reads the anomaly list from the API response.
- Monitor names follow the existing naming convention: `pba:health`, `pba:content-queue`, `pba:engagement`, `pba:pipeline`.
