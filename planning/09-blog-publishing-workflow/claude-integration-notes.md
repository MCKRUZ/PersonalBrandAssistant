# Integration Notes: External Review Feedback

Reviewed by: OpenAI GPT-5.2

## Integrating

### 1. Chat conversation windowing (Section 2)
**Suggestion**: Rolling summarization or windowing to prevent unbounded token growth.
**Integrating because**: Valid concern. Long blog authoring sessions will exceed context. Add conversation windowing with periodic summarization.

### 2. SSE persistence consistency (Section 2)
**Suggestion**: Persist assistant message only after stream completes; store partial/in-progress row.
**Integrating because**: Real race condition. Stream cut = inconsistent state.

### 3. Finalize draft output contract (Section 2)
**Suggestion**: Define strict JSON output contract for ExtractFinalDraftAsync with validation.
**Integrating because**: Correct — model may omit sections or include commentary. Structured extraction is more reliable.

### 4. BlogDelayOverride semantics contradiction (Section 6)
**Suggestion**: Pick one model — currently mixes null=skip with null=default plus a separate BlogSkipped bool.
**Integrating because**: This is a genuine bug in the plan. Fix: `TimeSpan? DelayOverride` where null = use default, plus `bool BlogSkipped` as explicit skip flag. Remove the contradictory "null TimeSpan means skip" language.

### 5. Canonical URL timing (Section 4)
**Suggestion**: Blog HTML needs canonical URL to Substack, but URL unknown until RSS detection.
**Integrating because**: Real sequencing issue. Fix: generate blog HTML with placeholder, regenerate with real URL once Substack URL is confirmed. Block "Publish to Blog" until SubstackPostUrl is present.

### 6. URL/path contract mismatch (Section 4)
**Suggestion**: DeployVerificationUrlPattern doesn't match FilePath pattern (missing date prefix and .html).
**Integrating because**: Genuine config inconsistency. Fix: align both patterns.

### 7. Slug/file path collisions (Section 4)
**Suggestion**: Add uniqueness suffix for same-day posts with similar titles.
**Integrating because**: Edge case but real. Add short content ID hash suffix.

### 8. Notification idempotency (Section 11)
**Suggestion**: ScheduledPublishProcessor could spam "Blog ready" notifications every poll tick.
**Integrating because**: Correct — need unique constraint on (ContentId, Type) for pending notifications.

### 9. Race between RSS detection and manual mark (Section 11)
**Suggestion**: Could set SubstackPublishedAt twice, create duplicate notifications.
**Integrating because**: Valid. Add idempotency with unique index on SubstackPostUrl and upsert behavior.

### 10. Dual source of truth for platform status (Section 10)
**Suggestion**: Blog-specific columns + generic ContentPlatformStatus creates consistency trap.
**Integrating because**: Important architectural concern. Fix: make ContentPlatformStatus the authoritative source for publish state, use blog-specific columns only for metadata (URLs, commit SHA, scheduled date) that has no generic equivalent.

### 11. GitHub fine-grained PAT (Section 13)
**Suggestion**: Use fine-grained PAT restricted to single repo instead of repo scope.
**Integrating because**: Correct security practice. Easy win.

### 12. Deploy verification robustness (Section 4)
**Suggestion**: 60s may be insufficient, use exponential backoff, check GitHub Pages build status.
**Integrating because**: Valid. GitHub Pages can take minutes. Add proper retry with backoff.

### 13. Stored XSS in chat rendering (Sections 2, 4)
**Suggestion**: Sanitize markdown rendering with DOMPurify, disable raw HTML in Markdig.
**Integrating because**: Real security concern. Angular auto-escapes by default but markdown renderers can bypass this.

### 14. RSS sliding window instead of timestamp cutoff (Section 5)
**Suggestion**: Always re-scan last 7-14 days and dedupe by guid/link instead of relying on pubDate > lastPoll.
**Integrating because**: More robust approach. Clock skew and backdated pubDates are real.

## NOT Integrating

### 1. Unique marker embedded in Substack content
**Not integrating because**: Too fragile. Substack strips HTML comments and normalizes content. The combination of exact title match + manual "paste Substack URL" fallback is sufficient. Overly clever matching isn't needed for a single-user system publishing ~1-2 posts/week.

### 2. ContentRevision / versioning system
**Not integrating because**: Overengineered for this scope. Content versioning is a separate feature. The current plan tracks finalized content, and the chat history serves as the revision trail.

### 3. Image/asset pipeline
**Not integrating because**: Valid concern but out of scope per the spec. Images are handled manually today and don't block the core workflow. Can be added later.

### 4. Multi-user / authZ
**Not integrating because**: PBA is a single-user system. The existing auth model is sufficient.

### 5. Timezone policy
**Not integrating because**: PBA already stores DateTimeOffset (UTC) throughout. The existing convention applies. Not a new concern for this feature.

### 6. JSON column portability
**Not integrating because**: PBA is already using EF Core with its chosen provider. This is an existing architectural decision, not something to revisit for this feature.

### 7. Prompt injection protection
**Not integrating because**: The chat endpoint is authenticated and single-user. The system prompt contains no secrets. Standard Claude API usage. Not a meaningful attack vector here.

### 8. CSRF on publish endpoints
**Not integrating because**: PBA uses JWT auth (not cookie-based), so CSRF is not applicable.

### 9. Observability additions
**Not integrating because**: Good operational practice but not part of this feature's scope. Can be added as a cross-cutting concern later.

### 10. Manual "mark as verified" for failed deploy verification
**Not integrating because**: The retry button covers this. If deploy truly fails, user can re-trigger. Adding a "mark as verified" bypass adds complexity for a rare edge case.
