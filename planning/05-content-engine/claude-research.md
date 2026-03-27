# Content Engine Research

## Part 1: Codebase Analysis

### Agent Orchestration Layer (Phase 03)

**IAgentOrchestrator** (`Infrastructure/Agents/AgentOrchestrator.cs`):
- `ExecuteAsync(AgentTask, ct)` → `Result<AgentExecutionResult>`
- `GetExecutionStatusAsync(executionId, ct)` — track progress
- `ListExecutionsAsync(contentId, ct)` — query history

**Five Agent Capabilities** (all implement `IAgentCapability`):
1. **WriterAgentCapability** — Blog posts, Standard tier, template "blog-post"
2. **SocialAgentCapability** — Social posts, Fast tier, template "post"
3. **RepurposeAgentCapability** — Cross-format adaptation
4. **EngagementAgentCapability** — Engagement analysis + content suggestions
5. **AnalyticsAgentCapability** — Performance reports

**IAgentCapability pattern:**
```csharp
public interface IAgentCapability
{
    AgentCapabilityType Type { get; }
    ModelTier DefaultModelTier { get; }
    Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct);
}
```

**AgentCapabilityBase** provides: template rendering via `IPromptTemplateService`, chat client init via `IChatClientFactory`, token counting, structured `AgentOutput` building.

**AgentContext:**
```csharp
public record AgentContext
{
    public required Guid ExecutionId { get; init; }
    public required BrandProfilePromptModel BrandProfile { get; init; }
    public ContentPromptModel? Content { get; init; }
    public required IPromptTemplateService PromptService { get; init; }
    public required IChatClient ChatClient { get; init; }
    public required Dictionary<string, string> Parameters { get; init; }
    public required ModelTier ModelTier { get; init; }
}
```

**Prompt Building:** `IPromptTemplateService.RenderAsync(agentName, templateName, variables)` loads from filesystem. Variables: `brand` (BrandProfilePromptModel), `content` (ContentPromptModel), `task` (params dict).

**Token Tracking:** `ITokenTracker` (scoped) — `RecordUsageAsync`, `GetCostForPeriodAsync`, `GetBudgetRemainingAsync`, `IsOverBudgetAsync`. Model tier downgrade on transient errors: Advanced → Standard → Fast.

**Error Handling:** Transient errors (429, 500-504) retry with exponential backoff (2s, 4s, 8s). Execution timeout configurable. Budget exceeded → notification to user.

### Platform Integrations (Phase 04)

**IPublishingPipeline** (`Infrastructure/Services/PlatformServices/PublishingPipeline.cs`):
- `PublishAsync(contentId, ct)` → `Result<Unit>`
- Flow: Load content → for each platform: skip if published/processing → format → rate limit check → publish → update ContentPlatformStatus
- Idempotency via SHA256(contentId:platform:version)
- Partial failure handling (some platforms succeed, others fail)

**IPlatformContentFormatter:**
```csharp
public interface IPlatformContentFormatter
{
    PlatformType Platform { get; }
    Result<PlatformContent> FormatAndValidate(Content content);
}
```
- Twitter: 280 chars, thread splitting, hashtags from Tags
- LinkedIn: Long-form, different line limits
- Instagram: Caption constraints
- YouTube: Title, description, tags

**ISocialPlatform (engagement):**
```csharp
Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct);
```
Returns: `EngagementStats(Likes, Comments, Shares, Impressions, Clicks, PlatformSpecific)`.

### Workflow Engine (Phase 02)

**State Machine:** Draft → Review → Approved → Scheduled → Publishing → Published/Failed
- Auto-approval: `Autonomous` always auto-approves; `SemiAuto` auto-approves if parent is published/approved
- Domain events: `ContentApprovedEvent`, `ContentRejectedEvent`, `ContentScheduledEvent`, `ContentPublishedEvent`
- Concurrent updates via optimistic locking (`xmin` column)

**IContentScheduler:** Manages scheduled time assignment and retrieval.

### Domain Model

**Content Entity:**
- `ContentType` enum: BlogPost, SocialPost, Thread, VideoDescription
- `Body` (text), `Title` (optional), `Status`, `TargetPlatforms` (PlatformType[])
- `Metadata` (ContentMetadata JSONB), `ScheduledAt`, `PublishedAt`
- `CapturedAutonomyLevel`, `ParentContentId` (self-referential for relationships)
- `Version` (uint, concurrency), `RetryCount`, `NextRetryAt`

