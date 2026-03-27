# Content Engine — Implementation Plan

## 1. Context and Goals

Personal Brand Assistant is an AI-powered personal branding tool built on .NET 10 + Angular 19 + PostgreSQL, self-hosted on a Synology NAS via Docker. Phases 01-04 are implemented: foundation, workflow engine, agent orchestration, and platform integrations (Twitter, LinkedIn, Instagram, YouTube).

Phase 05 — the Content Engine — is the largest remaining backend phase. It covers:

1. **Sidecar Integration & Phase 03 Rewrite:** Replace the Anthropic SDK-based agent orchestration with the claude-code-sidecar, a TypeScript service that wraps Claude Code CLI via WebSocket. All AI operations move to this sidecar, which operates in full agent mode (editing files, running commands, committing to git).

2. **Content Creation Pipeline:** Topic → Outline → Draft → Final lifecycle, with AI-assisted generation at each stage through the sidecar.

3. **Content Repurposing:** Tree-structured content relationships where a pillar piece (e.g., blog post) generates multi-level derivatives (threads, highlights, cross-platform adaptations).

4. **Content Calendar:** RRULE-based recurring series, slot management, autonomy-driven auto-fill from content backlog.

5. **Brand Voice Validation:** Three-layer system (few-shot prompting + rule-based checks + LLM-as-judge scoring) with configurable gating per autonomy level.

6. **Trend Monitoring:** Self-hosted Docker stack (TrendRadar + FreshRSS + Reddit API) with a .NET aggregation service that scores relevance and generates content suggestions.

7. **Content Analytics:** Hybrid batch + on-demand engagement aggregation from platform APIs, with cross-platform performance metrics.

8. **Docker Compose:** Configuration for sidecar, TrendRadar, and FreshRSS services alongside the existing .NET + PostgreSQL stack.

**Autonomy Dial Principle:** Every automated behavior is governed by the existing `AutonomyLevel` enum (Manual, SemiAuto, Autonomous). This is the universal control mechanism — no feature operates outside it.

---

## 2. Sidecar Integration Architecture

### 2.1 The Sidecar

The claude-code-sidecar is a separate TypeScript project at `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\claude-code-sidecar`. It's a pnpm monorepo with:

- **`packages/core/`** — SubprocessManager spawns `claude -p "task" --output-format stream-json`, parses streaming events
- **`packages/web/`** — Custom Node.js HTTP + WebSocket server at `localhost:3001`

**WebSocket Protocol:**
- Client sends: `{ type: "send-message", payload: { message } }`, `{ type: "new-session" }`, `{ type: "abort" }`
- Server sends: `{ type: "chat-event", payload: ChatEvent }`, `{ type: "file-change", payload }`, `{ type: "status", payload: { status: "running" | "idle" } }`, `{ type: "session-update", payload }`, `{ type: "error", payload: { message } }`

Auth is delegated to Claude Code's own credentials. Session persistence via `--resume <sessionId>`.

**Deployment note:** In Docker, the .NET API connects to `ws://sidecar:3001/ws` (Docker internal DNS). For local dev, use `ws://localhost:3001/ws`. The sidecar must be on an internal Docker network only — never publish port 3001 to LAN.

### 2.2 ISidecarClient Interface

New interface in `Application/Common/Interfaces/`:

```csharp
public interface ISidecarClient
{
    Task<SidecarSession> ConnectAsync(CancellationToken ct);
    IAsyncEnumerable<SidecarEvent> SendTaskAsync(string task, string? sessionId, CancellationToken ct);
    Task AbortAsync(string? sessionId, CancellationToken ct);
    bool IsConnected { get; }
}
```

**SidecarEvent** is a discriminated union record covering all server message types:

```csharp
public abstract record SidecarEvent;
public record ChatEvent(string EventType, string? Text, string? FilePath, string? ToolName) : SidecarEvent;
public record FileChangeEvent(string FilePath, string ChangeType) : SidecarEvent;
public record StatusEvent(string Status) : SidecarEvent;
public record SessionUpdateEvent(string SessionId) : SidecarEvent;
public record TaskCompleteEvent(string SessionId, int InputTokens, int OutputTokens) : SidecarEvent;
public record ErrorEvent(string Message) : SidecarEvent;
```

### 2.3 SidecarClient Implementation

