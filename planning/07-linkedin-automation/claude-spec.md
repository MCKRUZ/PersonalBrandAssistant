# Autonomous Content Workflow — Complete Specification

## Overview

Build an end-to-end automated content pipeline that runs on a daily schedule: selects the most compelling trending topic using AI curation, generates platform-specific content for all connected platforms with brand voice, generates a professional image via ComfyUI (Furious workstation, FLUX models), and publishes through the existing PublishingPipeline. The system operates at configurable autonomy levels — in Autonomous mode it runs fully hands-off; in Semi-Auto mode it generates everything but holds for explicit human approval (no auto-publish, no expiry).

## Architecture

### New Services

1. **`DailyContentOrchestrator`** — The central orchestration service that chains the full pipeline: trend curation → content generation (per-platform) → image generation → workflow transitions. Runs all platform versions in a single pipeline execution.

2. **`DailyContentProcessor`** — BackgroundService using Cronos cron expressions for timezone-aware daily scheduling. Replaces the interval-based pattern with a configurable time-of-day trigger (default: 9AM ET, weekdays). Uses `Task.Delay` + `CronExpression.GetNextOccurrence()` pattern. Includes date-based idempotency to prevent duplicate runs.

3. **`IComfyUiClient`** / `ComfyUiClient` — Thin HTTP + WebSocket wrapper for the ComfyUI REST API on Furious (192.168.50.47:8188). Queues prompts via `POST /prompt`, tracks progress via WebSocket `/ws`, retrieves images via `GET /view`. No existing .NET SDK — build from scratch using `HttpClient` + `ClientWebSocket`.

4. **`IImageGenerationService`** / `ImageGenerationService` — Orchestrates the image generation flow: receives post content → sends to sidecar for AI prompt generation → submits workflow to ComfyUI → polls for completion → downloads image → stores via `IMediaStorage`. Handles ComfyUI-specific concerns (workflow JSON templating, model selection, output retrieval).

5. **`IImagePromptService`** / `ImagePromptService` — Uses the sidecar (Claude) to analyze post content and generate a FLUX-optimized image prompt. Returns natural language descriptions targeting professional LinkedIn-style visuals (clean, editorial, corporate). Knows to avoid text in images, use muted corporate palettes, and produce simple compositions.

### Modified Services

6. **`LinkedInPlatformAdapter`** — Extend to support image posts via LinkedIn Images API: `initializeUpload` → `PUT binary` → poll until `AVAILABLE` → create post with `content.media.id`. Currently text-only.

7. **`ContentPipeline`** — Add method `GeneratePlatformDraftAsync(Guid contentId, PlatformType platform)` that generates content tailored to a specific platform's style and constraints. Uses platform-specific system prompts (punchy tweets for Twitter, professional tone for LinkedIn, etc.).

8. **`LinkedInContentFormatter`** / platform formatters — Ensure image metadata (media URN) is passed through to the adapter when publishing.

### Existing Services (No Changes)

- **TrendMonitor** — Already provides `GetSuggestionsAsync()` and `AcceptSuggestionAsync()`
- **WorkflowEngine** — State machine handles all transitions, auto-approval logic intact
- **PublishingPipeline** — Per-platform publishing with idempotency, rate limiting, retry
- **ScheduledPublishProcessor** — Picks up scheduled content and publishes
- **BrandVoiceService** — Validates tone/persona alignment
- **MediaStorage** — File storage with signed URLs

## Pipeline Flow

### Autonomous Mode (Full Auto)

