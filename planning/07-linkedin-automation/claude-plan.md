# Implementation Plan: Autonomous Content Workflow

## 1. Context & Goals

The Personal Brand Assistant (PBA) already has a mature content pipeline: trend monitoring (8 sources), AI content generation via Claude sidecar, a state-machine workflow engine, and platform publishing adapters with retry/idempotency. What it lacks is the glue that chains these into an autonomous daily workflow, plus image generation via the user's self-hosted ComfyUI instance.

This plan adds:
- A daily scheduler that fires at a configurable time (default 9AM ET weekdays)
- An orchestrator that chains trend curation, per-platform content generation, image generation, and publishing
- A ComfyUI integration for AI-generated professional images
- LinkedIn image upload support (currently text-only)
- Multi-platform content generation (unique content per platform, not reformatted copies)

### Key Decisions from Stakeholder Interview

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Image failure | Block publish + notify | User considers images essential, not optional |
| Multi-platform content | Generate unique per platform | Higher quality; LinkedIn professional, Twitter punchy, blog teaser |
| Semi-Auto review | No auto-publish, no expiry | Content waits indefinitely for explicit approval |
| Topic selection | AI-curated from top 5 | Claude picks the most compelling topic considering diversity + engagement |
| Pipeline model | All platforms in single run | Simpler orchestration, atomic success/failure tracking |
| Image sharing | One image, resize per platform | Efficient; generate once at 1536x1536, downscale to platform-optimal sizes |

---

## 2. Architecture Overview

### New Services

```
Infrastructure/
  Services/
    ContentAutomation/
      DailyContentOrchestrator.cs     # Pipeline orchestration
      ImagePromptService.cs            # AI prompt generation for ComfyUI
      ImageGenerationService.cs        # ComfyUI integration + image management
      ComfyUiClient.cs                 # HTTP + WebSocket client for ComfyUI API
      ImageResizer.cs                  # Platform-specific image cropping
  BackgroundJobs/
    DailyContentProcessor.cs           # Cron-scheduled BackgroundService

Application/
  Common/
    Interfaces/
      IDailyContentOrchestrator.cs
      IImagePromptService.cs
      IImageGenerationService.cs
      IComfyUiClient.cs
      IImageResizer.cs
    Models/
      ContentAutomationOptions.cs
      ComfyUiOptions.cs
      AutomationRunResult.cs
      ImageGenerationResult.cs

Domain/
  Entities/
    AutomationRun.cs                   # Tracks each pipeline execution
```

### Integration with Existing Services

The orchestrator consumes existing interfaces without modifying their contracts:

- `ITrendMonitor.GetSuggestionsAsync()` → trend candidates
- `ITrendMonitor.AcceptSuggestionAsync()` → creates Draft content
- `IContentPipeline.GenerateOutlineAsync()` / `GenerateDraftAsync()` → primary content
- `IContentPipeline.SubmitForReviewAsync()` → triggers workflow transitions
- `IWorkflowEngine.TransitionAsync()` → state machine
- `IPublishingPipeline.PublishAsync()` → platform dispatch
- `ISidecarClient.SendTaskAsync()` → AI prompts for topic curation + image prompts
- `IMediaStorage.SaveAsync()` → image storage
- `INotificationService` → push notifications

Existing services requiring modification:
- **LinkedInPlatformAdapter** — add image upload support via LinkedIn Images API
- **LinkedInContentFormatter** — read image metadata from Content and pass `MediaFile` through to `PlatformContent.Media` (currently hardcodes empty array)
- **Other platform formatters** — same pattern for image passthrough

---

## 3. Daily Content Processor (Scheduling)

### Purpose
A `BackgroundService` that fires at a configurable time using Cronos cron expressions with timezone awareness. Replaces the `PeriodicTimer` pattern used by other background jobs with a `Task.Delay` + `CronExpression.GetNextOccurrence()` approach.

### Design

**Configuration** (`ContentAutomationOptions`):
- `CronExpression` (string, default `"0 9 * * 1-5"` — 9AM weekdays)
- `TimeZone` (string, default `"Eastern Standard Time"`)
- `Enabled` (bool, default `true`)
- `AutonomyLevel` (AutonomyLevel enum)
- `TopTrendsToConsider` (int, default `5`)
- `TargetPlatforms` (PlatformType[], default `["LinkedIn"]`)

