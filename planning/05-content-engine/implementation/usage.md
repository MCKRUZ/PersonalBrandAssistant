# Content Engine — Usage Guide

## What Was Built

The Content Engine adds end-to-end content creation, repurposing, scheduling, brand voice validation, trend monitoring, and analytics to the Personal Brand Assistant. It spans 12 implementation sections across 294 files.

## API Endpoints

### Content Pipeline (`/api/content-pipeline`)
- `POST /create` — Create content from a topic (returns 201 with content ID)
- `POST /{id}/outline` — Generate an outline via sidecar LLM
- `POST /{id}/draft` — Generate a full draft
- `POST /{id}/submit` — Submit content for review

### Repurposing (`/api/repurposing`)
- `POST /{id}/repurpose` — Repurpose content to target platforms (body: `{ TargetPlatforms: [...] }`)
- `GET /{id}/suggestions` — Get repurposing suggestions
- `GET /{id}/tree` — Get the content derivation tree

### Calendar (`/api/calendar`)
- `GET ?from={date}&to={date}` — Get calendar slots in date range
- `POST /series` — Create a recurring content series (RRULE-based)
- `POST /slots` — Create manual calendar slots
- `POST /{slotId}/assign/{contentId}` — Assign content to a slot
- `POST /auto-fill?from={date}&to={date}` — Auto-fill empty slots (requires SemiAuto+ autonomy)

### Brand Voice (`/api/brand-voice`)
- `GET /score/{contentId}` — Score content against brand voice profile (returns composite + 3 subscores)

### Trends (`/api/trends`)
- `GET /suggestions?limit=20` — Get trend suggestions
- `POST /suggestions/{id}/accept` — Accept a suggestion (creates content)
- `POST /suggestions/{id}/dismiss` — Dismiss a suggestion
- `POST /refresh` — Refresh trends (requires SemiAuto+ autonomy)

### Analytics (`/api/analytics`)
- `GET /content/{id}` — Get performance report for content
- `GET /top?from={date}&to={date}&limit=10` — Get top performing content
- `POST /content/{id}/refresh` — Refresh engagement data (returns 202)

## Background Processors

Four background services run automatically:
1. **RepurposeOnPublishProcessor** — Auto-repurposes published content to series platforms (2-hour lookback)
2. **CalendarSlotProcessor** — Materializes RRULE occurrences into concrete slots, auto-fills if SemiAuto+
3. **TrendAggregationProcessor** — Polls HackerNews/Reddit/RSS for trends, scores relevance via LLM
4. **EngagementAggregationProcessor** — Fetches latest engagement metrics from platform APIs

## Configuration

### appsettings.json sections:
```json
{
  "Sidecar": { "WebSocketUrl": "ws://localhost:3001/ws" },
  "ContentEngine": { "BrandVoiceScoreThreshold": 70, "MaxAutoRegenerateAttempts": 3 },
  "TrendMonitoring": { "AggregationIntervalMinutes": 30 }
}
```

## Docker Services

- **pba-sidecar** — Claude Code sidecar (internal network only, no published ports)
- **pba-trendradar** — TrendRadar v0.3.0 (internal network only)
- **pba-freshrss** — FreshRSS v1.24.3 (port 8080 in dev override only)

## Key Patterns

- **Result<T>** — All service methods return `Result<T>` with `.ToHttpResult()` for API responses
- **Autonomy enforcement** — CalendarEndpoints.AutoFill and TrendEndpoints.Refresh check `AutonomyConfiguration.GlobalLevel`
- **Sidecar integration** — LLM calls go through `ISidecarClient.SendTaskAsync()`, not direct API
- **Idempotency** — Repurposing and calendar slot creation are idempotent
- **Tree depth limits** — Repurposing enforces `MaxTreeDepth` to prevent infinite derivation chains
