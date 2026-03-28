# Integration Notes: Opus Review Feedback

## Integrating

### #1 ContentType immutability — INTEGRATING
Correct. Will change plan to determine ContentType *before* calling AcceptSuggestionAsync. Add a `ContentType` parameter to `AcceptSuggestionAsync` or create content manually with the correct type.

### #3 Auto-approval logic conflict — INTEGRATING
Critical catch. Semi-Auto children with `ParentContentId` will auto-approve, violating the "no auto-publish" requirement. Fix: set `CapturedAutonomyLevel = Manual` on child content in Semi-Auto mode to prevent auto-approval chaining.

### #4 & #5 LinkedInContentFormatter + image data flow — INTEGRATING
Plan was missing the formatter modification. Will add explicit section on modifying `LinkedInContentFormatter.FormatAndValidate()` to read `imageFileId` from metadata, construct `MediaFile`, and pass through to `PlatformContent.Media`. Adapter loads bytes via `IMediaStorage`.

### #6 WebSocket singleton — INTEGRATING
Agree. ComfyUI WebSocket should be per-request, not persistent. HttpClient stays singleton via IHttpClientFactory. WebSocket created fresh for each generation request.

### #7 Resolution upscaling — INTEGRATING
Valid. Will change default generation to 1536x1536 so all platform crops only downscale. RTX 5090 32GB handles this easily.

### #9 Sidecar response parsing — INTEGRATING
Will add structured JSON output instruction to all sidecar prompts. Parse with JsonSerializer, retry once on parse failure, then fail the run.

### #10 Image required flag — INTEGRATING
Will add `ImageRequired` bool on Content entity. Formatter/adapter checks this before publishing. Prevents manual approval from bypassing image requirement.

### #12 Implementation ordering — INTEGRATING
Will add dependency graph and recommended build order.

### #13 Concurrent access — INTEGRATING
Will check for `Running` status in idempotency check, not just `Completed`.

### #14 ImageSharp license — INTEGRATING
Will switch to SkiaSharp (MIT) instead of ImageSharp (commercial license concerns).

### #16 Circuit breaker — INTEGRATING
Will add configurable circuit breaker: after N consecutive ComfyUI failures, pause automation and notify.

### #17 Missing Approved->Scheduled transition — INTEGRATING
Will add explicit `TransitionAsync(Scheduled)` + set `ScheduledAt` after auto-approval in Autonomous mode.

### #18 Rate limiting on trigger endpoint — INTEGRATING
Will add rate limit (1 per 15 min) + Running status check.

### #21 All-dismissed suggestions — INTEGRATING
Will filter for `Status == Pending` when loading suggestions.

## Not Integrating

### #2 GeneratePlatformDraftAsync pattern divergence — NOT INTEGRATING
The existing pattern (reading from AiGenerationContext) works for single-content generation but is awkward for derivative content where the parent body is the primary input. A new method with explicit parameters is cleaner for this use case. Will document this as an intentional pattern deviation.

### #8 Orphaned image cleanup — NOT INTEGRATING
Not worth the complexity for this phase. Images are small (~2MB each), failed runs are infrequent. Can add cleanup later if storage becomes a concern.

### #11 Testing strategy — NOT INTEGRATING (in plan)
Testing is handled by the TDD plan (step 16). Not the responsibility of the implementation plan.

### #15 Header duplication — NOT INTEGRATING
Minor maintenance concern. The per-request headers ensure correctness even if default headers are modified. Not worth changing existing working code.

### #19 AutomationRun record vs class — NOT INTEGRATING
EF Core handles `record class` with mutable properties fine. Using a regular class is equally valid but not a material difference. Will keep as-is and let the implementer choose.

### #20 ImageFileId source of truth — NOT INTEGRATING (clarifying)
Content.ImageFileId is per-version (each platform crop). AutomationRun.ImageFileId is the original 1536x1536. Both are needed. Will clarify in plan.