**ContentMetadata:**
```csharp
public class ContentMetadata
{
    public List<string> Tags { get; set; } = [];
    public List<string> SeoKeywords { get; set; } = [];
    public Dictionary<string, string> PlatformSpecificData { get; set; } = new();
    public string? AiGenerationContext { get; set; }
    public int? TokensUsed { get; set; }
    public decimal? EstimatedCost { get; set; }
}
```

**BrandProfile Entity:**
- `ToneDescriptors` (List<string>), `StyleGuidelines`, `VocabularyPreferences` (VocabularyConfig)
- `Topics`, `PersonaDescription`, `ExampleContent` (List<string>)
- Injected into prompts as `BrandProfilePromptModel`

**AgentExecution Entity:** Tracks per-execution: agent type, model used, token counts, cost, duration, error, output summary.

### Testing Patterns

- **Framework:** xUnit + Moq + MockQueryable
- **Naming:** `{HandlerClass}Tests`, methods `Handle_{Scenario}_{Expected}`
- **Setup:** Constructor initializes mocks, AAA pattern
- **Mock DbSet:** `new List<T>().AsQueryable().BuildMockDbSet()`
- **Assertions:** `result.IsSuccess`, `result.ErrorCode`, `result.Errors`, `result.Value`

### DI & Service Registration

- **Singletons:** IDateTimeProvider, IChatClientFactory, IPromptTemplateService, IEncryptionService, IMediaStorage, TimeProvider
- **Scoped:** IWorkflowEngine, IAgentOrchestrator, ITokenTracker, IContentScheduler, IApprovalService, INotificationService, IPublishingPipeline, IOAuthManager, IRateLimiter, all adapters/formatters
- **Multi-registration:** `IAgentCapability` (5 implementations), `ISocialPlatform` (4), `IPlatformContentFormatter` (4)
- **Config binding:** `services.Configure<T>(configuration.GetSection(T.SectionName))`

### Key Architectural Patterns

- **Result<T>:** `Success(value)`, `Failure(errorCode, errors)`, `NotFound()`, `ValidationFailure()`, `Conflict()`
- **MediatR CQRS:** Commands/queries return `Result<T>`, FluentValidation, LoggingBehavior
- **Auditing:** `AuditableEntityBase` + interceptors for timestamps and audit logs
- **Immutability:** `init` properties, records for DTOs, `with` expressions

---

## Part 2: Web Research

### 1. Content Repurposing Engine Patterns

**Pillar Content Model:** One substantial source asset decomposes into 15-25+ derivative pieces per platform. Not copy-paste — requires structured transformation pipeline.

**Recommended Architecture:**
- **Entity-based modeling:** Model around real entities (articles, insights, threads) not page types
- **Component-level granularity:** Break content into reusable blocks with typed fields
- **Separation of content from presentation:** Structured JSON storage, platform-specific formatters
- **Rich metadata tagging:** Content pillar, audience segment, format type, campaign, platform constraints

**Transformation Pipeline:**
```
Source Content (pillar)
  -> Content Parser (extract structured blocks + key points)
  -> Platform Adapters (format constraints per platform)
  -> Voice Validator (brand consistency check)
  -> Queue/Scheduler (time-slot assignment)
  -> Publisher (API dispatch)
```

**AI-Native Publishing:** Artefact-based model where AI generates content that flows through a Content API into artefact storage then to public rendering. Content pieces are portable artefacts, not tied to specific pages.

