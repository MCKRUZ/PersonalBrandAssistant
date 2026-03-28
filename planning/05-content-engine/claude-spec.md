# 05 — Content Engine: Synthesized Specification

## Overview

The Content Engine is the content creation, repurposing, calendar, brand voice, trend monitoring, and analytics layer. It also includes a **full architectural migration** of the agent orchestration layer (phase 03) from the Anthropic SDK to the claude-code-sidecar, plus Docker Compose configuration for the full self-hosted stack.

## Scope Changes from Original Spec

The scope has expanded significantly based on interview findings:

1. **Sidecar Migration (phase 03 rewrite):** All AI interactions move from direct Anthropic SDK (`IChatClientFactory`, `IChatClient`) to the claude-code-sidecar WebSocket API. The sidecar wraps Claude Code CLI and provides full agent capabilities (file editing, bash, MCP tools, session persistence).

2. **Full Agent Mode:** The sidecar operates in full agent mode — it edits files directly, commits to git repos, and writes blog HTML. Content generation is not just "return text" but "perform the task end-to-end."

3. **Docker Compose:** Configuration for running the sidecar, TrendRadar, and FreshRSS alongside the .NET app and PostgreSQL.

---

## Architecture: Claude Code Sidecar Integration

### Sidecar Overview

The claude-code-sidecar (`C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\claude-code-sidecar`) is a TypeScript monorepo:
- **`packages/core/`** — Framework-agnostic library: SubprocessManager spawns `claude -p "task" --output-format stream-json`
- **`packages/web/`** — Next.js 15 + custom HTTP server with WebSocket at `localhost:3001`

**WebSocket Protocol:**
- Client → Server: `{ type: "send-message", payload: { message } }`, `{ type: "new-session" }`, `{ type: "abort" }`
- Server → Client: `{ type: "chat-event", payload: ChatEvent }`, `{ type: "file-change", payload }`, `{ type: "status", payload }`, `{ type: "session-update", payload }`

**Auth:** Delegated to Claude Code's own credentials. Sidecar never manages API keys.

**Session Persistence:** Uses `--resume <sessionId>` for context continuity across messages.

### .NET Integration Layer

Replace `IChatClientFactory` / `IChatClient` with a new `ISidecarClient` abstraction:

```
ISidecarClient
  ├── ConnectAsync() — Establish WebSocket connection
  ├── SendTaskAsync(task, sessionId?, ct) — Send task, return streaming events
  ├── AbortAsync() — Cancel running task
  ├── NewSessionAsync() — Create fresh session
  └── Events: OnChatEvent, OnFileChange, OnStatus, OnError
```

The `IAgentOrchestrator` refactors to:
- Build task prompts (same template system, same brand voice injection)
- Send prompts to sidecar via WebSocket
- Parse streaming events for token tracking, progress updates, completion
- Map sidecar events to existing `AgentExecution` entity updates

### What Changes in Phase 03

| Component | Current (SDK) | New (Sidecar) |
|-----------|--------------|---------------|
| `IChatClientFactory` | Creates Anthropic SDK clients | **Replaced by `ISidecarClient`** |
| `IChatClient` | Direct API message calls | **WebSocket task dispatch** |
| `AgentCapabilityBase` | Renders prompt → calls API → parses response | Renders prompt → sends to sidecar → streams events |
| Token tracking | Parsed from API `UsageDetails` | Parsed from sidecar `result` events (`usage.input_tokens`, `usage.output_tokens`) |
| Model selection | Model ID in API call | Model selected by Claude Code based on task (or configurable via CLAUDE.md) |
| Retry logic | Application-level retry with backoff | Claude Code handles retries internally |
| Budget tracking | Pre-execution budget check | Still application-level — parse token usage from sidecar events |

### What Stays the Same

- `IAgentCapability` interface and capability types (Writer, Social, Repurpose, Engagement, Analytics)
- `IPromptTemplateService` for prompt building
- `AgentExecution` entity for tracking
- `ITokenTracker` for cost management
- `BrandProfilePromptModel` for brand voice injection
- MediatR command/query handlers
- All domain entities

---

## Content Creation Pipeline

### Topic → Outline → Draft → Final Lifecycle

1. **Topic Sources:** Manual input, trend suggestions (from TrendMonitor), content calendar slots, repurposed ideas
2. **Outline Generation:** Orchestrator sends outline task to sidecar with brand voice context
3. **Draft Generation:** Sidecar generates content via Claude Code; for blogs, it writes HTML files directly
4. **Human Editing:** Dashboard (phase 06) provides editing UI; content stored in DB
5. **Final Review:** Content submitted to workflow engine (phase 02) for approval flow