Singleton service in Infrastructure. Uses `System.Net.WebSockets.ClientWebSocket` to connect to `ws://localhost:3001/ws`. Implements:

- Connection management with automatic reconnection on disconnect
- Bounded transport-level retries (3 attempts) for WS disconnects and sidecar crashes — distinct from Claude Code's internal API retries
- JSON serialization of outbound messages
- Stream parsing of inbound messages (one JSON object per WebSocket message frame)
- `IAsyncEnumerable<SidecarEvent>` for streaming consumption
- Health check endpoint for monitoring sidecar availability

Configuration via `SidecarOptions`:

```csharp
public class SidecarOptions
{
    public const string SectionName = "Sidecar";
    public string WebSocketUrl { get; set; } = "ws://localhost:3001/ws";  // Docker: ws://sidecar:3001/ws
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int ReconnectDelaySeconds { get; set; } = 5;
}
```

### 2.4 Phase 03 Agent Refactoring

**What changes:**

- `IChatClientFactory` and `IChatClient` — **Removed**. Replaced by `ISidecarClient`.
- `ChatClientFactory` — **Deleted**. Configuration (model selection, pricing) moves to sidecar's CLAUDE.md project config.
- `AgentCapabilityBase.ExecuteAsync` — Refactored. Instead of creating an Anthropic `ChatClient` and calling `CompleteAsync`, it:
  1. Builds the prompt string (same template system, same brand voice injection)
  2. Calls `ISidecarClient.SendTaskAsync(prompt, sessionId, ct)`
  3. Collects streaming events, extracting: generated text, file changes, token usage
  4. Returns `AgentOutput` with the same structure as before
- `AgentOrchestrator` — Simplified. No model tier selection (Claude Code handles this). No retry logic (Claude Code retries internally). Budget tracking and execution recording remain.

**What stays the same:**

- `IAgentCapability` interface
- All 5 capability types (Writer, Social, Repurpose, Engagement, Analytics)
- `IPromptTemplateService` for template rendering
- `AgentExecution` entity for tracking
- `ITokenTracker` for cost management
- `BrandProfilePromptModel` for brand voice injection
- All MediatR commands/queries that interact with agents
- `AgentOrchestrationOptions` (minus model/pricing config which moves to sidecar)

**Migration Strategy:** The refactoring replaces internals while preserving the public interface. Consumers of `IAgentOrchestrator` (workflow engine, publishing pipeline, future content pipeline) see no change.

---

## 3. Content Creation Pipeline

### 3.1 IContentPipeline Interface

```csharp
public interface IContentPipeline
{
    Task<Result<Guid>> CreateFromTopicAsync(ContentCreationRequest request, CancellationToken ct);
    Task<Result<string>> GenerateOutlineAsync(Guid contentId, CancellationToken ct);
    Task<Result<string>> GenerateDraftAsync(Guid contentId, CancellationToken ct);
    Task<Result<BrandVoiceScore>> ValidateVoiceAsync(Guid contentId, CancellationToken ct);
    Task<Result<Unit>> SubmitForReviewAsync(Guid contentId, CancellationToken ct);
}
```

**ContentCreationRequest:**
```csharp
public record ContentCreationRequest(
    ContentType Type,
    string Topic,
    string? Outline,
    PlatformType[]? TargetPlatforms,
    Guid? ParentContentId,
    Dictionary<string, string>? Parameters);
```

### 3.2 Pipeline Flow

1. **CreateFromTopicAsync** — Creates a `Content` entity in Draft status with the topic in metadata
2. **GenerateOutlineAsync** — Sends outline task to sidecar. Sidecar generates structured outline. Stored in `ContentMetadata.AiGenerationContext`.
3. **GenerateDraftAsync** — Sends draft generation task to sidecar. For blog posts: sidecar writes HTML files directly using matt-kruczek-blog-writer patterns. For social: sidecar returns formatted text. Updates `Content.Body`.
4. **ValidateVoiceAsync** — Runs brand voice validation (see section 6). Returns score.
5. **SubmitForReviewAsync** — Triggers workflow engine transition: Draft → Review. If autonomy allows, may auto-approve.

### 3.3 Blog Writing (Full Agent Mode)

For `ContentType.BlogPost`:
- The sidecar task prompt includes: topic, outline, brand voice profile, SEO keywords, target blog structure
- Sidecar's Claude Code session has access to the blog repo directory (mounted via Docker volume)
- Claude Code writes HTML files matching matthewkruczek.ai's structure
- Claude Code commits to the blog git repo
- The content pipeline captures the file path and commit hash from sidecar `file-change` events