```
DailyContentProcessor fires at 9AM ET
  ↓
DailyContentOrchestrator.ExecuteAsync()
  ↓
1. TREND CURATION
   • TrendMonitor.GetSuggestionsAsync(limit: 5) → top 5 trends
   • Send to sidecar: "Pick the most compelling topic considering engagement potential,
     topic diversity vs last 7 days of published content, and brand alignment"
   • Sidecar returns selected suggestion ID + reasoning
   ↓
2. CONTENT CREATION
   • TrendMonitor.AcceptSuggestionAsync(selectedId) → Content ID (Draft)
   • ContentPipeline.GenerateOutlineAsync(contentId)
   • ContentPipeline.GenerateDraftAsync(contentId) → primary content body
   ↓
3. PLATFORM-SPECIFIC VERSIONS
   For each connected platform:
   • ContentPipeline.GeneratePlatformDraftAsync(contentId, platform)
   • Creates child Content entities linked to parent via ParentContentId
   • Each child has platform-specific body (LinkedIn professional, Twitter punchy, etc.)
   ↓
4. IMAGE GENERATION
   • ImagePromptService.GeneratePromptAsync(primaryContent.Body) → FLUX prompt
   • ImageGenerationService.GenerateAsync(prompt) → ComfyUI workflow execution
   • Downloads image → MediaStorage.SaveAsync() → fileId
   • Associates image fileId with all Content versions via Metadata.PlatformSpecificData
   ↓
   If image generation FAILS:
   • Do NOT publish any platform version
   • Notify user via INotificationService (push + dashboard)
   • Transition all content to Review status (manual intervention required)
   • Log failure details for debugging
   • STOP pipeline execution
   ↓
5. BRAND VALIDATION
   • ContentPipeline.ValidateVoiceAsync(contentId) per platform version
   • If score below threshold → flag for review, notify user
   ↓
6. WORKFLOW TRANSITION
   • ContentPipeline.SubmitForReviewAsync() → auto-approves (Autonomous)
   • WorkflowEngine transitions: Draft → Review → Approved → Scheduled
   • Set ScheduledAt = now (immediate publish) or configurable delay
   ↓
7. PUBLISH (via existing infrastructure)
   • ScheduledPublishProcessor picks up content
   • PublishingPipeline dispatches to each platform adapter
   • LinkedInPlatformAdapter: uploads image + creates post
   • TwitterPlatformAdapter: posts tweet with image
   • Other adapters: format + publish
```

### Semi-Auto Mode

Same pipeline through steps 1-5. At step 6:
- Content transitions to Review (not auto-approved)
- User receives dashboard notification + Discord push
- Content waits indefinitely for explicit approval (no auto-publish, no expiry)
- User reviews in PBA dashboard, approves or edits
- On approval → Scheduled → Published

## ComfyUI Integration

### Connection Details
- **Host:** `192.168.50.47:8188` (Furious workstation)
- **GPU:** RTX 5090, 32GB VRAM
- **Models:** FLUX Dev/Schnell available
- **Auth:** None (ComfyUI has no built-in auth; network-level security via Docker/firewall)

### Workflow Template
Store a base FLUX text-to-image workflow JSON as an embedded resource or config file. At runtime, inject:
- `prompt` text (from ImagePromptService)
- `seed` (random per generation)
- `width/height` (1024x1024 default, then resize per platform)
- Model checkpoint name (configurable)

### Execution Flow
1. `POST /prompt` with workflow JSON + client_id UUID
2. Connect WebSocket `/ws?clientId={uuid}` for progress
3. Wait for `executing` event with `node == null` (completion)
4. `GET /history/{prompt_id}` → extract output filename
5. `GET /view?filename={name}&subfolder={sub}&type=output` → download binary
6. Store via `IMediaStorage.SaveAsync()`

### Image Resizing
After generation at 1024x1024, resize/crop for each platform:
- **LinkedIn:** 1200x627 (landscape feed optimal)
- **Twitter/X:** 1200x675 (in-stream image)
- **Instagram:** 1080x1080 (square)
- **Blog:** 1200x630 (Open Graph)

Use `System.Drawing` or `ImageSharp` for resizing.

## LinkedIn Image Upload

The current `LinkedInPlatformAdapter` only supports text posts. Extend with the Images API flow:

1. `POST /rest/images?action=initializeUpload` → get `uploadUrl` + image URN
2. `PUT {uploadUrl}` with binary image data (`Content-Type: application/octet-stream`)
3. Poll `GET /rest/images/{imageUrn}` until `status == AVAILABLE`
4. Include `content.media.id` = image URN in the post creation request

