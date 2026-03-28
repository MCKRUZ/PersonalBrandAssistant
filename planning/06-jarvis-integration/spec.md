# Jarvis ↔ PBA Integration

## Goal
Enable bidirectional communication between the Personal Brand Assistant (PBA) and the Jarvis AI assistant ecosystem, so Jarvis can command PBA capabilities and PBA can surface status/alerts through Jarvis's dashboard and notification infrastructure.

## Context

### PBA (Personal Brand Assistant)
- .NET 10 API (port 5000), Angular 19 dashboard (port 4200), PostgreSQL
- Content pipeline, repurposing, calendar, brand voice, trend monitoring, social engagement
- Claude sidecar for LLM orchestration (WebSocket)
- Docker Compose deployment

### Jarvis Ecosystem (5 repos)
- **jarvis-hud**: Next.js 15 dashboard (React 19, Zustand, Tailwind). BFF proxy pattern. Polls jarvis-monitor every 30s. Port 3200.
- **jarvis-monitor**: Node.js + Hono + Redis. Extensible tick-based monitor system (Docker, GitHub CI, PRs, Service Health). Alert classification + routing (Discord DM, dashboard, voice). Port 3100.
- **jarvis-stack**: Docker Compose orchestrating OpenClaw Gateway (port 18789), Redis, monitor, HUD, docker-socket-proxy.
- **jarvis-persona**: Agent identity config (SOUL.md, voice registers, heartbeat, tools). Read by OpenClaw DigitalHumanLoader.
- **jarvis-voice-bridge**: Python Discord bot. Speech → faster-whisper → OpenClaw Gateway (WebSocket JSON-RPC) → Chatterbox TTS → Discord voice.

### Communication Architecture
- OpenClaw Gateway = central agent brain (WebSocket JSON-RPC, token auth)
- Monitor → Redis (latest state + history sorted sets) → HUD (HTTP poll via BFF)
- Voice Bridge → OpenClaw Gateway (WebSocket, per-peer sessions)
- HUD Chat → OpenClaw Gateway (HTTP via BFF proxy)

## Integration Requirements

### 1. Jarvis Commands PBA (via OpenClaw Agent Tools)
Jarvis should be able to invoke PBA capabilities through natural language (chat or voice):
- "Schedule a LinkedIn post about X"
- "What's in my content queue?"
- "Draft a blog post on topic Y"
- "Show my engagement stats for the last week"
- "What trends are hot right now?"
- "Repurpose my latest blog post for Twitter"

This requires registering PBA API endpoints as tools accessible to the Jarvis agent via OpenClaw Gateway.

### 2. PBA Surfaces in Jarvis Dashboard (jarvis-hud)
Add PBA-specific panels/pages to the Jarvis HUD:
- Content queue status (pending, in-progress, published)
- Upcoming scheduled posts (calendar view or list)
- Engagement metrics (likes, shares, comments across platforms)
- Trend alerts (hot topics relevant to brand)
- Content pipeline health

### 3. Shared MCP Tools
Build an MCP server that exposes PBA capabilities as tools callable by both Jarvis (via OpenClaw) and Claude Code sessions:
- Content pipeline operations (create, status, publish)
- Calendar management (view, schedule, reschedule)
- Analytics queries (engagement, trends, performance)
- Social engagement actions (respond, like, bookmark)

### 4. Push Notifications (jarvis-monitor Integration)
Add PBA-specific monitors to jarvis-monitor:
- PBA API health check (service-health monitor type)
- Content pipeline status (custom monitor: items stuck, failed, queue depth)
- Engagement alerts (low engagement on recent posts, viral content detection)
- Calendar alerts (empty slots, overdue content, scheduling conflicts)
- Trend alerts (breaking trends matching brand topics)

These route through Jarvis's existing alert system (severity classification → Discord DM, dashboard, voice).

## Technical Constraints
- PBA runs in its own Docker Compose stack; Jarvis runs in jarvis-stack
- Cross-stack communication should go through HTTP APIs (not shared Docker networks)
- PBA API already has token-based auth (ApiKey header)
- jarvis-monitor expects monitors to implement the Monitor interface and return MonitorResult
- jarvis-hud uses Zustand stores and React components
- OpenClaw Gateway uses WebSocket JSON-RPC protocol
- MCP servers use stdio transport with JSON-RPC

## Non-Goals
- Merging the two Docker Compose stacks into one
- Replacing PBA's Angular dashboard with jarvis-hud
- Moving PBA's database to Redis
- Changing PBA's existing API contracts (additive only)