**Source of truth:** The database is authoritative. Sidecar writes files + commits, then the API persists body + metadata (commit hash, file path, slug) to the `Content` entity. If the DB save fails, the commit is orphaned but content is not considered "published."

The `Content.Body` stores the generated HTML. `ContentMetadata.PlatformSpecificData` stores blog-specific data (slug, commit hash, file path).

---

## 4. Content Repurposing

### 4.1 IRepurposingService Interface

```csharp
public interface IRepurposingService
{
    Task<Result<IReadOnlyList<Guid>>> RepurposeAsync(Guid sourceContentId, PlatformType[] targetPlatforms, CancellationToken ct);
    Task<Result<IReadOnlyList<RepurposingSuggestion>>> SuggestRepurposingAsync(Guid contentId, CancellationToken ct);
}
```

**RepurposingSuggestion:**
```csharp
public record RepurposingSuggestion(
    PlatformType Platform,
    ContentType SuggestedType,
    string Rationale,
    float ConfidenceScore);
```

### 4.2 Tree-Structured Relationships

The existing `Content.ParentContentId` supports one level. For multi-level trees:
- No schema change needed — `ParentContentId` already enables arbitrary depth
- Add a query method to walk the tree: `GetContentTreeAsync(Guid rootId)` returns all descendants
- Add `Content.RepurposeSourcePlatform` (nullable `PlatformType`) to track which platform's content was the source
- Track depth via a computed property or materialized `TreeDepth` field
- **Max tree depth:** Configurable limit (default 3). `RepurposingService` checks depth before creating children to prevent recursive explosion.

### 4.3 Repurposing Flow

1. Source content reaches Published or Approved status
2. Autonomy check:
   - **Autonomous:** Auto-trigger `RepurposeAsync` for all configured target platforms
   - **SemiAuto:** Auto-trigger only if source is published (not just approved)
   - **Manual:** Create `RepurposingSuggestion` entries; user triggers manually
3. For each target platform:
   - Parse source content into structured components (key points, quotes, stats)
   - Send repurposing task to sidecar with source components + platform constraints
   - Create child `Content` entity with `ParentContentId` set
   - Child enters workflow engine (may auto-approve based on autonomy + parent status)
   - **Idempotency:** Unique constraint on `(ParentContentId, Platform, ContentType)` prevents duplicate children from event replays

### 4.4 Auto-Repurpose Background Processor

`RepurposeOnPublishProcessor` (BackgroundService):
- Listens for content status changes to Published
- Checks autonomy level
- Triggers repurposing for eligible content

---

## 5. Content Calendar & Scheduling

### 5.1 New Domain Entities

**ContentSeries:**
```csharp
public class ContentSeries : AuditableEntityBase
{
    public string Name { get; set; }
    public string? Description { get; set; }
    public string RecurrenceRule { get; set; }  // iCalendar RRULE string
    public PlatformType[] TargetPlatforms { get; set; }
    public ContentType ContentType { get; set; }
    public List<string> ThemeTags { get; set; }
    public string TimeZoneId { get; set; }  // IANA timezone for RRULE interpretation
    public bool IsActive { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}
```

**CalendarSlot:**
```csharp
public class CalendarSlot : AuditableEntityBase
{
    public DateTimeOffset ScheduledAt { get; set; }
    public PlatformType Platform { get; set; }
    public Guid? ContentSeriesId { get; set; }  // null for manual slots
    public Guid? ContentId { get; set; }  // null until content assigned
    public CalendarSlotStatus Status { get; set; }  // Open, Filled, Published, Skipped
    public bool IsOverride { get; set; }  // true if overriding a recurring occurrence
    public DateTimeOffset? OverriddenOccurrence { get; set; }  // original occurrence timestamp this overrides
}
```

### 5.2 IContentCalendarService Interface

```csharp
public interface IContentCalendarService
{
    Task<Result<IReadOnlyList<CalendarSlot>>> GetSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<Guid>> CreateSeriesAsync(ContentSeriesRequest request, CancellationToken ct);
    Task<Result<Guid>> CreateManualSlotAsync(CalendarSlotRequest request, CancellationToken ct);
    Task<Result<Unit>> AssignContentAsync(Guid slotId, Guid contentId, CancellationToken ct);
    Task<Result<int>> AutoFillSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
```

