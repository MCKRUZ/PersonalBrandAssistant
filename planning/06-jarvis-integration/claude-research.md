# Research Findings: Jarvis ↔ PBA Integration

## Part 1: Codebase Research

### 1. Personal Brand Assistant (PBA)

**API Architecture:**
- Minimal APIs with mapper functions (14 endpoint mappers in `Program.cs`)
- API Key middleware (`ApiKeyMiddleware`) for auth
- CORS configured for Angular dev (ports 4200, 4201)
- Health checks at `/health` and `/health/ready`

**Key Services:**
- **SidecarClient** (`SidecarClient.cs`): WebSocket connection to Claude sidecar (port 3001). Sends `send-message` frames, receives streamed events (`chat-event`, `file-change`, `status`, `session-update`, `error`). Session-based, one concurrent stream.
- **ContentPipeline** (`ContentPipeline.cs`): Orchestrates outline → draft → voice validation → approval. Uses SidecarClient for Claude invocations. Tracks token usage.
- **Platform Adapters** (`PlatformAdapterBase.cs`): Abstract base for social platforms. Methods: `PublishAsync`, `DeletePostAsync`, `GetEngagementAsync`, `GetProfileAsync`. Token management via `IOAuthManager`, rate limiting via `IRateLimiter`.
- **Agent Capability** (`IAgentCapability.cs`): Interface with `Type`, `DefaultModelTier`, `ExecuteAsync(AgentContext, ct)` → `Result<AgentOutput>`.
- **Agent Orchestrator** (`IAgentOrchestrator.cs`): `ExecuteAsync(AgentTask, ct)` → `Result<AgentExecutionResult>`.

**Docker Compose:**
- Services: db (PostgreSQL 17), sidecar (Claude), api (.NET on 5000→8080), freshrss, trendradar (optional), web (Angular on 4200)
- Networks: `internal` (bridge for internal comms), `default` (external)

### 2. Jarvis Monitor

**Monitor Interface:**
```typescript
interface Monitor {
  readonly id: string;
  readonly name: string;
  execute(ctx: TickContext): Promise<MonitorResult>;
}
```

**TickEngine:**
- Central scheduling with configurable intervals per monitor
- Jitter (0-30%) to prevent thundering herd
- 30-second timeout per execution
- Stores results to Redis: `jarvis:latest:{monitorId}` (hash), `jarvis:history:{monitorId}` (sorted set)

**Built-in Monitors:**
- `ServiceHealthMonitor`: HTTP health checks with latency tracking
- `DockerMonitor`: Running/total containers via Docker socket
- `GitHubCIMonitor`: GitHub Actions workflow status
- `GitHubPRMonitor`: Open/stale PR tracking

**Health Types:**
```typescript
Severity: "info" | "low" | "medium" | "high" | "critical"
MonitorStatus: "healthy" | "degraded" | "unhealthy" | "unknown" | "error"
MonitorResult: { monitorId, timestamp, status, severity, details, message }
```

**API (Hono):** Bearer token auth. Endpoints: `GET /health`, `GET /status`, `GET /status/:monitorId`, `GET /history/:monitorId`, `GET /config`.

**Config (`jarvis-monitors.json`):**
```json
{
  "defaults": { "level": "standard", "intervals": { "docker": "15m", "git": "30m", "health": "5m" } },
  "hosts": { "hostname": { "docker": {...}, "role": "primary", "services": [...] } },
  "projects": { "name": { "level": "deep", "github": {owner, repo}, "healthEndpoint": "..." } }
}
```

### 3. Jarvis HUD (Dashboard)

**BFF Proxy Pattern:**
- `/api/status` → proxies to jarvis-monitor (JARVIS_API_TOKEN server-side)
- `/api/chat` → proxies to OpenClaw Gateway (OPENCLAW_TOKEN server-side)

**Zustand Store:**
- `setMonitors(monitors: MonitorResult[])`, `setConnectionState()`, `setLastUpdate()`
- `useMonitorData` hook polls `/api/status` every 30s with connection state tracking

**Data Derivation (`page.tsx`):**
- Parses monitorId patterns: `github:owner/repo:ci`, `github:owner/repo:prs`, `service:hostname:serviceName`, `docker:*`
- Derives: MetricCards, AlertFeed, ProjectsGrid, BriefingPanel

**Components:** MetricCard, BriefingPanel, AlertFeed, ProjectsGrid, ChatPanel

### 4. Jarvis Voice Bridge

**OpenClaw Gateway Protocol (WebSocket JSON-RPC):**
1. Server sends `connect.challenge` with nonce
2. Client sends `connect` request with auth params
3. Server responds with handshake
4. Client sends `chat.send` with `{agentId, message, metadata}`
5. Receives streaming deltas, waits for `state: "final"`

**Session key format:** `agent:main:discord:dm:{user_id}`

### 5. Jarvis Stack

**Services:** openclaw-gateway (18789), docker-socket-proxy, redis (7-alpine), jarvis-monitor (3100), jarvis-hud (3200)
**Network:** `jarvis-net` bridge. HUD (3200) and OpenClaw (18789) exposed to host.

### 6. OpenClaw Gateway

**Tool Registration:** Two mechanisms:
- Built-in tools (file ops, exec, web) configured via `openclaw.json` allow/deny
- MCP servers for external API integration (primary mechanism for PBA)

**Agent Configuration:** Workspace-based. `jarvis-persona/` directory with identity files (SOUL.md, tools.md, heartbeat.md).

