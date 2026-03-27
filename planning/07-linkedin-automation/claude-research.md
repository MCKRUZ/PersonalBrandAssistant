# Research Findings: Autonomous LinkedIn Content Workflow

## Part 1: Codebase Analysis

### Existing Service Integration Map

#### TrendMonitor & TrendAggregationProcessor
- **TrendMonitor.cs** — Full trend pipeline: polls 8 sources → deduplicates → LLM-scores relevance → clusters into TrendSuggestions
- **TrendAggregationProcessor.cs** — BackgroundService with `PeriodicTimer(interval)`, calls `RefreshTrendsAsync()` each cycle
- **Key method for automation:** `AcceptSuggestionAsync(Guid suggestionId)` — marks suggestion as Accepted, creates Draft Content with target platforms, returns Content ID
- **Config:** `TrendMonitoringOptions` — `AggregationIntervalMinutes`, `RelevanceScoreThreshold`, `MaxSuggestionsPerCycle`

#### ContentPipeline
- **IContentPipeline** — `CreateFromTopicAsync`, `GenerateOutlineAsync`, `GenerateDraftAsync`, `ValidateVoiceAsync`, `SubmitForReviewAsync`
- **Draft flow:** Loads topic + outline from `AiGenerationContext` JSON, injects brand voice (persona, tone, style), streams via sidecar WebSocket, stores body + token counts
- **Auto-approval:** `SubmitForReviewAsync` checks `ShouldAutoApproveAsync()` — auto-approves if Autonomous or SemiAuto with published parent
- **Content entity:** `ContentType` (BlogPost|SocialPost|Thread|VideoDescription), `Status` (8 states), `TargetPlatforms[]`, `Metadata.PlatformSpecificData`, `CapturedAutonomyLevel`

#### PublishingPipeline & LinkedInPlatformAdapter
- **PublishingPipeline.PublishAsync** — per-platform publishing with idempotency keys (SHA256 of contentId:platform:version), rate limit checks, adapter dispatch
- **LinkedInPlatformAdapter** — Posts to `/posts` with author URN, commentary, visibility=PUBLIC. Gets user URN via `/userinfo`. Captures `x-restli-id` header. Max 3000 chars.
- **Currently text-only** — no image upload flow in the adapter
- **Token management:** PlatformAdapterBase handles encrypted token load, 401 refresh, retry

#### WorkflowEngine
- **Stateless library** state machine: Draft → Review → Approved → Scheduled → Publishing → Published (+ Failed, Archived)
- **Auto-approval:** Autonomous = always; SemiAuto = if parent approved; Manual/Assisted = never
- **Concurrency:** Uses DB concurrency tokens, returns Conflict on race

#### ScheduledPublishProcessor
- **30-second PeriodicTimer** — queries `Status == Scheduled && ScheduledAt <= now`
- **Per content:** Transition to Publishing → PublishingPipeline.PublishAsync() → Transition to Published/Failed
- **Retry backoff:** [1 min, 5 min, 15 min+]

#### MediaStorage
- **IMediaStorage** — `SaveAsync(Stream, fileName, mimeType)`, `GetStreamAsync(fileId)`, `GetSignedUrlAsync(fileId, expiry)`
- **LocalMediaStorage** — stores at `{BasePath}/{yyyy}/{MM}/{guid}.{ext}`, validates MIME via magic bytes, 50MB max
- **Signed URLs:** HMAC-SHA256 token with expiry for secure access

#### Sidecar Integration
- **ISidecarClient** — WebSocket connection to `ws://localhost:3001/ws`
- **Protocol:** `new-session` → `send-message` with task + systemPrompt + sessionId → streams events
- **Events:** ChatEvent (text), FileChangeEvent, TaskCompleteEvent (tokens), ErrorEvent, StatusEvent

#### Background Job Pattern
All use `BackgroundService` + `PeriodicTimer` + `IServiceScopeFactory.CreateScope()` for DI isolation. Graceful error handling (log, continue).

#### DI Registration
- `AddInfrastructure(IConfiguration)` in `DependencyInjection.cs`
- Scoped: WorkflowEngine, ContentPipeline, PublishingPipeline, TrendMonitor, BrandVoiceService
- Singleton: SidecarClient, MediaStorage
- Options pattern: `services.Configure<T>(configuration.GetSection(T.SectionName))`

#### Configuration Pattern
- Options classes with `const string SectionName` and defaults
- Injected as `IOptions<T>` in constructors
- Mapped from `appsettings.json` sections

#### Result<T> Pattern
- `Result<T>.Success(value)`, `Result<T>.Failure(ErrorCode, errors)`, `Result<T>.NotFound(msg)`
- ErrorCode enum: None, ValidationFailed, NotFound, Conflict, Unauthorized, InternalError, RateLimited

### Key Integration Points for New Orchestrator

1. **Entry:** `ITrendMonitor.AcceptSuggestionAsync(suggestionId)` → returns Content ID
2. **Generate:** `IContentPipeline.GenerateOutlineAsync(contentId)` → `GenerateDraftAsync(contentId)`
3. **Validate:** `IContentPipeline.ValidateVoiceAsync(contentId)` → brand voice score
4. **Submit:** `IContentPipeline.SubmitForReviewAsync(contentId)` → auto-approves if Autonomous
5. **Schedule:** `IWorkflowEngine.TransitionAsync(contentId, Scheduled)` + set `ScheduledAt`
6. **Publish:** Handled by existing `ScheduledPublishProcessor` → `PublishingPipeline` → `LinkedInPlatformAdapter`

### Testing Setup
- **Framework:** xUnit
- **Patterns:** AAA, Moq for mocking
- **Integration tests:** `WebApplicationFactory<Program>` with in-memory DB
- **Test utilities:** Custom test helpers in test project