### Autonomy-Driven Behavior

All content operations respect `AutonomyLevel`:

| Operation | Autonomous | SemiAuto | Manual |
|-----------|-----------|----------|--------|
| Trend → Draft | Auto-generate, queue for review | Auto-generate from high-confidence trends only | Surface suggestions only |
| Repurpose on publish | Auto-generate all platform variants | Auto-generate if parent is published | Suggest opportunities |
| Calendar slot fill | Auto-fill from approved backlog | Suggest best-fit content | Empty slots await assignment |
| Brand voice gate | Hard gate — auto-regenerate below threshold | Advisory score only | Advisory score only |

---

## Blog Writing Integration

The sidecar operates in **full agent mode** for blog writing:
1. Content engine sends task to sidecar: "Write a blog post about X in Matt's voice"
2. Sidecar's Claude Code session has access to the blog repo and the matt-kruczek-blog-writer skill's patterns
3. Claude Code writes HTML files, optimizes SEO, generates meta tags
4. Claude Code commits to the blog git repo
5. Content engine tracks the execution and maps the output back to a Content entity

**Key:** The blog-writer skill's prompt patterns and voice guidelines are loaded into the sidecar's project context via CLAUDE.md configuration.

---

## Content Repurposing Engine

### Transformation Model

Content relationships are **tree-structured** (multi-level):
```
Blog Post (pillar)
├── Twitter Thread
│   ├── Highlight Tweet 1
│   └── Highlight Tweet 2
├── LinkedIn Post
├── Instagram Caption
└── YouTube Description
```

Cross-platform repurposing is supported: a high-engagement tweet can be expanded into a LinkedIn article.

### Repurposing Flow

1. Source content published (or approved)
2. Autonomy check determines whether to auto-generate or suggest
3. For each target platform:
   a. Send repurposing task to sidecar with source content + platform constraints
   b. Sidecar generates adapted content
   c. Create child Content entity with `ParentContentId` pointing to source
4. Child content enters workflow engine (may auto-approve based on autonomy + parent status)

### Content Parsing

Before repurposing, the source content is parsed into structured components:
- **Key points** (extractive summary)
- **Quotes** (notable phrases)
- **Statistics/data** (if present)
- **Media references** (images, links)
- **Metadata** (tags, keywords)

These components feed the per-platform generation prompts.

---

## Content Calendar & Scheduling

### Data Model

- **ContentSeries** — Recurring content pattern (e.g., "Tip Tuesday", "Weekly Roundup")
  - Name, description, recurrence rule (RRULE format), target platforms, content type, theme/topic tags
- **CalendarSlot** — Generated occurrence from a series or manually created
  - Scheduled datetime, platform, content series reference (optional), assigned content (optional)
- **RecurrenceRule** — iCalendar RRULE stored as string, occurrences generated at query time

### Calendar Operations

- CRUD for series and manual slots
- Query: "Get all slots for date range" — generates occurrences from RRULEs + manual slots
- Auto-fill: Match approved/queued content to empty slots by topic/platform affinity
- Exception handling: Single-occurrence overrides for recurring series

### Queue Slot Management

Define weekly posting capacity per platform (configurable):
- Twitter: 7 posts/week
- LinkedIn: 3 posts/week
- Instagram: 5 posts/week
- YouTube: 1 video/week

Posts auto-fill into next available slot when autonomy allows.

---

## Brand Voice System

### Three-Layer Validation

1. **Prompt Injection (existing):** `BrandProfile.ExampleContent` injected as few-shot examples in system prompt
2. **Rule-Based Checks (new):** Vocabulary preferences (preferred/avoided terms from `VocabularyConfig`), tone descriptors, forbidden patterns
3. **LLM-as-Judge Scoring (new):** Post-generation, send content + brand profile to sidecar for scoring. Returns a `BrandVoiceScore` (0-100) with dimension breakdown.

### Scoring Dimensions

- **Tone Alignment** (0-100) — Matches `ToneDescriptors`
- **Vocabulary Consistency** (0-100) — Uses preferred terms, avoids avoided terms
- **Persona Fidelity** (0-100) — Maintains persona from `PersonaDescription`
- **Overall Score** — Weighted average

### Gating Behavior

- **Autonomous:** Score < threshold → auto-regenerate (up to 3 attempts), then fail
- **SemiAuto / Manual:** Score displayed as advisory, no blocking

---

## Trend Monitoring

### Self-Hosted Stack (Docker)