### 5.3 RRULE-Based Recurrence

Store recurrence as an RRULE string (e.g., `FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0`). At query time:
1. Load all active `ContentSeries` with date ranges overlapping the query window
2. Parse each RRULE and generate occurrences within the window, using the series `TimeZoneId` for correct DST handling
3. Merge with materialized `CalendarSlot` records (manual slots + overrides)
4. Return unified list sorted by `ScheduledAt`

Use an RRULE parsing library (e.g., `Ical.Net` NuGet package) for occurrence generation.

### 5.4 Auto-Fill Algorithm

When `AutoFillSlotsAsync` is called (or triggered by background processor at Autonomous level):
1. Query open slots in the date range
2. Query approved/queued content not yet assigned to any slot
3. For each open slot, score candidate content by:
   - Platform match (content's `TargetPlatforms` includes slot's `Platform`)
   - Theme/topic match (content metadata tags vs series theme tags)
   - Age (prefer older approved content to prevent staleness)
4. Assign highest-scoring content to each slot (use `SELECT FOR UPDATE SKIP LOCKED` to prevent double-assignment under concurrent calls)
5. Return count of slots filled

---

## 6. Brand Voice System

### 6.1 IBrandVoiceService Interface

```csharp
public interface IBrandVoiceService
{
    Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct);
    Task<Result<Unit>> ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct);
    Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile);
}
```

**BrandVoiceScore:**
```csharp
public record BrandVoiceScore(
    int OverallScore,
    int ToneAlignment,
    int VocabularyConsistency,
    int PersonaFidelity,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> RuleViolations);
```

### 6.2 Three-Layer Implementation

**Layer 1 — Prompt Injection (existing):** Already implemented via `BrandProfile.ExampleContent` fed into `BrandProfilePromptModel`. No changes needed.

**Layer 2 — Rule-Based Checks:** Synchronous, no AI needed. Content is first normalized (strip HTML tags, decode entities) before checking.
- Check for avoided terms from `VocabularyConfig.AvoidedTerms`
- Check for expected preferred terms from `VocabularyConfig.PreferredTerms` (warn if none present)
- Validate tone markers (configurable patterns per tone descriptor)
- Return list of rule violations

**Layer 3 — LLM-as-Judge:** Send content + brand profile to sidecar with a scoring prompt that enforces JSON output format. Parse the structured JSON response into `BrandVoiceScore` dimensions. Validate with schema; handle invalid/partial responses gracefully (return error result, not crash).

### 6.3 Gating Logic

In `ValidateAndGateAsync`:
- **Autonomous:** If `OverallScore < threshold` (configurable, default 70), auto-regenerate via `IContentPipeline.GenerateDraftAsync` up to 3 times. If still below threshold, fail with `ErrorCode.ValidationFailed`.
- **SemiAuto / Manual:** Return score as advisory. No blocking. Score stored in `ContentMetadata`.

---

## 7. Trend Monitoring

### 7.1 Docker Services

**TrendRadar:** Open-source Python tool monitoring 35+ sources. Runs as Docker container with 512MB RAM. Exposes data via its own API/webhook.

**FreshRSS:** Self-hosted RSS aggregator. Exposes a REST API for reading feed items. Supports WebSub for push notifications.

**Reddit polling:** No dedicated container — handled by the .NET background service using Reddit's free API (100 queries/min).

### 7.2 New Domain Entities

**TrendSource:**
```csharp
public class TrendSource : AuditableEntityBase
{
    public string Name { get; set; }
    public TrendSourceType Type { get; set; }  // TrendRadar, FreshRSS, Reddit, HackerNews
    public string? ApiUrl { get; set; }
    public int PollIntervalMinutes { get; set; }
    public bool IsEnabled { get; set; }
}
```

**TrendItem:**
```csharp
public class TrendItem : AuditableEntityBase
{
    public string Title { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string SourceName { get; set; }
    public TrendSourceType SourceType { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string? DeduplicationKey { get; set; }  // hash for cross-source dedup
}
```