---

## Part 2: Web Research

### ComfyUI API Integration

**API Endpoints (no built-in auth):**
| Method | Endpoint | Purpose |
|--------|----------|---------|
| `POST /prompt` | Queue workflow. Body: `{ "prompt": <workflow_json>, "client_id": "<uuid>" }`. Returns `{ "prompt_id": "..." }` |
| `GET /history/{prompt_id}` | Get execution results with output filenames |
| `GET /view?filename=X&subfolder=Y&type=output` | Download generated image as binary |
| `WS /ws?clientId=<uuid>` | Real-time progress (progress, executing, executed) |

**Workflow JSON format:** API format uses numeric node IDs with `class_type` + `inputs`. Node references are `["node_id", output_index]` tuples. FLUX workflows use `UNETLoader` + `DualCLIPLoader` + `VAELoader` + `SamplerCustomAdvanced`.

**Completion strategy:** WebSocket recommended — listen for `type: "executing"` with `data.node == null` → call `GET /history/{prompt_id}` for output filenames.

**No .NET SDK exists.** Build thin wrapper with `HttpClient` + `ClientWebSocket`.

**Security:** Zero built-in auth. Run behind reverse proxy with API key validation. Docker network isolation recommended.

### LinkedIn Image Post API (2025-2026)

**3-step upload flow with Images API (`/rest/images`):**
1. `POST /rest/images?action=initializeUpload` → returns `uploadUrl` + image URN
2. `PUT {uploadUrl}` with binary image data
3. `POST /rest/posts` with `content.media.id` = image URN

**Must poll** `GET /rest/images/{imageUrn}` until `status == AVAILABLE` before creating post.

**Image requirements:** JPG/PNG/GIF, max ~36M pixels (~6000x6000), altText max 4086 chars.

**Required scopes:** `w_member_social` (post + upload) + `openid` (get person URN).

**Headers:** `Linkedin-Version: 202603`, `X-Restli-Protocol-Version: 2.0.0`.

**Rate limits:** Per-app per-member, not published, 429 on exceed.

**Key change from current adapter:** Current LinkedInPlatformAdapter only does text posts. Needs image upload flow added.

### AI Image Prompt Engineering

**FLUX vs SDXL:** FLUX better for natural language prompts, text rendering, and photorealism. FLUX Dev for quality (20 steps), FLUX Schnell for speed (4 steps). SDXL if VRAM-constrained.

**Professional LinkedIn prompt patterns:**
- Abstract tech: `"Clean minimalist illustration of interconnected neural network nodes, soft gradient background navy to electric blue, modern flat design, professional corporate style, no text overlay"`
- Editorial: `"Professional infographic-style visualization, isometric perspective, muted slate blue and coral, editorial magazine quality, white space composition"`
- Thought leadership: `"Abstract digital transformation, geometric shapes dissolving into light particles, deep teal and warm amber, cinematic depth of field, corporate imagery"`

**Style keywords that work:** `minimalist`, `flat design`, `editorial style`, `isometric`, `muted palette`, `gradient background`, `white space`, `high contrast`

**Pitfalls:** Don't render text in AI images (add in post-processing). Keep compositions simple for small feed display. Avoid photorealistic faces. Use muted corporate-appropriate palettes.

**Resolution:** Generate at 1024x1024, crop/resize to 1200x627 for LinkedIn optimal display.

### Time-Based Scheduling in .NET

**Recommended: Cronos + BackgroundService with Task.Delay**
- Cronos: 58x faster than NCrontab at parsing, handles DST, supports `TimeZoneInfo`
- Pattern: Parse cron → `GetNextOccurrence(now, timezone)` → `Task.Delay(next - now)` → execute → loop

**Configuration:**
```json
{
  "Scheduling": {
    "LinkedInDailyPost": {
      "CronExpression": "0 9 * * 1-5",
      "TimeZone": "Eastern Standard Time",
      "Enabled": true
    }
  }
}
```

**Idempotency for daily jobs:**
- Store last successful run date in DB
- On execution, check if today already completed
- Date-based idempotency key: `{date}_{contentHash}`
- Handle missed runs: compare lastRun to now, decide catch-up vs skip

**PeriodicTimer vs Task.Delay:** PeriodicTimer for fixed intervals (polling ComfyUI status). Task.Delay + Cronos for time-of-day scheduling (daily 9AM trigger).

---

## Sources

### ComfyUI
- [ComfyUI Routes Docs](https://docs.comfy.org/development/comfyui-server/comms_routes)
- [9elements API Guide](https://9elements.com/blog/hosting-a-comfyui-workflow-via-api/)
- [ViewComfy Production Guide](https://www.viewcomfy.com/blog/building-a-production-ready-comfyui-api)

### LinkedIn API
- [Posts API](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api)
- [Images API](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/images-api)
- [Rate Limits](https://learn.microsoft.com/en-us/linkedin/shared/api-guide/concepts/rate-limits)

### Image Generation
- [FLUX Prompt Guide](https://nebius.com/blog/posts/creating-images-with-flux-prompt-guide)
- [SDXL Best Practices](https://neurocanvas.net/blog/sdxl-best-practices-guide/)
- [FLUX Tutorial](https://docs.comfy.org/tutorials/flux/flux-1-text-to-image)

### .NET Scheduling
- [Cronos GitHub](https://github.com/HangfireIO/Cronos)
- [Steven Giesel Cron Scheduler](https://steven-giesel.com/blogPost/fb1ce2ab-dd27-43ed-aaab-077adf2d15cd)
- [Milan Jovanovic Background Tasks](https://www.milanjovanovic.tech/blog/running-background-tasks-in-asp-net-core)
