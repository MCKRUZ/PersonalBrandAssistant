# Content Engine — TDD Plan

Testing framework: xUnit + Moq + MockQueryable. Naming: `{Class}Tests`, methods `Handle_{Scenario}_{Expected}` or `{Method}_{Scenario}_{Expected}`. AAA pattern. Mock DbSet via `BuildMockDbSet()`. Assert via `result.IsSuccess`, `result.ErrorCode`, `result.Value`.

---

## 2. Sidecar Integration Architecture

### ISidecarClient / SidecarEvent models
- Test: SidecarEvent record types are correctly discriminated (ChatEvent, FileChangeEvent, etc.)
- Test: SidecarOptions binds from configuration section correctly

### SidecarClient implementation
- Test: ConnectAsync establishes WebSocket connection (mock WebSocket server)
- Test: ConnectAsync times out after configured timeout
- Test: SendTaskAsync streams events as IAsyncEnumerable
- Test: SendTaskAsync yields TaskCompleteEvent with token counts
- Test: SendTaskAsync cancels on CancellationToken
- Test: AbortAsync sends abort message with session ID
- Test: Automatic reconnection on disconnect (up to 3 transport retries)
- Test: IsConnected reflects actual connection state
- Test: Health check returns healthy/unhealthy based on connection

### Phase 03 Agent Refactoring
- Test: AgentCapabilityBase.ExecuteAsync sends prompt to ISidecarClient
- Test: AgentCapabilityBase.ExecuteAsync collects text from ChatEvent stream
- Test: AgentCapabilityBase.ExecuteAsync extracts token usage from TaskCompleteEvent
- Test: AgentOrchestrator no longer references IChatClientFactory
- Test: WriterAgentCapability generates correct prompt template
- Test: SocialAgentCapability generates correct prompt template
- Test: RepurposeAgentCapability generates correct prompt template
- Test: Budget tracking still works with sidecar token events

---

## 3. Content Creation Pipeline

### IContentPipeline
- Test: CreateFromTopicAsync creates Content entity in Draft status with topic in metadata
- Test: CreateFromTopicAsync returns validation failure for empty topic
- Test: GenerateOutlineAsync sends outline task to ISidecarClient
- Test: GenerateOutlineAsync stores outline in ContentMetadata.AiGenerationContext
- Test: GenerateOutlineAsync fails if content not found
- Test: GenerateDraftAsync sends draft task with brand voice context
- Test: GenerateDraftAsync updates Content.Body with generated text
- Test: GenerateDraftAsync for BlogPost captures file path and commit hash from events
- Test: ValidateVoiceAsync delegates to IBrandVoiceService.ScoreContentAsync
- Test: SubmitForReviewAsync transitions content to Review status via workflow engine
- Test: SubmitForReviewAsync at Autonomous level auto-approves

### MediatR Commands
- Test: CreateFromTopicCommand validates via FluentValidation
- Test: GenerateOutlineCommand returns NotFound for invalid contentId
- Test: GenerateDraftCommand returns NotFound for invalid contentId
- Test: SubmitForReviewCommand returns conflict if content already submitted

---

## 4. Content Repurposing

### IRepurposingService
- Test: RepurposeAsync creates child Content for each target platform
- Test: RepurposeAsync sets ParentContentId on children
- Test: RepurposeAsync sets RepurposeSourcePlatform
- Test: RepurposeAsync respects max tree depth (default 3) — fails if exceeded
- Test: RepurposeAsync is idempotent — skips if child already exists for (Parent, Platform, Type)
- Test: SuggestRepurposingAsync returns suggestions with confidence scores
- Test: GetContentTreeAsync returns full descendant tree via recursive query

### Autonomy behavior
- Test: Autonomous — auto-triggers repurpose on content publish
- Test: SemiAuto — auto-triggers only for published parents
- Test: Manual — creates suggestions only, no auto-generation

### RepurposeOnPublishProcessor
- Test: Processor triggers on Published status change
- Test: Processor checks autonomy level before processing
- Test: Processor is idempotent on duplicate events

---

## 5. Content Calendar & Scheduling

### ContentSeries entity
- Test: ContentSeries validates RRULE format
- Test: ContentSeries requires TimeZoneId
- Test: EF configuration maps PlatformType[] and ThemeTags correctly

### CalendarSlot entity
- Test: CalendarSlot with IsOverride=true requires OverriddenOccurrence
- Test: EF configuration for CalendarSlot indexes and constraints