Required headers: `Linkedin-Version: 202603`, `X-Restli-Protocol-Version: 2.0.0`

## Scheduling Configuration

```json
{
  "ContentAutomation": {
    "CronExpression": "0 9 * * 1-5",
    "TimeZone": "Eastern Standard Time",
    "Enabled": true,
    "AutonomyLevel": "Autonomous",
    "TopTrendsToConsider": 5,
    "TargetPlatforms": ["LinkedIn", "TwitterX"],
    "ImageGeneration": {
      "Enabled": true,
      "ComfyUiBaseUrl": "http://192.168.50.47:8188",
      "WorkflowTemplate": "flux-linkedin-image",
      "TimeoutSeconds": 120,
      "DefaultWidth": 1024,
      "DefaultHeight": 1024
    }
  }
}
```

## Notification Strategy

### Image Generation Failure
- **Dashboard:** Toast notification + item in "Needs Attention" queue
- **Push:** Discord message via existing notification infrastructure
- **Content state:** Transitions to Review (requires manual intervention)

### Semi-Auto Review Ready
- **Dashboard:** Badge count on review queue + toast
- **Push:** Discord message: "New content ready for review: {topic} — {platform count} platform versions"

### Pipeline Completion (Autonomous)
- **Dashboard:** Success toast + activity log entry
- **Push:** Optional Discord summary: "Published: {topic} to {platforms}"

## Data Model Changes

### New Entity: `AutomationRun`
Tracks each execution of the daily pipeline:
- `Id` (Guid)
- `TriggeredAt` (DateTimeOffset)
- `Status` (enum: Running, Completed, PartialFailure, Failed)
- `SelectedSuggestionId` (Guid, nullable)
- `ContentId` (Guid, nullable — primary content)
- `ImageFileId` (string, nullable)
- `PlatformResults` (JSON — per-platform status)
- `ErrorDetails` (string, nullable)
- `CompletedAt` (DateTimeOffset, nullable)
- `DurationMs` (long)

### Content Entity Extensions
- `ImageFileId` (string, nullable) — reference to generated image in MediaStorage
- Already has: `ParentContentId` for parent/child content hierarchy

### New Options Class: `ContentAutomationOptions`
- `CronExpression`, `TimeZone`, `Enabled`
- `AutonomyLevel`, `TopTrendsToConsider`, `TargetPlatforms`
- Nested `ImageGenerationOptions`: `Enabled`, `ComfyUiBaseUrl`, `WorkflowTemplate`, `TimeoutSeconds`, dimensions

## Error Handling

| Failure | Behavior |
|---------|----------|
| No trends available | Log, skip run, notify if persistent (3+ consecutive) |
| Sidecar unavailable | Retry once after 30s, then fail run + notify |
| Content generation fails | Fail run, notify, log error details |
| ComfyUI unreachable | Fail run, block publish, notify (per interview: image required) |
| ComfyUI timeout (>120s) | Fail image step, block publish, notify |
| Image download fails | Retry once, then fail + notify |
| LinkedIn upload fails | Retry via existing PublishingPipeline backoff |
| Rate limited | Defer to existing rate limiter, reschedule |
| Partial platform failure | Publish to successful platforms, notify about failures |

## Dependencies

### New NuGet Package
- **Cronos** — Cron expression parsing with timezone support (replaces PeriodicTimer for daily scheduling)
- **SixLabors.ImageSharp** — Cross-platform image resizing (no System.Drawing dependency)

### Existing
- Stateless (workflow engine)
- System.Net.WebSockets (ComfyUI WebSocket)
- HttpClient (ComfyUI REST, LinkedIn Images API)

## Non-Goals

- Not replacing TrendMonitor or any existing trend source
- Not building a generic workflow engine (using existing WorkflowEngine)
- Not adding new social platform adapters (using existing ones)
- Not supporting video generation (image only for this phase)
- Not building a ComfyUI management UI
- Not handling LinkedIn organization pages (personal profile only)