**Scheduling loop:**
1. Parse cron expression via `Cronos.CronExpression.Parse()`
2. Calculate next occurrence: `GetNextOccurrence(DateTimeOffset.UtcNow, timeZone)`
3. `Task.Delay(next - now, stoppingToken)`
4. **Idempotency check:** Query `AutomationRun` table for today's date. If a run with status `Completed` or `Running` exists, skip. Checking `Running` prevents concurrent duplicate runs from manual triggers.
5. Create `IServiceScope`, resolve `IDailyContentOrchestrator`, call `ExecuteAsync()`
6. Loop back to step 2

**Error handling:** Log error, create failed `AutomationRun` record, continue to next scheduled occurrence. No retry within the same day (the next day's run will pick up fresh trends).

### New NuGet Dependency
- `Cronos` (HangfireIO) for cron parsing. Already battle-tested, 58x faster than NCrontab, handles DST correctly.

---

## 4. Daily Content Orchestrator

### Purpose
The central pipeline service that chains all steps: trend curation → content generation → image generation → workflow transitions. Executes synchronously within a single scoped lifetime.

### Interface

```csharp
interface IDailyContentOrchestrator
{
    Task<AutomationRunResult> ExecuteAsync(ContentAutomationOptions options, CancellationToken ct);
}
```

### Pipeline Steps

**Step 1: AI-Curated Trend Selection**

Load the top N suggestions via `ITrendMonitor.GetSuggestionsAsync(limit: options.TopTrendsToConsider)`. Filter for `Status == Pending` only (exclude previously Accepted or Dismissed suggestions). If fewer than 1 pending suggestion exists, fail the run with "No trends available."

Send the top suggestions to the sidecar with a system prompt that instructs Claude to:
- Consider engagement potential for the target audience
- Check topic diversity against content published in the last 7 days (query `Content` table for recent Published items)
- Evaluate brand alignment against the brand profile
- Return a structured JSON response: `{"suggestionId": "...", "reasoning": "...", "contentType": "SocialPost|Thread|BlogPost"}` where `contentType` is determined by topic depth/complexity

Parse the sidecar response with `JsonSerializer`. On parse failure, retry the prompt once with explicit formatting instructions, then fail the run with a clear error.

**Step 2: Primary Content Creation**

The AI curation response (Step 1) already includes the recommended `contentType`. Use this to determine the content type *before* creating the Content entity.

**Important:** `Content.ContentType` has a `private init` setter — it can only be set at construction time. The orchestrator must determine the content type first, then either:
- Pass the `ContentType` to `AcceptSuggestionAsync` (requires adding a `ContentType?` override parameter), or
- Create the Content entity directly via the orchestrator instead of using `AcceptSuggestionAsync`, setting the correct `ContentType` at construction

The recommended approach is adding an optional `ContentType?` parameter to `AcceptSuggestionAsync` that overrides the suggestion's default type. This is a minimal change to the existing interface.

Call `IContentPipeline.GenerateOutlineAsync(contentId)` then `GenerateDraftAsync(contentId)` to produce the primary content body.

**Step 3: Platform-Specific Content Generation**

For each platform in `options.TargetPlatforms`:
- Create a child Content entity (set `ParentContentId = primaryContentId`, `TargetPlatforms = [platform]`)
- Generate a platform-specific draft via a new method `IContentPipeline.GeneratePlatformDraftAsync(childContentId, platform, parentBody)`
- This method sends the primary content body to the sidecar with a platform-specific system prompt:
  - **LinkedIn:** "Rewrite as a professional LinkedIn post. Authoritative tone, thought leadership angle. Max 3000 chars. Include relevant hashtags."
  - **TwitterX:** "Rewrite as a punchy tweet or thread. Sharp, opinionated, dev-community credible. Max 280 chars per tweet."
  - **PersonalBlog:** "Rewrite as a blog teaser/excerpt that drives traffic to the full post."

Each child content inherits the parent's outline and topic context but gets a unique body.

**Step 4: Image Generation**

Call `IImagePromptService.GeneratePromptAsync(primaryContent.Body)` to get a FLUX-optimized prompt.

Call `IImageGenerationService.GenerateAsync(prompt, options.ImageGeneration)`:
1. Submits workflow to ComfyUI
2. Waits for completion (WebSocket or polling)
3. Downloads the output image
4. Stores via `IMediaStorage.SaveAsync()` → returns `fileId`

**On failure:** Stop the entire pipeline. Do not publish any content. Transition all content (parent + children) to Review status. Send notification via `INotificationService`. Record failure in `AutomationRun`.

On success, call `IImageResizer.ResizeForPlatformsAsync(fileId, platforms)` to produce platform-specific crops. Store each crop, associate fileIds with respective child Content entities via `Metadata.PlatformSpecificData["imageFileId"]`.

**Step 5: Brand Validation**

For each content version (parent + children), call `IContentPipeline.ValidateVoiceAsync(contentId)`. If any version scores below threshold, flag it but don't block (the review step handles this).

**Step 6: Workflow Transitions**

For each content version, call `IContentPipeline.SubmitForReviewAsync(contentId)`:
- **Autonomous mode:** Auto-approves (existing logic). After auto-approval, the orchestrator must explicitly call `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Scheduled)` and set `content.ScheduledAt = DateTimeOffset.UtcNow` for immediate publish. The existing `SubmitForReviewAsync` only auto-approves to `Approved` — it does not schedule.
- **Semi-Auto mode:** Transitions to Review, stops. No auto-publish. User must explicitly approve. **Critical:** Set `CapturedAutonomyLevel = AutonomyLevel.Manual` on all child Content entities in Semi-Auto mode. Without this, the existing `ShouldAutoApproveAsync()` in `WorkflowEngine` will auto-approve children when `ParentContentId` is set and the parent is Approved — contradicting the "no auto-publish" requirement.

Send notification:
- **Autonomous:** Success summary after publish completes
- **Semi-Auto:** "Content ready for review" with topic + platform count

**Step 7: Record Run**

Create/update `AutomationRun` entity with status, timings, content IDs, platform results.

---

## 5. ComfyUI Client

### Purpose
Thin wrapper around ComfyUI's REST + WebSocket API. No business logic — just HTTP/WS communication.

### Interface

```csharp
interface IComfyUiClient
{
    Task<string> QueuePromptAsync(JsonObject workflow, CancellationToken ct);
    Task<ComfyUiResult> WaitForCompletionAsync(string promptId, CancellationToken ct);
    Task<byte[]> DownloadImageAsync(string filename, string subfolder, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
```

### Configuration (`ComfyUiOptions`)
- `BaseUrl` (string, default `"http://192.168.50.47:8188"`)
- `TimeoutSeconds` (int, default `120`)
- `HealthCheckTimeoutSeconds` (int, default `5`)

### Completion Detection

Use WebSocket connection to `/ws?clientId={uuid}` for real-time progress. Listen for the `executing` message type where `data.node` is null — this signals completion. Then call `GET /history/{promptId}` to get output filenames.

Fall back to HTTP polling of `/history/{promptId}` if WebSocket connection fails.

### Health Check

Before queueing work, call `GET /system_stats` with a short timeout. If ComfyUI is unreachable, fail fast with a clear error rather than queueing into the void.

---

## 6. Image Generation Service

### Purpose
Orchestrates the full image generation flow: prompt → ComfyUI → download → store.

### Interface

```csharp
interface IImageGenerationService
{
    Task<ImageGenerationResult> GenerateAsync(string prompt, ImageGenerationOptions options, CancellationToken ct);
}
```

```csharp
record ImageGenerationResult(bool Success, string? FileId, string? Error, long DurationMs);
```

### Workflow Template

Store a FLUX text-to-image workflow JSON template as an embedded resource. The template defines the full node graph (UNETLoader → DualCLIPLoader → CLIPTextEncode → KSampler → VAEDecode → SaveImage). At runtime, inject:
- `prompt` text into the CLIPTextEncode node's `text` input
- `seed` (random Int64) into the KSampler node
- `width` / `height` into the EmptyLatentImage node
- Model checkpoint name into the loader node (configurable via options)

The template should be the API format (numeric node IDs with `class_type` + `inputs`), obtained from ComfyUI's "Save (API Format)" export.

### Flow
1. Health check: `IComfyUiClient.IsAvailableAsync()`
2. Load workflow template, inject parameters
3. Queue: `IComfyUiClient.QueuePromptAsync(workflow)`
4. Wait: `IComfyUiClient.WaitForCompletionAsync(promptId)` with timeout
5. Extract output filename from history response
6. Download: `IComfyUiClient.DownloadImageAsync(filename, subfolder)`
7. Store: `IMediaStorage.SaveAsync(stream, "generated.png", "image/png")`
8. Return `ImageGenerationResult` with fileId

---

## 7. Image Prompt Service

### Purpose
Uses the Claude sidecar to generate FLUX-optimized image prompts from post content.

### Interface

```csharp
interface IImagePromptService
{
    Task<string> GeneratePromptAsync(string postContent, CancellationToken ct);
}
```

### Prompt Engineering

The system prompt instructs Claude to:
- Read the post content and identify the core visual concept
- Generate a FLUX-compatible natural language description
- Target professional, LinkedIn-appropriate visuals: clean minimalist compositions, muted corporate palettes, editorial quality
- Avoid: text in the image, busy compositions, photorealistic human faces, neon/over-saturated colors
- Include style keywords: `minimalist`, `flat design`, `editorial style`, `gradient background`, `high contrast`, `professional corporate`
- Keep the prompt under 200 words (FLUX handles ~500 tokens but shorter is more focused)

The service sends the task to the sidecar via `ISidecarClient.SendTaskAsync()`, consumes the event stream, and returns the prompt text.

---

## 8. Image Resizer

### Purpose
Crops/resizes the generated 1024x1024 image to platform-optimal dimensions.

### Interface

```csharp
interface IImageResizer
{
    Task<IReadOnlyDictionary<PlatformType, string>> ResizeForPlatformsAsync(
        string sourceFileId, PlatformType[] platforms, CancellationToken ct);
}
```

Returns a mapping of platform → fileId for each resized version.

### Platform Dimensions

Source image generated at 1536x1536 — all platform crops only require downscaling (no upscale blur).

| Platform | Width | Height | Aspect | Crop Strategy |
|----------|-------|--------|--------|---------------|
| LinkedIn | 1200 | 627 | ~1.91:1 | Center crop to 1536x803, downscale to 1200x627 |
| TwitterX | 1200 | 675 | 16:9 | Center crop to 1536x864, downscale to 1200x675 |
| Instagram | 1080 | 1080 | 1:1 | Downscale from 1536x1536 |
| PersonalBlog | 1200 | 630 | ~1.9:1 | Same as LinkedIn |

### New NuGet Dependency
- `SkiaSharp` (MIT licensed) for cross-platform image manipulation. Preferred over `SixLabors.ImageSharp` which requires a commercial license for non-open-source use.

---

## 9. LinkedIn Image Upload Extension

### Current State
`LinkedInPlatformAdapter.ExecutePublishAsync()` creates text-only posts via `POST /rest/posts` with `commentary` and `visibility` fields.

### Changes Required

Add image upload capability using LinkedIn's Images API:

**New method: `UploadImageAsync(byte[] imageData, string authorUrn, CancellationToken ct)`**
1. `POST /rest/images?action=initializeUpload` with `owner = authorUrn`
2. Extract `uploadUrl` and `image` URN from response
3. `PUT {uploadUrl}` with raw binary data (`Content-Type: application/octet-stream`)
4. Poll `GET /rest/images/{imageUrn}` until `status == "AVAILABLE"` (max 30s, 2s intervals)
5. Return image URN

**Modify `ExecutePublishAsync()`:**
- Check if `PlatformContent` has an associated image (via metadata or a new field on `PlatformContent`)
- If image exists: call `UploadImageAsync()`, include `content.media` block in post JSON:
  ```
  "content": { "media": { "altText": "...", "id": "urn:li:image:..." } }
  ```
- If no image: post as text-only (current behavior, unchanged)

**Alt text generation:** The sidecar prompt that generates the image description can also produce a short alt text (<120 chars) for accessibility. Store alongside the image prompt.

### OAuth Scope
The current scope `w_member_social` already covers image uploads. No scope change needed.

---

## 10. Platform Content Generation

### New Method on ContentPipeline

```csharp
Task<Result<string>> GeneratePlatformDraftAsync(
    Guid contentId, PlatformType platform, string parentBody, CancellationToken ct);
```

This method:
1. Loads the Content entity
2. Loads brand voice context (existing `BrandProfile`)
3. Builds a platform-specific system prompt with the parent body as context
4. Sends to sidecar for generation
5. Stores the platform-specific body in the Content entity

### Platform System Prompts

Each platform gets a distinct system prompt that defines tone, format, and constraints. These prompts should live in configuration (not hardcoded) so they can be tuned without code changes.

The prompts must reference the `matt-kruczek-linkedin-writer` and `matt-kruczek-twitter-writer` voice patterns established in the user's content writing rules: all content must be humanized, no em-dashes, platform-authentic voice.

**Key constraints per platform:**
- LinkedIn: Max 3000 chars, professional authority, thought leadership, hashtags
- TwitterX: Max 280 chars per tweet (or thread), punchy, opinionated, dev-community credible
- PersonalBlog: Teaser format, drives to full article, SEO-conscious

---

## 11. AutomationRun Entity

### Purpose
Tracks each execution of the daily pipeline for observability, debugging, and preventing duplicate runs.

### Fields

```csharp
record AutomationRun
{
    Guid Id;
    DateTimeOffset TriggeredAt;
    AutomationRunStatus Status;    // Running, Completed, PartialFailure, Failed
    Guid? SelectedSuggestionId;
    Guid? PrimaryContentId;
    string? ImageFileId;
    string? ImagePrompt;
    string? SelectionReasoning;
    string? ErrorDetails;
    DateTimeOffset? CompletedAt;
    long DurationMs;
    int PlatformVersionCount;
}
```

### Enum

```csharp
enum AutomationRunStatus { Running, Completed, PartialFailure, Failed }
```

### Idempotency
The `DailyContentProcessor` queries this table before each run: if a `Completed` run exists for today's date, skip. This prevents duplicate posts from app restarts, deployments, or clock drift.

---

## 12. Configuration

### appsettings.json Structure

```
ContentAutomation:
  CronExpression          # "0 9 * * 1-5"
  TimeZone                # "Eastern Standard Time"
  Enabled                 # true
  AutonomyLevel           # "Autonomous" | "SemiAuto" | "Manual"
  TopTrendsToConsider     # 5
  TargetPlatforms         # ["LinkedIn", "TwitterX"]
  ImageGeneration:
    Enabled               # true
    ComfyUiBaseUrl        # "http://192.168.50.47:8188"
    WorkflowTemplate      # "flux-text-to-image" (embedded resource name)
    TimeoutSeconds        # 120
    DefaultWidth          # 1536
    DefaultHeight         # 1536
    ModelCheckpoint       # "flux1-dev-fp8.safetensors"
  PlatformPrompts:
    LinkedIn              # System prompt override (optional)
    TwitterX              # System prompt override (optional)
    PersonalBlog          # System prompt override (optional)
```

### DI Registration

Register in `AddInfrastructure()`:
- `services.AddScoped<IDailyContentOrchestrator, DailyContentOrchestrator>()`
- `services.AddSingleton<IComfyUiClient, ComfyUiClient>()` (singleton for HttpClient via IHttpClientFactory; WebSocket connections created per-request, not held persistently — ComfyUI restarts would silently kill a persistent WebSocket)
- `services.AddScoped<IImageGenerationService, ImageGenerationService>()`
- `services.AddScoped<IImagePromptService, ImagePromptService>()`
- `services.AddSingleton<IImageResizer, ImageResizer>()`
- `services.AddHostedService<DailyContentProcessor>()`
- `services.Configure<ContentAutomationOptions>(config.GetSection("ContentAutomation"))`

---

## 13. Notification Integration

### Existing Infrastructure
PBA already has `INotificationService` used by `PublishingPipeline` for partial failure notifications. The orchestrator hooks into this same service.

### Notification Events

| Event | Channel | Message |
|-------|---------|---------|
| Image generation failed | Dashboard + Discord | "Daily content pipeline failed: ComfyUI image generation error. Content held for review. Topic: {topic}" |
| Content ready for review (Semi-Auto) | Dashboard + Discord | "New content ready for review: {topic} — {count} platform versions. Review at {dashboard_url}" |
| Pipeline completed (Autonomous) | Dashboard + Discord (optional) | "Published: {topic} to {platforms}. Run duration: {duration}s" |
| No trends available | Dashboard | "Daily content pipeline skipped: no trending topics met the relevance threshold" |
| Consecutive failures (3+) | Dashboard + Discord | "Daily content pipeline has failed {count} consecutive days. Check ComfyUI health and sidecar connectivity." |

---

## 14. Database Migration

### New Table: `AutomationRuns`
- All fields from the AutomationRun entity (section 11)
- Index on `TriggeredAt` (for idempotency date checks)
- Index on `Status` (for monitoring queries)

### Altered Table: `Contents`
- Add nullable `ImageFileId` column (string, references media storage — this is the platform-specific cropped version)
- Add `ImageRequired` bool column (default false) — when true, the formatter/adapter must verify `ImageFileId` is non-null before publishing. Prevents manual approval from bypassing the image requirement.

Note: `AutomationRun.ImageFileId` stores the original 1536x1536 source image. `Content.ImageFileId` stores the per-platform cropped version. Both are needed.

### Migration Approach
Standard EF Core migration. No data migration needed (new table, new nullable columns).

---

## 15. API Endpoints (Optional Dashboard Support)

While the daily pipeline runs automatically, the dashboard may want to display automation status and allow manual triggers.

### New Endpoint Group: `/api/automation`

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/automation/runs` | List recent automation runs with status |
| `GET` | `/api/automation/runs/{id}` | Get run details |
| `POST` | `/api/automation/trigger` | Manually trigger a pipeline run (rate limited: 1 per 15 min, blocked while Running) |
| `GET` | `/api/automation/config` | Get current automation configuration |
| `PUT` | `/api/automation/config` | Update automation settings (cron, enabled, autonomy, etc.) |

These follow existing patterns: minimal API endpoints in `AutomationEndpoints.cs`, registered via `MapAutomationEndpoints()`.

---

## 16. Error Handling Strategy

### Pipeline Errors (Orchestrator Level)

The orchestrator wraps each step in error handling. On any step failure:
1. Record error details in `AutomationRun`
2. Transition any created content to appropriate state (Review if partially generated, leave as Draft if generation failed)
3. Send notification
4. Return `AutomationRunResult` with failure status

### ComfyUI Errors

| Error | Handling |
|-------|----------|
| Connection refused | Health check catches this early. Fail fast, notify. |
| Prompt validation error | ComfyUI returns `{ "error": "...", "node_errors": {...} }`. Log node errors, fail the image step. |
| Timeout (>120s) | Cancel WebSocket listener, fail the image step. |
| Corrupted output | Validate downloaded bytes (check PNG magic bytes). If invalid, fail. |

### Sidecar Errors

| Error | Handling |
|-------|----------|
| Connection failed | Retry once after 30s. If still fails, abort pipeline. |
| ErrorEvent received | Log error message, fail the current step. |
| Timeout (no TaskComplete) | After 60s with no completion event, abort and fail. |

---

## 17. Platform Formatter Changes (Image Passthrough)

### Problem
`LinkedInContentFormatter.FormatAndValidate()` (and other platform formatters) currently hardcode `Array.Empty<MediaFile>()` for the media list. Even if `Content.ImageFileId` is populated, the formatter won't pass it to `PlatformContent.Media`. The adapter never sees image data.

### Image Data Flow (End-to-End)

1. **Orchestrator** stores `imageFileId` on each child Content entity after resizing
2. **Formatter** reads `Content.ImageFileId`, loads image bytes via `IMediaStorage.GetStreamAsync()`, constructs `MediaFile(fileId, mimeType, altText, bytes)` and includes it in `PlatformContent.Media`
3. **Adapter** receives `PlatformContent` with populated `Media` list. For LinkedIn: calls `UploadImageAsync()` with the bytes, gets back an image URN, includes it in the post JSON

### Changes Per Formatter
- **LinkedInContentFormatter:** Check `Content.ImageFileId`. If set, create `MediaFile` with fileId, `"image/png"`, and alt text from `Content.Metadata.PlatformSpecificData["imageAltText"]`. Pass `IMediaStorage` via constructor injection.
- **TwitterContentFormatter:** Same pattern. Twitter handles image upload differently but the formatter's job is just to pass the `MediaFile` through.
- All formatters: Check `Content.ImageRequired` flag. If true and `ImageFileId` is null, return a validation error (prevents publishing without an image).

---

## 18. Circuit Breaker for ComfyUI

### Problem
The ComfyUI server is a workstation that can be offline for maintenance, updates, or power issues. Without a circuit breaker, the pipeline will fail and send notifications every day at 9AM indefinitely.

### Design
Track consecutive ComfyUI failures in the `AutomationRun` table. After `N` consecutive failures where `ErrorDetails` contains "ComfyUI" (configurable, default 3):

- Set `ContentAutomation:ImageGeneration:Enabled = false` in the runtime config
- Send a notification: "ComfyUI has been unreachable for {N} consecutive days. Image generation disabled. Re-enable manually after ComfyUI is restored."
- Pipeline continues to run daily but skips image generation and blocks publishing (since images are required)

When ComfyUI is restored, the user re-enables via:
- `PUT /api/automation/config` with `imageGeneration.enabled = true`, or
- Updating appsettings.json

### Configuration
- `ContentAutomation:ImageGeneration:CircuitBreakerThreshold` (int, default 3)

---

## 19. Implementation Ordering

### Dependency Graph

```
Layer 1 (no dependencies):
  ComfyUiClient
  ContentAutomationOptions / ComfyUiOptions
  AutomationRun entity + migration
  SkiaSharp ImageResizer
  Cronos dependency

Layer 2 (depends on Layer 1):
  ImageGenerationService (depends on ComfyUiClient, MediaStorage)
  ImagePromptService (depends on SidecarClient)

Layer 3 (depends on Layer 2):
  LinkedInContentFormatter changes (image passthrough)
  Other platform formatter changes
  LinkedInPlatformAdapter image upload
  ContentPipeline.GeneratePlatformDraftAsync
  AcceptSuggestionAsync ContentType parameter

Layer 4 (depends on Layer 3):
  DailyContentOrchestrator (depends on everything above)

Layer 5 (depends on Layer 4):
  DailyContentProcessor (scheduling, depends on orchestrator)
  AutomationEndpoints (API, depends on orchestrator)
```

### Recommended Build Order
1. `ComfyUiClient` + `ComfyUiOptions` — can be tested independently against live ComfyUI
2. `ImageResizer` — pure image manipulation, easy to unit test
3. `AutomationRun` entity + EF Core migration — database schema
4. `ImagePromptService` — sidecar integration, testable with mock sidecar
5. `ImageGenerationService` — chains ComfyUI client + media storage
6. `LinkedInContentFormatter` + adapter image upload — extends existing publishing path
7. `ContentPipeline.GeneratePlatformDraftAsync` — extends content generation
8. `DailyContentOrchestrator` — wires everything together
9. `DailyContentProcessor` — scheduling wrapper
10. `AutomationEndpoints` — dashboard API

---

## 20. Dependency Summary

### New NuGet Packages
- `Cronos` (1.x) — Cron expression parsing with timezone support
- `SkiaSharp` (2.x) — Cross-platform image resizing (MIT licensed)

### Existing Dependencies (No Changes)
- `Stateless` — Workflow state machine
- `System.Net.WebSockets` — ComfyUI WebSocket (built into .NET)
- `System.Net.Http` — ComfyUI REST + LinkedIn Images API (built into .NET)
- `Microsoft.AspNetCore.DataProtection` — Token encryption