1. **TrendRadar** — Open source, monitors 35+ sources, 512MB RAM, MCP integration
2. **FreshRSS** — RSS feed aggregator, WebSub push, website scraping
3. **Reddit API** — Free tier (100 queries/min, non-commercial)
4. **Hacker News API** — Free, no auth

### Trend Aggregation Service

.NET background service that:
1. Polls trend sources on configurable intervals (or receives push from TrendRadar/FreshRSS webhooks)
2. Deduplicates across sources
3. Scores relevance to brand profile using sidecar (LLM scoring)
4. Clusters related topics
5. Creates `TrendSuggestion` entities
6. At Autonomous level: auto-generates draft content from high-relevance trends

### Domain Entities

- **TrendSource** — Configured data source (type, URL, poll interval, enabled)
- **TrendItem** — Individual detected trend (title, description, source, score, detected_at)
- **TrendSuggestion** — User-facing suggestion (trend items clustered, relevance score, suggested content type, suggested platforms, status: pending/accepted/dismissed)

---

## Content Analytics

### Engagement Aggregation

Background processor (`EngagementAggregationProcessor`):
- Runs every 4 hours
- For each `ContentPlatformStatus` with `Published` status and `PublishedAt` within retention window:
  - Call `ISocialPlatform.GetEngagementAsync(platformPostId, ct)`
  - Store snapshot in `EngagementSnapshot` entity
- Rate-limit aware (uses existing `IRateLimiter`)

### On-Demand Refresh

API endpoint for dashboard to trigger fresh engagement fetch for specific content.

### Analytics Entities

- **EngagementSnapshot** — Point-in-time engagement data per platform post (likes, comments, shares, impressions, clicks, fetched_at)
- **ContentPerformance** — Aggregated view per content (total engagement across platforms, cost per engagement from `AgentExecution.Cost`)

### Insights

- Top-performing content types, topics, posting times
- Cross-platform performance comparison
- Cost-per-engagement metrics (LLM cost vs engagement)
- Feed insights back into calendar suggestions and trend scoring

---

## Docker Compose Configuration

Add to existing docker-compose.yml:

```yaml
services:
  sidecar:
    build: ../claude-code-sidecar
    ports:
      - "3001:3001"
    volumes:
      - ./blog-output:/workspace/blog
      - ./prompts:/workspace/prompts
    environment:
      - SIDECAR_CONFIG_DIR=/workspace

  trendradar:
    image: sansan0/trendradar:latest
    volumes:
      - trendradar-data:/app/data
    environment:
      - TELEGRAM_BOT_TOKEN=${TELEGRAM_BOT_TOKEN}

  freshrss:
    image: freshrss/freshrss:latest
    ports:
      - "8080:80"
    volumes:
      - freshrss-data:/var/www/FreshRSS/data
```

---

## Interfaces Produced

- `ISidecarClient` — WebSocket client for claude-code-sidecar communication
- `IContentPipeline` — Create, advance content through creation stages
- `IRepurposingService` — Transform content across formats/platforms
- `IContentCalendarService` — Manage calendar series, slots, auto-fill
- `IBrandVoiceService` — Score content, validate against brand profile
- `ITrendMonitor` — Fetch trends, score relevance, create suggestions
- `IEngagementAggregator` — Collect and aggregate platform engagement data
- Content and calendar API endpoints for dashboard

## Interfaces Refactored

- `IChatClientFactory` → **Removed** (replaced by `ISidecarClient`)
- `IChatClient` → **Removed** (replaced by sidecar WebSocket protocol)
- `AgentCapabilityBase` → **Refactored** to use sidecar task dispatch
- `AgentOrchestrator` → **Refactored** to orchestrate via sidecar

## Definition of Done

1. Sidecar integration: .NET WebSocket client connects to sidecar, sends tasks, receives streaming events
2. Phase 03 agents refactored: all 5 capabilities work through sidecar (Writer, Social, Repurpose, Engagement, Analytics)
3. Content creation pipeline: topic → outline → draft → final, with sidecar generating content
4. Blog writing: sidecar writes HTML to blog repo in full agent mode
5. Repurposing: tree-structured relationships, autonomy-driven auto-generation for 2+ platforms
6. Calendar: CRUD for series + slots, RRULE-based recurrence, autonomy-driven auto-fill
7. Brand voice: three-layer validation with configurable gating per autonomy level
8. Trend monitoring: Docker containers running, background service polling and scoring
9. Analytics: hybrid batch + on-demand engagement aggregation
10. Docker Compose: sidecar + TrendRadar + FreshRSS configured and runnable
11. Integration tests for pipeline, repurposing, and sidecar communication
