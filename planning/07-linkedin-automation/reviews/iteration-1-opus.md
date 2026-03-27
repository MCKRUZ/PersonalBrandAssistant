# Opus Review

**Model:** claude-opus-4-6
**Generated:** 2026-03-24T01:30:00Z

---

## Critical Issues

### 1. ContentType is immutable
`Content.ContentType` has `private init` setter (Content.cs:29). Plan says to update it after `AcceptSuggestionAsync` creates the entity, which won't work. Fix: decide content type *before* calling Accept and pass it in.

### 2. GeneratePlatformDraftAsync architectural mismatch
Plan passes `parentBody` as parameter, but existing `GenerateDraftAsync` reads everything from entity's `AiGenerationContext`. Diverges from established pattern without explanation.

### 3. Auto-approval logic conflict
In Semi-Auto mode, plan says "no auto-publish." But `WorkflowEngine.cs:132-138` auto-approves SemiAuto children when parent is Approved. Since plan creates children with `ParentContentId`, they will auto-approve. Contradicts the "no auto-publish" requirement.

### 4. LinkedInContentFormatter hardcodes empty media
`LinkedInContentFormatter.cs:43` always returns `Array.Empty<MediaFile>()`. Even with image metadata on Content, formatter won't pass it through. Plan mentions this obliquely in spec but omits it from implementation sections.

### 5. Image data flow gap
Plan never describes how image bytes flow from `IMediaStorage` through the formatter to the LinkedIn upload API. The `PlatformContent.Media` mechanism exists but isn't wired up.

## Design Concerns

### 6. ComfyUI WebSocket singleton lifecycle
Holding persistent WebSocket in singleton will fail silently when ComfyUI restarts. Create per-request WebSocket connections instead.

### 7. Upscaling 1024x1024 to 1200x627
Center-cropping then upscaling introduces blur. Generate at higher resolution (1536x1536 or landscape 2048x1024) since RTX 5090 handles it easily.

### 8. No cleanup of orphaned images on failure

### 9. Sidecar response parsing fragility
Parsing Guid from free-text LLM output is unreliable. Need structured JSON output instructions.

### 10. No "image required" flag on Content
Manual approval could bypass image requirement. Need `ImageRequired` flag or `ImageFileId != null` check as publishing precondition.

## Missing Considerations

### 11. No testing strategy (80% coverage requirement)
### 12. No implementation ordering / dependency graph
### 13. No concurrent access protection for manual triggers
### 14. ImageSharp license (commercial use concerns) - consider SkiaSharp (MIT)
### 15. LinkedIn API version header duplication
### 16. No circuit breaker for persistent ComfyUI outages
### 17. Missing Approved->Scheduled transition step in Autonomous mode
### 18. No rate limiting on manual trigger endpoint
### 19. AutomationRun uses record but needs mutable semantics
### 20. ImageFileId source of truth unclear (Content vs AutomationRun)
### 21. No handling of all-dismissed/accepted suggestions