**TrendSuggestion:**
```csharp
public class TrendSuggestion : AuditableEntityBase
{
    public string Topic { get; set; }
    public string Rationale { get; set; }
    public float RelevanceScore { get; set; }  // 0-1, from LLM scoring
    public ContentType SuggestedContentType { get; set; }
    public PlatformType[] SuggestedPlatforms { get; set; }
    public TrendSuggestionStatus Status { get; set; }  // Pending, Accepted, Dismissed
    public ICollection<TrendSuggestionItem> RelatedTrends { get; set; }  // join entity with similarity score
}
```

### 7.3 ITrendMonitor Interface

```csharp
public interface ITrendMonitor
{
    Task<Result<IReadOnlyList<TrendSuggestion>>> GetSuggestionsAsync(int limit, CancellationToken ct);
    Task<Result<Unit>> DismissSuggestionAsync(Guid suggestionId, CancellationToken ct);
    Task<Result<Guid>> AcceptSuggestionAsync(Guid suggestionId, CancellationToken ct);
    Task<Result<Unit>> RefreshTrendsAsync(CancellationToken ct);
}
```

### 7.4 TrendAggregationProcessor (BackgroundService)

Runs on configurable interval (default 30 minutes):
1. Poll each enabled `TrendSource`:
   - TrendRadar: HTTP GET to its API
   - FreshRSS: HTTP GET to its REST API for unread items
   - Reddit: HTTP GET to `/r/{subreddit}/hot.json` for configured subreddits
   - Hacker News: HTTP GET to `/v0/topstories.json`
2. Deduplicate across sources using title similarity / URL matching
3. Score relevance against brand profile using sidecar (batch scoring prompt)
4. Cluster related trends by topic similarity
5. Create `TrendSuggestion` entities for high-relevance clusters
6. At Autonomous level: auto-trigger content creation for top suggestions

---

## 8. Content Analytics

### 8.1 New Domain Entities

**EngagementSnapshot:**
```csharp
public class EngagementSnapshot : AuditableEntityBase
{
    public Guid ContentPlatformStatusId { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int Shares { get; set; }
    public int? Impressions { get; set; }  // nullable — not all platforms provide this
    public int? Clicks { get; set; }  // nullable — not all platforms provide this
    public DateTimeOffset FetchedAt { get; set; }
}
```

### 8.2 IEngagementAggregator Interface

```csharp
public interface IEngagementAggregator
{
    Task<Result<EngagementSnapshot>> FetchLatestAsync(Guid contentPlatformStatusId, CancellationToken ct);
    Task<Result<ContentPerformanceReport>> GetPerformanceAsync(Guid contentId, CancellationToken ct);
    Task<Result<IReadOnlyList<TopPerformingContent>>> GetTopContentAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
```

**ContentPerformanceReport:**
```csharp
public record ContentPerformanceReport(
    Guid ContentId,
    IReadOnlyDictionary<PlatformType, EngagementSnapshot> LatestByPlatform,
    int TotalEngagement,
    decimal? LlmCost,
    decimal? CostPerEngagement);
```

### 8.3 EngagementAggregationProcessor (BackgroundService)

Runs every 4 hours:
1. Query `ContentPlatformStatus` entries with `Published` status
2. Filter to content published within retention window (configurable, default 30 days)
3. For each entry: call `ISocialPlatform.GetEngagementAsync(platformPostId, ct)`
4. Save new `EngagementSnapshot` records
5. Respect rate limits via `IRateLimiter`
6. **Retention:** Keep hourly snapshots for 7 days, daily for 30 days, then delete older records. Index on `(ContentPlatformStatusId, FetchedAt DESC)`.

### 8.4 On-Demand Refresh

API endpoint allows the dashboard to trigger a fresh fetch for specific content, bypassing the batch schedule.

---

## 9. API Endpoints

New Minimal API endpoint groups. All endpoints require authentication. State-changing endpoints (content generation, publishing, trend refresh) enforce server-side autonomy checks via `IAutonomyPolicy`.

**Content Pipeline:**
- `POST /api/content/create` — Create from topic
- `POST /api/content/{id}/outline` — Generate outline
- `POST /api/content/{id}/draft` — Generate draft
- `POST /api/content/{id}/validate-voice` — Score brand voice
- `POST /api/content/{id}/submit` — Submit for review

**Repurposing:**
- `POST /api/content/{id}/repurpose` — Trigger repurposing to target platforms
- `GET /api/content/{id}/repurpose-suggestions` — Get suggestions
- `GET /api/content/{id}/tree` — Get content relationship tree

