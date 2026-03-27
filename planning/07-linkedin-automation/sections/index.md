<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-foundation
section-02-comfyui-client
section-03-image-services
section-04-image-resizer
section-05-formatter-changes
section-06-linkedin-image-upload
section-07-platform-content-gen
section-08-orchestrator
section-09-scheduler
section-10-api-endpoints
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-foundation | - | all | Yes |
| section-02-comfyui-client | 01 | 03 | Yes |
| section-03-image-services | 01, 02 | 08 | No |
| section-04-image-resizer | 01 | 05, 08 | Yes |
| section-05-formatter-changes | 01, 04 | 06, 08 | No |
| section-06-linkedin-image-upload | 05 | 08 | No |
| section-07-platform-content-gen | 01 | 08 | Yes |
| section-08-orchestrator | 03, 05, 06, 07 | 09 | No |
| section-09-scheduler | 08 | 10 | No |
| section-10-api-endpoints | 08 | - | Yes |

## Execution Order

1. **Batch 1:** section-01-foundation (no dependencies)
2. **Batch 2:** section-02-comfyui-client, section-04-image-resizer, section-07-platform-content-gen (parallel after 01)
3. **Batch 3:** section-03-image-services, section-05-formatter-changes (parallel; 03 needs 02, 05 needs 04)
4. **Batch 4:** section-06-linkedin-image-upload (needs 05)
5. **Batch 5:** section-08-orchestrator (needs 03, 05, 06, 07)
6. **Batch 6:** section-09-scheduler, section-10-api-endpoints (parallel after 08)

## Section Summaries

### section-01-foundation
AutomationRun entity, ContentAutomationOptions, ComfyUiOptions, EF Core migration, Content entity changes (ImageFileId, ImageRequired), new interfaces (IDailyContentOrchestrator, IComfyUiClient, IImageGenerationService, IImagePromptService, IImageResizer), DI registration, Cronos + SkiaSharp NuGet packages.

### section-02-comfyui-client
ComfyUiClient implementation: HTTP POST /prompt, WebSocket /ws completion detection with per-request connections, GET /history, GET /view image download, GET /system_stats health check. Configuration via ComfyUiOptions.

### section-03-image-services
ImageGenerationService (workflow template loading, parameter injection, ComfyUI orchestration, media storage) and ImagePromptService (sidecar integration, FLUX-optimized prompt generation with style guidance).

### section-04-image-resizer
SkiaSharp-based image resizer: center crop + downscale from 1536x1536 to platform-specific dimensions (LinkedIn 1200x627, Twitter 1200x675, Instagram 1080x1080, Blog 1200x630). Stores resized versions via IMediaStorage.

### section-05-formatter-changes
Modify LinkedInContentFormatter and other platform formatters to read Content.ImageFileId, construct MediaFile, validate ImageRequired flag. Establishes image data flow from Content entity through PlatformContent.Media to adapters.

### section-06-linkedin-image-upload
Extend LinkedInPlatformAdapter with image upload: initializeUpload, PUT binary, poll until AVAILABLE, include media in post creation. Handles both image and text-only posts (backward compatible).

### section-07-platform-content-gen
Add GeneratePlatformDraftAsync to ContentPipeline: platform-specific system prompts for LinkedIn, TwitterX, PersonalBlog. Configurable prompts via ContentAutomationOptions. Add ContentType override parameter to AcceptSuggestionAsync.

### section-08-orchestrator
DailyContentOrchestrator: chains trend curation (AI-curated with structured JSON), content creation, per-platform generation, image generation, brand validation, workflow transitions. Handles autonomy levels, image failure blocking, Semi-Auto Manual override on children. Circuit breaker for ComfyUI outages.

### section-09-scheduler
DailyContentProcessor BackgroundService: Cronos cron scheduling with timezone awareness, Task.Delay pattern, date-based idempotency (checks Completed + Running), IServiceScope per execution.

### section-10-api-endpoints
AutomationEndpoints: GET /runs, GET /runs/{id}, POST /trigger (rate limited), GET /config, PUT /config. Dashboard support for monitoring and manual triggering.