Sources: [DEV Community](https://dev.to/yusufhansck/designing-an-ai-native-content-publishing-pipeline-298a), [Storyblok](https://www.storyblok.com/mp/structured-content), [Planable](https://planable.io/blog/repurposing-content/)

### 2. Brand Voice AI Validation

**Three-Layer Hybrid Approach:**

| Layer | Technique | Strength |
|-------|-----------|----------|
| 1 | Few-shot prompting (3-5 exemplars) | Quick setup, intuitive |
| 2 | RAG with brand exemplars (embedding similarity) | Factual accuracy, dynamic context |
| 3 | Fine-tuning (50-100+ examples) | Deep tone/style encoding |

**Practical Architecture:**
```
Content Generation Request
  -> System Prompt (brand voice rules + few-shot examples)
  -> LLM Generation
  -> Post-Generation Scoring:
      - Embedding similarity vs brand exemplars
      - Rule-based checks (forbidden words, required terminology)
      - LLM-as-judge evaluation (tone, formality, persona)
  -> Score > threshold? -> Approve : -> Revision loop
```

**Scoring Dimensions:** Tone alignment, vocabulary consistency, persona fidelity, sentiment alignment, source attribution quality.

**For our system:** Layer 1 (few-shot) is already partially supported via `BrandProfile.ExampleContent`. Layer 2 (embedding scoring) and rule-based checks are the best additions without requiring fine-tuning infrastructure.

Sources: [Entry Point AI](https://www.entrypointai.com/blog/approaches-to-ai-prompt-engineering-embeddings-or-fine-tuning/), [Acrolinx](https://www.acrolinx.com/blog/does-your-ai-speak-your-brand-voice/), [Evidently AI](https://www.evidentlyai.com/llm-guide/llm-evaluation-metrics)

### 3. Content Calendar Scheduling Patterns

**Data Model (Fowler Temporal Expression + iCalendar RRULE):**

Core Entities:
- **ContentItem** — Source content with metadata
- **ScheduleSlot** — Time window on a specific platform
- **RecurrenceRule** — iCalendar RRULE-based pattern (daily, weekly, monthly, custom)
- **ScheduledPost** — Junction: ContentItem + ScheduleSlot + platform formatting
- **ContentSeries** — Groups recurring content (e.g., "Weekly Tips")

**Key Design Decision:** Store recurrence rules, not instances. Generate occurrences at query time. Keeps DB clean, makes pattern changes simple, handles exceptions through override records.

**Queue Slot Management:** Define weekly posting slots per platform/profile (e.g., 3/week LinkedIn, 7/week Twitter). Posts auto-fill next available slot. Queue skips manually-scheduled slots.

**Exception Handling for Recurring Series:**
1. Update only this occurrence (exception record)
2. Update this and all future (split series)
3. Update all occurrences (modify root rule)

Sources: [Martin Fowler - Recurring Events](https://martinfowler.com/apsupp/recurring.pdf), [iCalendar RFC-2445](https://icalendar.org/), [Schema.org Schedule](https://schema.org/Schedule)

### 4. Trend Monitoring APIs and Data Sources

**Recommended Self-Hosted Strategy (Tiered):**

| Tier | Source | Cost | Notes |
|------|--------|------|-------|
| 1 (Free) | TrendRadar (open source) | $0 | 35+ sources, Docker, 512MB RAM, MCP integration |
| 1 (Free) | FreshRSS (self-hosted) | $0 | 1M+ articles, WebSub push, website scraping |
| 1 (Free) | Reddit API | $0 | 100 queries/min, non-commercial |
| 1 (Free) | Hacker News API | $0 | No auth required |
| 2 | Google Trends API (alpha) | $0 | Apply for access, 5yr rolling window |
| 2 | Twitter/X Basic | $200/mo | Required for read access |
| 3 | n8n workflows | $0 | Open source automation for trend pipelines |

**Architecture:**
```
TrendRadar + FreshRSS + Reddit ──> Trend Aggregation Service
                                    -> Deduplication
                                    -> Relevance Scoring (LLM)
                                    -> Topic Clustering
                                    -> Content Calendar Integration
```

**Start with Tier 1** (zero API cost). Add Google Trends and Twitter/X Basic only when free tier data proves insufficient.

Sources: [TrendRadar GitHub](https://github.com/sansan0/TrendRadar), [FreshRSS GitHub](https://github.com/FreshRSS/FreshRSS), [Google Trends API](https://developers.google.com/search/apis/trends)

---

## Cross-Cutting Insights

1. **Content as structured artefacts** — the pillar model + component granularity aligns well with existing `ContentMetadata` JSONB flexibility
2. **Brand voice as middleware** — voice validation should be a cross-cutting concern in the pipeline, not a single step
3. **RRULE-based scheduling** — store rules, generate occurrences at query time
4. **Start cheap on trends** — TrendRadar + FreshRSS + Reddit free tier covers broad ground at zero cost
5. **Existing ParentContentId** already supports content relationship trees for repurposing
6. **BrandProfile.ExampleContent** provides the foundation for few-shot voice matching