**Calendar:**
- `GET /api/calendar?from={date}&to={date}` — Get slots for range
- `POST /api/calendar/series` — Create recurring series
- `POST /api/calendar/slot` — Create manual slot
- `PUT /api/calendar/slot/{id}/assign` — Assign content to slot
- `POST /api/calendar/auto-fill?from={date}&to={date}` — Auto-fill slots

**Brand Voice:**
- `GET /api/brand-voice/score/{contentId}` — Get brand voice score
- `PUT /api/brand-voice/profile` — Update brand profile

**Trends:**
- `GET /api/trends/suggestions` — Get trend suggestions
- `POST /api/trends/suggestions/{id}/accept` — Accept suggestion (creates content)
- `POST /api/trends/suggestions/{id}/dismiss` — Dismiss suggestion
- `POST /api/trends/refresh` — Trigger manual refresh

**Analytics:**
- `GET /api/analytics/content/{id}` — Get content performance
- `GET /api/analytics/top?from={date}&to={date}&limit={n}` — Top performing content
- `POST /api/analytics/content/{id}/refresh` — On-demand engagement refresh

---

## 10. Docker Compose Configuration

Add three services to the existing `docker-compose.yml`:

**Sidecar Service:**
- Build from `../claude-code-sidecar`
- Internal network only — port 3001 NOT published to host/LAN
- Volume mounts: blog output directory, prompts directory
- Environment: `SIDECAR_CONFIG_DIR`, `PORT`
- Depends on: nothing (standalone)

**TrendRadar Service:**
- Image: `sansan0/trendradar:latest` (pin to specific version tag before production)
- Persistent volume for data
- Environment: alert config (Telegram bot token, etc.)

**FreshRSS Service:**
- Image: `freshrss/freshrss:latest` (pin to specific version tag before production)
- Port 8080 for web UI and API
- Persistent volume for data and config

Network configuration ensures the .NET API can reach sidecar (port 3001), TrendRadar API, and FreshRSS API via Docker's internal DNS.

---

## 11. EF Core Migrations

New entities requiring migration:
- `ContentSeries` — Calendar recurring series
- `CalendarSlots` — Calendar slot instances
- `TrendSources` — Configured trend data sources
- `TrendItems` — Detected trend entries
- `TrendSuggestions` — User-facing suggestions
- `TrendSuggestionItems` — Junction table linking TrendSuggestions to TrendItems (with similarity score)
- `EngagementSnapshots` — Point-in-time engagement data

Modified entities:
- `Content` — Add `TreeDepth` (int, computed or stored) and `RepurposeSourcePlatform` (nullable PlatformType)
- `BrandProfile` — No changes (existing fields sufficient)

New enums:
- `TrendSourceType` (TrendRadar, FreshRSS, Reddit, HackerNews)
- `TrendSuggestionStatus` (Pending, Accepted, Dismissed)
- `CalendarSlotStatus` (Open, Filled, Published, Skipped)

---

## 12. DI Registration

New registrations in `DependencyInjection.cs`:

**Sidecar:**
- `services.Configure<SidecarOptions>(configuration.GetSection(SidecarOptions.SectionName))`
- `services.AddSingleton<ISidecarClient, SidecarClient>()`

**Remove:**
- `services.AddSingleton<IChatClientFactory, ChatClientFactory>()` — deleted

**Content Services (scoped):**
- `IContentPipeline → ContentPipeline`
- `IRepurposingService → RepurposingService`
- `IContentCalendarService → ContentCalendarService`
- `IBrandVoiceService → BrandVoiceService`
- `ITrendMonitor → TrendMonitor`
- `IEngagementAggregator → EngagementAggregator`

**Background Services:**
- `RepurposeOnPublishProcessor`
- `TrendAggregationProcessor`
- `EngagementAggregationProcessor`
- `CalendarSlotProcessor` (generates slots from series and triggers auto-fill)

---

## 13. Configuration (appsettings.json)

```json
{
  "Sidecar": {
    "WebSocketUrl": "ws://localhost:3001/ws",
    "ConnectionTimeoutSeconds": 30,
    "ReconnectDelaySeconds": 5
  },
  "ContentEngine": {
    "BrandVoiceScoreThreshold": 70,
    "MaxAutoRegenerateAttempts": 3,
    "EngagementRetentionDays": 30,
    "EngagementAggregationIntervalHours": 4
  },
  "TrendMonitoring": {
    "AggregationIntervalMinutes": 30,
    "TrendRadarApiUrl": "http://trendradar:8000/api",
    "FreshRssApiUrl": "http://freshrss:80/api",
    "RedditSubreddits": ["programming", "dotnet", "webdev"]
  }
}
```

