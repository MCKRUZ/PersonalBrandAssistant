# TDD Plan: Autonomous Content Workflow

Mirrors the implementation plan structure. Each section lists test stubs to write BEFORE implementing. Uses xUnit + Moq following existing PBA test conventions.

---

## 3. Daily Content Processor (Scheduling)

### Unit Tests
- Test: Processor skips execution when `Enabled = false` in options
- Test: Processor skips execution when an AutomationRun with `Completed` status exists for today
- Test: Processor skips execution when an AutomationRun with `Running` status exists for today
- Test: Processor creates a new IServiceScope and resolves IDailyContentOrchestrator per execution
- Test: Processor creates a failed AutomationRun record when orchestrator throws
- Test: Processor continues to next scheduled occurrence after an error (does not crash)
- Test: Cron expression parsing handles invalid expressions gracefully (logs error, does not start)
- Test: TimeZone string maps correctly to TimeZoneInfo (handles "Eastern Standard Time")

### Integration Tests
- Test: Processor calls ExecuteAsync on the orchestrator when schedule fires (use short interval for test)

---

## 4. Daily Content Orchestrator

### Unit Tests — Step 1: Trend Selection
- Test: Fails with "No trends available" when GetSuggestionsAsync returns empty list
- Test: Filters suggestions to Pending status only
- Test: Sends top N suggestions to sidecar with structured JSON output instructions
- Test: Parses valid JSON response with suggestionId, reasoning, contentType
- Test: Retries sidecar prompt once on JSON parse failure
- Test: Fails run after retry on persistent parse failure
- Test: Queries last 7 days of published content for diversity check

### Unit Tests — Step 2: Content Creation
- Test: Calls AcceptSuggestionAsync with AI-selected ContentType
- Test: Calls GenerateOutlineAsync then GenerateDraftAsync in sequence
- Test: Fails run and records error when AcceptSuggestionAsync returns failure

### Unit Tests — Step 3: Platform Content Generation
- Test: Creates child Content entity per target platform with ParentContentId set
- Test: Calls GeneratePlatformDraftAsync for each platform
- Test: Sets CapturedAutonomyLevel = Manual on children when orchestrator is in SemiAuto mode
- Test: Each child has correct TargetPlatforms (single platform array)

### Unit Tests — Step 4: Image Generation
- Test: Calls ImagePromptService then ImageGenerationService in sequence
- Test: On image failure, transitions ALL content (parent + children) to Review status
- Test: On image failure, sends notification via INotificationService
- Test: On image failure, does NOT proceed to brand validation or workflow transitions
- Test: On image success, calls ImageResizer for all target platforms
- Test: Associates platform-specific imageFileId with each child Content
- Test: Sets ImageRequired = true on all content versions

### Unit Tests — Step 5: Brand Validation
- Test: Calls ValidateVoiceAsync for each content version
- Test: Low brand score does not block pipeline (flags only)

### Unit Tests — Step 6: Workflow Transitions
- Test: In Autonomous mode, calls SubmitForReviewAsync then TransitionAsync(Scheduled) then sets ScheduledAt
- Test: In SemiAuto mode, calls SubmitForReviewAsync only (stops at Review)
- Test: In SemiAuto mode, sends "ready for review" notification
- Test: In Autonomous mode, sends completion notification after all transitions

### Unit Tests — Step 7: Recording
- Test: Creates AutomationRun with Completed status on success
- Test: Creates AutomationRun with Failed status on any step failure
- Test: Records DurationMs accurately
- Test: Records all content IDs and image file IDs

### Integration Tests
- Test: Full pipeline happy path (mock sidecar + ComfyUI, real DB)
- Test: Full pipeline with image failure (verifies no content published)
- Test: Full pipeline in SemiAuto mode (verifies content stops at Review)

---

## 5. ComfyUI Client

### Unit Tests
- Test: QueuePromptAsync sends POST to /prompt with workflow JSON + client_id
- Test: QueuePromptAsync returns prompt_id from response
- Test: QueuePromptAsync throws when ComfyUI returns error response with node_errors
- Test: IsAvailableAsync returns true when GET /system_stats succeeds
- Test: IsAvailableAsync returns false on connection refused
- Test: IsAvailableAsync returns false on timeout (within HealthCheckTimeoutSeconds)
- Test: DownloadImageAsync returns byte array from GET /view endpoint
- Test: DownloadImageAsync constructs correct URL with filename, subfolder, type=output params

### Integration Tests (against live ComfyUI or mock HTTP server)
- Test: WaitForCompletionAsync detects completion via WebSocket (executing with null node)
- Test: WaitForCompletionAsync falls back to HTTP polling when WebSocket fails
- Test: WaitForCompletionAsync throws on timeout (TimeoutSeconds exceeded)
- Test: WebSocket connection is created per-request (not reused across calls)

