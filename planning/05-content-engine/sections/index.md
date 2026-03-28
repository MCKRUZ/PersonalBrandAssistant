<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-domain-entities
section-02-sidecar-integration
section-03-agent-refactoring
section-04-content-pipeline
section-05-content-repurposing
section-06-content-calendar
section-07-brand-voice
section-08-trend-monitoring
section-09-content-analytics
section-10-background-processors
section-11-api-endpoints
section-12-docker-di-config
END_MANIFEST -->

# Content Engine — Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-domain-entities | - | 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12 | Yes |
| section-02-sidecar-integration | 01 | 03, 04, 07, 08 | No |
| section-03-agent-refactoring | 02 | 04, 05, 07, 08 | No |
| section-04-content-pipeline | 03 | 05, 10, 11 | No |
| section-05-content-repurposing | 04 | 10, 11 | Yes |
| section-06-content-calendar | 01 | 10, 11 | Yes (parallel with 05) |
| section-07-brand-voice | 03 | 11 | Yes (parallel with 04-06) |
| section-08-trend-monitoring | 02 | 10, 11 | Yes (parallel with 03-07) |
| section-09-content-analytics | 01 | 10, 11 | Yes (parallel with 02-08) |
| section-10-background-processors | 04, 05, 06, 08, 09 | 11 | No |
| section-11-api-endpoints | 04, 05, 06, 07, 08, 09 | 12 | No |
| section-12-docker-di-config | all | - | No |

## Execution Order

1. section-01-domain-entities (foundation — no dependencies)
2. section-02-sidecar-integration (after 01)
3. section-03-agent-refactoring (after 02)
4. section-04-content-pipeline, section-06-content-calendar, section-07-brand-voice, section-08-trend-monitoring, section-09-content-analytics (parallel batch after 03, except 04 needs 03 directly)
5. section-05-content-repurposing (after 04)
6. section-10-background-processors (after 04, 05, 06, 08, 09)
7. section-11-api-endpoints (after all services)
8. section-12-docker-di-config (final)

## Section Summaries

### section-01-domain-entities
All new domain entities (ContentSeries, CalendarSlot, TrendSource, TrendItem, TrendSuggestion, TrendSuggestionItem, EngagementSnapshot), new enums (TrendSourceType, TrendSuggestionStatus, CalendarSlotStatus), Content entity modifications (TreeDepth, RepurposeSourcePlatform), and EF Core configurations + migration for all new/modified entities.

### section-02-sidecar-integration
ISidecarClient interface, SidecarEvent discriminated union records, SidecarOptions configuration, and the SidecarClient WebSocket implementation with connection management, streaming, reconnection, and health check.

### section-03-agent-refactoring
Rewrite AgentCapabilityBase and AgentOrchestrator to use ISidecarClient instead of IChatClientFactory/IChatClient. Remove ChatClientFactory. Preserve IAgentCapability interface, all 5 capabilities, IPromptTemplateService, and AgentExecution tracking. Update token tracking to parse sidecar events.

### section-04-content-pipeline
IContentPipeline interface, ContentPipeline implementation, ContentCreationRequest model, and MediatR commands/queries for content creation lifecycle (CreateFromTopic, GenerateOutline, GenerateDraft, ValidateVoice, SubmitForReview). Blog writing full agent mode integration.

### section-05-content-repurposing
IRepurposingService interface, RepurposingService implementation, RepurposingSuggestion model, tree-structured content relationships (GetContentTreeAsync), max depth enforcement, idempotency constraints, and autonomy-driven behavior.

### section-06-content-calendar
IContentCalendarService interface, ContentCalendarService implementation, RRULE parsing with Ical.Net, occurrence generation with timezone support, slot management, auto-fill algorithm with transactional safety, and CalendarSlotProcessor for materialization.

### section-07-brand-voice
IBrandVoiceService interface, BrandVoiceService implementation with three-layer validation (prompt injection, rule-based checks with HTML stripping, LLM-as-judge with structured JSON output), BrandVoiceScore model, and autonomy-driven gating logic.

### section-08-trend-monitoring
ITrendMonitor interface, TrendMonitor service, trend source polling (TrendRadar, FreshRSS, Reddit, HackerNews HTTP clients), deduplication by URL canonicalization and fuzzy title matching, LLM relevance scoring via sidecar, topic clustering, and TrendAggregationProcessor.

### section-09-content-analytics
IEngagementAggregator interface, EngagementAggregator implementation, ContentPerformanceReport model, engagement fetching via ISocialPlatform, snapshot retention policy, and on-demand refresh endpoint logic.

### section-10-background-processors
RepurposeOnPublishProcessor, CalendarSlotProcessor, EngagementAggregationProcessor, and TrendAggregationProcessor background services. All with idempotency, autonomy checks, and error handling.

### section-11-api-endpoints
Minimal API endpoint groups: ContentPipelineEndpoints, RepurposingEndpoints, CalendarEndpoints, BrandVoiceEndpoints, TrendEndpoints, AnalyticsEndpoints. All with auth, FluentValidation, and server-side autonomy enforcement.

### section-12-docker-di-config
Docker Compose additions (sidecar, TrendRadar, FreshRSS with internal networking and pinned versions), DI registration for all new services, SidecarOptions/ContentEngineOptions/TrendMonitoringOptions configuration binding, IChatClientFactory removal, and appsettings.json updates.