---

## 14. File Structure

```
src/PersonalBrandAssistant.Domain/
  Entities/
    ContentSeries.cs
    CalendarSlot.cs
    TrendSource.cs
    TrendItem.cs
    TrendSuggestion.cs
    EngagementSnapshot.cs
    TrendSuggestionItem.cs
  Enums/
    TrendSourceType.cs
    TrendSuggestionStatus.cs
    CalendarSlotStatus.cs

src/PersonalBrandAssistant.Application/
  Common/
    Interfaces/
      ISidecarClient.cs
      IContentPipeline.cs
      IRepurposingService.cs
      IContentCalendarService.cs
      IBrandVoiceService.cs
      ITrendMonitor.cs
      IEngagementAggregator.cs
    Models/
      SidecarEvent.cs
      SidecarOptions.cs
      ContentCreationRequest.cs
      BrandVoiceScore.cs
      RepurposingSuggestion.cs
      ContentPerformanceReport.cs
      ContentEngineOptions.cs
      TrendMonitoringOptions.cs
  Features/
    Content/
      Commands/  (CreateFromTopic, GenerateOutline, GenerateDraft, SubmitForReview)
      Queries/   (GetContentTree)
    Calendar/
      Commands/  (CreateSeries, CreateSlot, AssignContent, AutoFillSlots)
      Queries/   (GetSlots)
    Trends/
      Commands/  (AcceptSuggestion, DismissSuggestion, RefreshTrends)
      Queries/   (GetSuggestions)
    Analytics/
      Queries/   (GetPerformance, GetTopContent)
    BrandVoice/
      Commands/  (ScoreContent, ValidateAndGate)

src/PersonalBrandAssistant.Infrastructure/
  Agents/
    AgentOrchestrator.cs (refactored — uses ISidecarClient)
    Capabilities/
      AgentCapabilityBase.cs (refactored — uses ISidecarClient)
  Services/
    SidecarClient.cs
    ContentServices/
      ContentPipeline.cs
      RepurposingService.cs
      ContentCalendarService.cs
      BrandVoiceService.cs
      TrendMonitor.cs
      EngagementAggregator.cs
  BackgroundJobs/
    RepurposeOnPublishProcessor.cs
    TrendAggregationProcessor.cs
    EngagementAggregationProcessor.cs
    CalendarSlotProcessor.cs
  Data/
    Configurations/
      ContentSeriesConfiguration.cs
      CalendarSlotConfiguration.cs
      TrendSourceConfiguration.cs
      TrendItemConfiguration.cs
      TrendSuggestionConfiguration.cs
      EngagementSnapshotConfiguration.cs
      TrendSuggestionItemConfiguration.cs

src/PersonalBrandAssistant.Api/
  Endpoints/
    ContentPipelineEndpoints.cs
    RepurposingEndpoints.cs
    CalendarEndpoints.cs
    BrandVoiceEndpoints.cs
    TrendEndpoints.cs
    AnalyticsEndpoints.cs

docker-compose.yml (modified)
```

---

## 15. Testing Strategy

- **Unit tests:** All services, pipeline logic, RRULE generation, brand voice rule checks, auto-fill algorithm
- **Integration tests:** Sidecar client (mock WebSocket server), EF Core configurations, MediatR handlers
- **Mock pattern:** `ISidecarClient` mocked in all tests — returns predefined event streams
- **Existing test infrastructure:** xUnit + Moq + MockQueryable, `WebApplicationFactory<Program>` for DI tests

Key test scenarios:
- Sidecar connection lifecycle (connect, send, receive, disconnect, reconnect)
- Content pipeline: topic → draft → voice validation → submit
- Repurposing: parent publish triggers child creation (per autonomy level)
- Calendar: RRULE generates correct occurrences, auto-fill matches content to slots
- Brand voice: rule checks catch violations, LLM scoring parses response, gating respects autonomy
- Trend aggregation: deduplication across sources, relevance scoring
- Analytics: batch aggregation, on-demand refresh, cost-per-engagement calculation