---

## 6. Image Generation Service

### Unit Tests
- Test: Calls IsAvailableAsync before queueing (health check first)
- Test: Fails with clear error when health check fails
- Test: Loads workflow template and injects prompt, seed, width, height
- Test: Calls QueuePromptAsync with injected workflow
- Test: Calls WaitForCompletionAsync with returned prompt_id
- Test: Extracts output filename from completion result
- Test: Downloads image via DownloadImageAsync
- Test: Stores image via IMediaStorage.SaveAsync with correct MIME type
- Test: Returns ImageGenerationResult with fileId on success
- Test: Returns ImageGenerationResult with error on ComfyUI failure
- Test: Validates downloaded bytes have PNG magic bytes
- Test: Returns error on corrupted output (invalid magic bytes)

---

## 7. Image Prompt Service

### Unit Tests
- Test: Sends post content to sidecar with FLUX-optimized system prompt
- Test: System prompt includes style keywords (minimalist, editorial, gradient, etc.)
- Test: System prompt instructs to avoid text, busy compositions, photorealistic faces
- Test: Returns generated prompt text from sidecar response
- Test: Handles sidecar ErrorEvent gracefully (returns error, does not throw)
- Test: Handles sidecar timeout (60s) gracefully

---

## 8. Image Resizer

### Unit Tests
- Test: Resizes 1536x1536 image to LinkedIn dimensions (1200x627)
- Test: Resizes 1536x1536 image to TwitterX dimensions (1200x675)
- Test: Resizes 1536x1536 image to Instagram dimensions (1080x1080)
- Test: Returns dictionary mapping PlatformType to fileId for each platform
- Test: Center-crops correctly for non-square target aspect ratios
- Test: Stores each resized image via IMediaStorage.SaveAsync
- Test: Handles empty platform array (returns empty dictionary)
- Test: Output is PNG format

---

## 9. LinkedIn Image Upload Extension

### Unit Tests
- Test: UploadImageAsync calls initializeUpload endpoint with correct owner URN
- Test: UploadImageAsync PUTs binary data to upload URL
- Test: UploadImageAsync polls image status until AVAILABLE
- Test: UploadImageAsync returns image URN on success
- Test: UploadImageAsync throws after 30s polling timeout
- Test: ExecutePublishAsync includes media block when PlatformContent has MediaFile
- Test: ExecutePublishAsync omits media block when PlatformContent has no MediaFile (backward compatible)
- Test: Alt text is included in media block

---

## 10. Platform Content Generation

### Unit Tests
- Test: GeneratePlatformDraftAsync loads brand voice context
- Test: GeneratePlatformDraftAsync uses LinkedIn system prompt for LinkedIn platform
- Test: GeneratePlatformDraftAsync uses Twitter system prompt for TwitterX platform
- Test: GeneratePlatformDraftAsync stores generated body in Content entity
- Test: Generated content respects platform character limits (LinkedIn 3000, Twitter 280)
- Test: System prompts reference humanizer rules (no em-dashes)

---

## 11. AutomationRun Entity

### Unit Tests
- Test: AutomationRun can be created with Running status
- Test: AutomationRun status can be updated to Completed with CompletedAt and DurationMs
- Test: AutomationRun status can be updated to Failed with ErrorDetails

### Integration Tests
- Test: AutomationRun persists to database and can be queried by date
- Test: Idempotency query correctly finds Completed/Running runs for today

---

## 15. API Endpoints

### Integration Tests (WebApplicationFactory)
- Test: GET /api/automation/runs returns list of recent runs
- Test: GET /api/automation/runs/{id} returns 404 for unknown ID
- Test: POST /api/automation/trigger returns 429 if run already in progress
- Test: POST /api/automation/trigger returns 429 if triggered within 15 minutes
- Test: POST /api/automation/trigger starts orchestrator and returns run ID
- Test: GET /api/automation/config returns current settings
- Test: PUT /api/automation/config updates settings

---

## 17. Platform Formatter Changes

### Unit Tests
- Test: LinkedInContentFormatter includes MediaFile when Content.ImageFileId is set
- Test: LinkedInContentFormatter excludes MediaFile when Content.ImageFileId is null
- Test: Formatter returns validation error when Content.ImageRequired is true and ImageFileId is null
- Test: Formatter loads image bytes via IMediaStorage when ImageFileId is set
- Test: Alt text is populated from Content.Metadata.PlatformSpecificData

---

## 18. Circuit Breaker

### Unit Tests
- Test: Circuit breaker trips after N consecutive ComfyUI failures
- Test: Circuit breaker sends notification when tripping
- Test: Pipeline skips image generation when circuit breaker is tripped
- Test: Counter resets on successful image generation