---

## Part 2: Web Research

### 1. OpenClaw Gateway Agent Tool Registration

**MCP Servers = Primary Integration Path**

Over 65% of active OpenClaw skills wrap MCP servers. Registration in `openclaw.json`:
```json
{
  "mcpServers": {
    "pba": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/mcp-server"],
      "env": { "PBA_API_URL": "http://localhost:5000", "PBA_API_KEY": "${PBA_API_KEY}" }
    }
  }
}
```

Gateway spawns MCP server processes, discovers tools automatically, routes calls via MCP protocol (JSON-RPC over stdio or HTTP/SSE).

**Skills Layer:** Markdown `SKILL.md` files teach repeatable workflows on top of tools. Can be user-invocable with gating rules for OS, binaries, env vars.

**Tool Call Flow:**
1. Agent receives message → LLM decides tool → Gateway checks policy → dispatches to MCP server → result returns via WebSocket events

### 2. MCP Server Implementation in .NET (2026)

**Official SDK:** `ModelContextProtocol` v1.1.0 (released March 2026). Three packages:
- `ModelContextProtocol.Core` — minimal, low-level
- `ModelContextProtocol` — hosting + DI extensions
- `ModelContextProtocol.AspNetCore` — HTTP transport

**Stdio Transport (for OpenClaw spawned servers):**
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
```

**HTTP Transport (for remote/networked):**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
var app = builder.Build();
app.MapMcp();
await app.RunAsync();
```

**Tool Definition Pattern:**
```csharp
[McpServerToolType]
public class ContentTools(IHttpClientFactory httpClientFactory)
{
    [McpServerTool(Name = "get_trending_topics", Title = "Get Trending Topics")]
    [Description("Retrieves current trending topics for a given platform")]
    public async Task<string> GetTrendingTopics(
        [Description("Platform: twitter, linkedin, reddit")] string platform,
        [Description("Max topics to return")] int limit = 10)
    {
        var client = httpClientFactory.CreateClient("PbaApi");
        var response = await client.GetFromJsonAsync<TrendingResult[]>($"/api/trends/{platform}?limit={limit}");
        return JsonSerializer.Serialize(response);
    }
}
```

**Key:** Detailed `[Description]` attributes are critical for LLM tool selection. Map REST endpoints 1:1 to MCP tools.

### 3. Cross-Service Docker Monitoring

**Health Check Patterns:**
- `/healthz` (liveness) — is the process alive?
- `/readyz` (readiness) — can it actually serve traffic?

**Cross-Stack Monitoring Options:**
1. **HTTP Polling** — monitor service polls external health endpoints (PBA at `http://host.docker.internal:5000/health`)
2. **Docker Socket** — mount Docker socket to inspect container health across stacks
3. **Shared External Network** — connect services across stacks via external Docker network

**Custom Monitor Pattern (Node.js):**
```javascript
class ExternalServiceMonitor {
  async checkService(service) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 5000);
    const response = await fetch(service.url, { signal: controller.signal });
    clearTimeout(timeout);
    return { name: service.name, healthy: response.ok, statusCode: response.status };
  }
}
```

### 4. Next.js 15 Dashboard Widgets

**File Structure:**
```
app/(dashboard)/
  layout.tsx        # Persistent shell
  page.tsx          # Default view
  content/page.tsx  # New PBA panel
```

**Component Composition:**
- Server Components (default) for data fetching
- Client Components (`'use client'`) for interactivity
- Suspense boundaries per widget for parallel streaming

**BFF Proxy (Route Handlers):**
```typescript
// app/api/pba/route.ts
export async function GET(request: NextRequest) {
  const [content, analytics] = await Promise.all([
    fetch(`${process.env.PBA_API_URL}/api/pipeline/status`, { headers: { 'x-api-key': process.env.PBA_API_KEY } }),
    fetch(`${process.env.PBA_API_URL}/api/analytics/summary`, { headers: { 'x-api-key': process.env.PBA_API_KEY } }),
  ]);
  return NextResponse.json({ pipeline: await content.json(), analytics: await analytics.json() });
}
```

**Zustand:** Separate stores per concern. Use `persist` middleware for preferences. Only Client Components can use stores.

---

## Part 3: Testing Infrastructure

### PBA Testing
- Framework: xUnit + Moq + `WebApplicationFactory<Program>`
- Database: In-memory EF Core for integration tests
- 628+ tests (138 Domain + 106 Application + 384 Infrastructure)
- Run: `dotnet test`

### Jarvis Monitor Testing
- Framework: Vitest
- Redis mocking for storage layer
- Custom monitors: simple `execute()` implementations
- Run: `npm test`

### Jarvis HUD Testing
- Framework: Vitest + React Testing Library
- Mock `/api/` endpoints via fetch mocking
- Zustand store testing via `getState()`
- Run: `npm test`

---

## Key Integration Decisions

1. **MCP Server is the primary integration mechanism** — build a .NET MCP server that wraps PBA's REST API, register it in OpenClaw's config
2. **Stdio transport for OpenClaw** (gateway spawns the process), **HTTP transport as optional** for direct access
3. **jarvis-monitor: HTTP polling** for PBA health (cross-stack, no shared network needed)
4. **jarvis-hud: BFF proxy + new page** with Server Components + Zustand for interactive widgets
5. **PBA API is additive only** — no changes to existing contracts, only new endpoints if needed