### IContentCalendarService
- Test: GetSlotsAsync generates occurrences from RRULE within date range
- Test: GetSlotsAsync uses ContentSeries.TimeZoneId for occurrence generation
- Test: GetSlotsAsync merges materialized slots with generated occurrences
- Test: GetSlotsAsync handles DST boundary correctly
- Test: CreateSeriesAsync validates RRULE string
- Test: CreateManualSlotAsync creates slot with no series reference
- Test: AssignContentAsync fills slot and changes status to Filled
- Test: AssignContentAsync fails if slot already filled
- Test: AutoFillSlotsAsync matches content to slots by platform + topic affinity
- Test: AutoFillSlotsAsync prefers older approved content
- Test: AutoFillSlotsAsync skips already-filled slots

### CalendarSlotProcessor
- Test: Processor materializes upcoming slots from active series
- Test: Processor triggers auto-fill at Autonomous level

---

## 6. Brand Voice System

### IBrandVoiceService
- Test: RunRuleChecks strips HTML before checking
- Test: RunRuleChecks detects avoided terms
- Test: RunRuleChecks warns when no preferred terms present
- Test: ScoreContentAsync sends scoring prompt to sidecar
- Test: ScoreContentAsync parses JSON response into BrandVoiceScore dimensions
- Test: ScoreContentAsync handles invalid JSON from LLM gracefully (returns error)
- Test: ValidateAndGateAsync at Autonomous auto-regenerates below threshold
- Test: ValidateAndGateAsync at Autonomous fails after 3 regen attempts
- Test: ValidateAndGateAsync at SemiAuto/Manual returns advisory score, no blocking

---

## 7. Trend Monitoring

### Domain entities
- Test: TrendSource validates required fields
- Test: TrendItem.DeduplicationKey is deterministic for same URL
- Test: TrendSuggestionItem join entity maps TrendSuggestion to TrendItem with score
- Test: EF configurations for all trend entities

### ITrendMonitor
- Test: GetSuggestionsAsync returns suggestions ordered by relevance
- Test: DismissSuggestionAsync sets status to Dismissed
- Test: AcceptSuggestionAsync creates Content from suggestion topic
- Test: RefreshTrendsAsync triggers poll cycle

### TrendAggregationProcessor
- Test: Processor polls enabled TrendSources only
- Test: Processor deduplicates across sources by URL canonicalization
- Test: Processor deduplicates by fuzzy title similarity above threshold
- Test: Processor scores relevance via ISidecarClient
- Test: Processor creates TrendSuggestion for high-relevance items
- Test: At Autonomous level, auto-creates content for top suggestions

---

## 8. Content Analytics

### EngagementSnapshot entity
- Test: Impressions and Clicks are nullable
- Test: EF configuration includes index on (ContentPlatformStatusId, FetchedAt DESC)

### IEngagementAggregator
- Test: FetchLatestAsync calls ISocialPlatform.GetEngagementAsync
- Test: FetchLatestAsync saves new EngagementSnapshot
- Test: GetPerformanceAsync aggregates across platforms
- Test: GetPerformanceAsync calculates CostPerEngagement from AgentExecution cost
- Test: GetTopContentAsync returns top content by total engagement in range

### EngagementAggregationProcessor
- Test: Processor queries published content within retention window
- Test: Processor respects rate limits via IRateLimiter
- Test: Processor handles platform API errors gracefully (partial success)
- Test: Retention cleanup removes snapshots older than policy allows

---

## 9. API Endpoints

- Test: All endpoints require authentication
- Test: POST /api/content/create validates request and returns contentId
- Test: POST /api/content/{id}/repurpose returns list of created child IDs
- Test: GET /api/calendar returns merged slots for date range
- Test: POST /api/calendar/auto-fill enforces autonomy check server-side
- Test: POST /api/trends/refresh triggers manual refresh
- Test: GET /api/analytics/top returns performance data

---

## 10-11. Docker Compose & EF Migrations

### Docker Compose
- Test: Sidecar service not published to external ports (validation test on compose file)

### EF Migrations
- Test: Migration creates all new tables (ContentSeries, CalendarSlots, TrendSources, TrendItems, TrendSuggestions, TrendSuggestionItems, EngagementSnapshots)
- Test: Content entity has new columns (TreeDepth, RepurposeSourcePlatform)
- Test: Unique constraint on (ParentContentId, Platform, ContentType) for repurposing idempotency

---

## 12. DI Registration

- Test: ISidecarClient resolves as singleton
- Test: IChatClientFactory no longer registered
- Test: IContentPipeline resolves as scoped
- Test: IRepurposingService resolves as scoped
- Test: IContentCalendarService resolves as scoped
- Test: IBrandVoiceService resolves as scoped
- Test: ITrendMonitor resolves as scoped
- Test: IEngagementAggregator resolves as scoped
- Test: All 4 background services are registered as hosted services
