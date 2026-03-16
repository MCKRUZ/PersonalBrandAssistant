I now have sufficient context. Let me produce the section content.

# Section 08 -- Trend Monitoring

## Overview

This section implements the trend monitoring subsystem: the `ITrendMonitor` service interface, `TrendMonitor` implementation, HTTP clients for four trend sources (TrendRadar, FreshRSS, Reddit, HackerNews), deduplication logic using URL canonicalization and fuzzy title matching, LLM relevance scoring via the sidecar, topic clustering, and the `TrendAggregationProcessor` background service.

**What this section delivers:**
- `ITrendMonitor` interface in Application layer
- `TrendMonitor` service implementation in Infrastructure layer
- `TrendMonitoringOptions` configuration model
- HTTP client wrappers for each trend source
- Deduplication and clustering logic
- `TrendAggregationProcessor` background service (registered in section-10, defined here)

**Dependencies (must be completed first):**
- **section-01-domain-entities**: Provides `TrendSource`, `TrendItem`, `TrendSuggestion`, `TrendSuggestionItem` entities, `TrendSourceType` and `TrendSuggestionStatus` enums, EF configurations, and `IApplicationDbContext` DbSet additions for all trend entities
- **section-02-sidecar-integration**: Provides `ISidecarClient`, `SidecarEvent` types, and `SidecarOptions` for LLM relevance scoring

---

## Tests

All tests use xUnit + Moq + MockQueryable. Naming convention: `{Class}Tests` for the class, `{Method}_{Scenario}_{Expected}` for methods. AAA pattern throughout. Mock `DbSet` via `BuildMockDbSet()`. Assert via `result.IsSuccess`, `result.ErrorCode`, `result.Value`.

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendMonitorTests.cs`

```csharp
/// ITrendMonitor service tests
public class TrendMonitorTests
{
    // Dependencies: Mock<IApplicationDbContext>, Mock<ISidecarClient>,
    //   Mock<ILogger<TrendMonitor>>, IOptions<TrendMonitoringOptions>

    // --- GetSuggestionsAsync ---
    // Test: GetSuggestionsAsync_WithSuggestions_ReturnsSuggestionsOrderedByRelevanceDescending
    //   Arrange: Seed 3 TrendSuggestion entities with Pending status, varying RelevanceScore
    //   Assert: result.Value is ordered by RelevanceScore descending, count respects limit param

    // Test: GetSuggestionsAsync_NoSuggestions_ReturnsEmptyList

    // --- DismissSuggestionAsync ---
    // Test: DismissSuggestionAsync_ValidId_SetsStatusToDismissed
    //   Arrange: Seed a TrendSuggestion with Pending status
    //   Act: Call DismissSuggestionAsync with its ID
    //   Assert: Status == TrendSuggestionStatus.Dismissed, SaveChangesAsync called

    // Test: DismissSuggestionAsync_InvalidId_ReturnsNotFound

    // --- AcceptSuggestionAsync ---
    // Test: AcceptSuggestionAsync_ValidId_CreatesContentFromSuggestionTopic
    //   Arrange: Seed a TrendSuggestion with Pending status
    //   Act: Call AcceptSuggestionAsync
    //   Assert: Status == TrendSuggestionStatus.Accepted, new Content entity created in Draft status,
    //           Content.Title or metadata contains the suggestion topic, returns the new Content ID

    // Test: AcceptSuggestionAsync_AlreadyAccepted_ReturnsConflict

    // --- RefreshTrendsAsync ---
    // Test: RefreshTrendsAsync_PollsEnabledSourcesOnly
    //   Arrange: Seed 3 TrendSource entities, one disabled
    //   Assert: HTTP clients called only for enabled sources

    // Test: RefreshTrendsAsync_TriggersFullPollCycle
    //   Assert: Calls poll, dedup, score, cluster, and suggestion creation in order
}
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/TrendDeduplicationTests.cs`

```csharp
/// Deduplication logic tests (can be static/pure methods on a helper class)
public class TrendDeduplicationTests
{
    // Test: Deduplicate_SameUrl_DifferentSources_MergesIntoSingleItem
    //   Arrange: Two TrendItems with URLs that canonicalize to the same value
    //   Assert: Output contains only one item

    // Test: Deduplicate_UrlCanonicalization_RemovesTrailingSlashAndQueryParams
    //   Arrange: "https://example.com/post?utm_source=x" and "https://example.com/post/"
    //   Assert: DeduplicationKey is identical

    // Test: Deduplicate_FuzzyTitleMatch_AboveThreshold_MergesItems
    //   Arrange: Two TrendItems with titles above similarity threshold (e.g., 0.85)
    //   Assert: Treated as duplicates

    // Test: Deduplicate_FuzzyTitleMatch_BelowThreshold_KeepsBothItems
    //   Arrange: Two TrendItems with titles below similarity threshold
    //   Assert: Both items retained

    // Test: DeduplicationKey_IsDeterministic_ForSameUrl
    //   Arrange: Call canonicalization twice with same URL
    //   Assert: Same key produced both times
}
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TrendAggregationProcessorTests.cs`

```csharp
/// TrendAggregationProcessor background service tests
public class TrendAggregationProcessorTests
{
    // Dependencies: Mock<ITrendMonitor>, Mock<IServiceScopeFactory>,
    //   Mock<IApplicationDbContext>, Mock<ISidecarClient>,
    //   IOptions<TrendMonitoringOptions>, Mock<ILogger<TrendAggregationProcessor>>

    // Test: Processor_PollsEnabledTrendSourcesOnly
    //   Arrange: Mix of enabled and disabled TrendSource entities
    //   Assert: Only enabled sources are polled

    // Test: Processor_DeduplicatesAcrossSourcesByUrlCanonicalization
    //   Arrange: Same article URL from TrendRadar and FreshRSS
    //   Assert: Single TrendItem persisted with merged source info

    // Test: Processor_DeduplicatesByFuzzyTitleSimilarityAboveThreshold
    //   Arrange: Two items with nearly identical titles, different URLs
    //   Assert: Treated as single trend

    // Test: Processor_ScoresRelevanceViaSidecarClient
    //   Arrange: Mock ISidecarClient.SendTaskAsync to return ChatEvent with JSON scores
    //   Assert: Each TrendItem receives a relevance score

    // Test: Processor_CreatesTrendSuggestionForHighRelevanceItems
    //   Arrange: Items with relevance scores above threshold
    //   Assert: TrendSuggestion entities created with linked TrendSuggestionItem join records

    // Test: Processor_AtAutonomousLevel_AutoCreatesContentForTopSuggestions
    //   Arrange: AutonomyLevel.Autonomous, high-relevance suggestion
    //   Assert: Content entity created in Draft status from suggestion topic

    // Test: Processor_AtManualLevel_DoesNotAutoCreateContent
    //   Arrange: AutonomyLevel.Manual, high-relevance suggestion
    //   Assert: TrendSuggestion created but no Content entity

    // Test: Processor_HandlesSourceApiErrors_ContinuesWithOtherSources
    //   Arrange: One HTTP client throws, others succeed
    //   Assert: Error logged, remaining sources still polled
}
```

### Test File: `tests/PersonalBrandAssistant.Application.Tests/Common/Models/TrendMonitoringOptionsTests.cs`

```csharp
/// Configuration binding test
public class TrendMonitoringOptionsTests
{
    // Test: TrendMonitoringOptions_BindsFromConfiguration_AllPropertiesSet
    //   Arrange: Build IConfiguration from in-memory dictionary with TrendMonitoring section
    //   Assert: All properties (AggregationIntervalMinutes, TrendRadarApiUrl, etc.) bind correctly

    // Test: TrendMonitoringOptions_Defaults_AreReasonable
    //   Assert: default interval is 30 min, subreddits list is not null
}
```

---

## Implementation Details

### 1. TrendMonitoringOptions Configuration Model

**File:** `src/PersonalBrandAssistant.Application/Common/Models/TrendMonitoringOptions.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public class TrendMonitoringOptions
{
    public const string SectionName = "TrendMonitoring";
    public int AggregationIntervalMinutes { get; set; } = 30;
    public string TrendRadarApiUrl { get; set; } = "http://trendradar:8000/api";
    public string FreshRssApiUrl { get; set; } = "http://freshrss:80/api";
    public string[] RedditSubreddits { get; set; } = ["programming", "dotnet", "webdev"];
    public string HackerNewsApiUrl { get; set; } = "https://hacker-news.firebaseio.com/v0";
    public float RelevanceScoreThreshold { get; set; } = 0.6f;
    public float TitleSimilarityThreshold { get; set; } = 0.85f;
    public int MaxSuggestionsPerCycle { get; set; } = 10;
}
```

Bind in appsettings.json under `"TrendMonitoring"` section (see section-12 for DI registration).

### 2. ITrendMonitor Interface

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/ITrendMonitor.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ITrendMonitor
{
    /// <summary>Returns pending suggestions ordered by relevance score descending.</summary>
    Task<Result<IReadOnlyList<TrendSuggestion>>> GetSuggestionsAsync(int limit, CancellationToken ct);

    /// <summary>Marks a suggestion as dismissed.</summary>
    Task<Result<Unit>> DismissSuggestionAsync(Guid suggestionId, CancellationToken ct);

    /// <summary>Accepts a suggestion: creates a Content entity from the topic and returns its ID.</summary>
    Task<Result<Guid>> AcceptSuggestionAsync(Guid suggestionId, CancellationToken ct);

    /// <summary>Triggers a full poll-dedup-score-cluster-suggest cycle on demand.</summary>
    Task<Result<Unit>> RefreshTrendsAsync(CancellationToken ct);
}
```

Uses `Result<T>` from `PersonalBrandAssistant.Application.Common.Models`. References `TrendSuggestion` entity from `PersonalBrandAssistant.Domain.Entities` (provided by section-01). `Unit` is from MediatR.

### 3. TrendMonitor Service Implementation

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendMonitor.cs`

This is the core service. Constructor dependencies:
- `IApplicationDbContext` -- for querying/persisting trend entities
- `ISidecarClient` -- for LLM relevance scoring
- `IHttpClientFactory` -- for HTTP calls to trend source APIs
- `IOptions<TrendMonitoringOptions>` -- configuration
- `ILogger<TrendMonitor>` -- structured logging

**Key methods and responsibilities:**

**`GetSuggestionsAsync(int limit, CancellationToken ct)`**
- Query `TrendSuggestions` with `Status == Pending`, ordered by `RelevanceScore` descending
- Include `RelatedTrends` navigation via `.Include()`
- Take `limit` items
- Return as `IReadOnlyList<TrendSuggestion>`

**`DismissSuggestionAsync(Guid suggestionId, CancellationToken ct)`**
- Find suggestion by ID, return `NotFound` if missing
- Set `Status = TrendSuggestionStatus.Dismissed`
- `SaveChangesAsync`

**`AcceptSuggestionAsync(Guid suggestionId, CancellationToken ct)`**
- Find suggestion by ID, return `NotFound` if missing
- Return `Conflict` if already accepted
- Set `Status = TrendSuggestionStatus.Accepted`
- Create a `Content` entity via `Content.Create(suggestion.SuggestedContentType, body: "", title: suggestion.Topic, targetPlatforms: suggestion.SuggestedPlatforms)`
- Add Content to `DbContext.Contents`
- `SaveChangesAsync`
- Return the new Content's `Id`

**`RefreshTrendsAsync(CancellationToken ct)`**
- Delegates to the internal poll cycle (same logic used by the background processor)
- Steps:
  1. **Poll**: Call each enabled source's HTTP client
  2. **Deduplicate**: Canonicalize URLs, fuzzy-match titles
  3. **Score**: Batch relevance scoring via sidecar
  4. **Cluster**: Group related items by topic similarity
  5. **Suggest**: Create `TrendSuggestion` + `TrendSuggestionItem` records

### 4. Trend Source HTTP Clients

Each source gets a thin wrapper. These can be private methods on `TrendMonitor` or extracted into internal classes. Use `IHttpClientFactory` named clients for each.

**TrendRadar Client:**
- `GET {TrendRadarApiUrl}/trends` -- returns JSON array of trending topics
- Map response to `TrendItem` with `SourceType = TrendSourceType.TrendRadar`
- Parse: title, description, URL, detected timestamp

**FreshRSS Client:**
- `GET {FreshRssApiUrl}/greader.php/reader/api/0/stream/contents/reading-list` -- GReader-compatible API
- Filter to unread items
- Map each item to `TrendItem` with `SourceType = TrendSourceType.FreshRSS`

**Reddit Client:**
- For each subreddit in `RedditSubreddits`:
  - `GET https://www.reddit.com/r/{subreddit}/hot.json?limit=25`
  - Set `User-Agent` header (Reddit requires this)
  - Map each post to `TrendItem` with `SourceType = TrendSourceType.Reddit`
- Rate limit: respect Reddit's 100 queries/min via small delay between subreddit calls

**HackerNews Client:**
- `GET {HackerNewsApiUrl}/topstories.json` -- returns array of item IDs
- Fetch top N items (configurable, default 30): `GET {HackerNewsApiUrl}/item/{id}.json`
- Map to `TrendItem` with `SourceType = TrendSourceType.HackerNews`
- Parallelize item fetches with `Task.WhenAll` (bounded concurrency via `SemaphoreSlim`)

Each client should:
- Handle HTTP errors gracefully (log and continue, do not abort the full cycle)
- Set `TrendItem.DetectedAt` from source timestamp or `DateTimeOffset.UtcNow`
- Populate `TrendItem.SourceName` with the specific source name (e.g., subreddit name, feed name)

### 5. Deduplication Logic

Deduplication runs after all sources are polled, on the combined list of raw `TrendItem` candidates. Two strategies work together:

**URL Canonicalization:**
- Strip query parameters (`utm_source`, `utm_medium`, `utm_campaign`, `ref`, `source`, and all utm_ prefixed params)
- Remove trailing slashes
- Normalize to lowercase
- Compute a deterministic hash (SHA256 of canonical URL) as `DeduplicationKey`
- Items with the same `DeduplicationKey` are merged (keep the one with the earliest `DetectedAt`)

**Fuzzy Title Matching:**
- For items without a URL or with different canonical URLs, compare titles
- Use a simple similarity metric: normalized Levenshtein distance or Jaccard similarity on word n-grams
- If similarity exceeds `TitleSimilarityThreshold` (default 0.85), treat as duplicate
- Merge: keep the item from the higher-priority source (TrendRadar > FreshRSS > Reddit > HackerNews)

The deduplication logic should be implemented as pure static methods on a helper class (e.g., `TrendDeduplicator`) for easy unit testing without mocks.

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/TrendDeduplicator.cs`

```csharp
/// <summary>Pure static deduplication logic for trend items.</summary>
internal static class TrendDeduplicator
{
    /// <summary>Canonicalizes a URL by removing tracking params, trailing slashes, lowercasing.</summary>
    public static string CanonicalizeUrl(string url) { ... }

    /// <summary>Computes a deterministic deduplication key from a canonical URL.</summary>
    public static string ComputeDeduplicationKey(string canonicalUrl) { ... }

    /// <summary>Returns similarity score (0-1) between two titles using word-level Jaccard.</summary>
    public static float ComputeTitleSimilarity(string title1, string title2) { ... }

    /// <summary>Deduplicates a list of trend items by URL and title similarity.</summary>
    public static IReadOnlyList<TrendItem> Deduplicate(
        IEnumerable<TrendItem> items, float titleSimilarityThreshold) { ... }
}
```

### 6. LLM Relevance Scoring

After deduplication, score each item's relevance to the brand using the sidecar.

**Approach -- batch scoring prompt:**
- Collect up to 50 deduplicated items per batch
- Build a prompt that includes: the brand profile summary (from active `BrandProfile`), the list of items (title + description), and instructions to output a JSON array of `{ "index": N, "score": 0.0-1.0, "rationale": "..." }`
- Send via `ISidecarClient.SendTaskAsync`
- Collect `ChatEvent` text fragments, concatenate, parse JSON response
- Map scores back to items by index
- If sidecar returns invalid JSON or errors, log and assign a default score of 0.0 (item will not become a suggestion)

Update each `TrendItem` with its relevance score before persisting.

### 7. Topic Clustering

After scoring, group high-relevance items into topic clusters to avoid creating redundant suggestions.

**Simple approach:**
- Filter items to those above `RelevanceScoreThreshold`
- Sort by relevance descending
- Greedy clustering: for each item, check title similarity against existing cluster centroids
- If similarity above threshold, add to that cluster; otherwise start a new cluster
- Each cluster becomes one `TrendSuggestion`

**Cluster-to-suggestion mapping:**
- `TrendSuggestion.Topic` = highest-scored item's title (or LLM-generated summary if available)
- `TrendSuggestion.Rationale` = aggregated rationale from relevance scoring
- `TrendSuggestion.RelevanceScore` = max score in the cluster
- `TrendSuggestion.SuggestedContentType` = inferred from source type and content (blog posts for long-form, social posts for news)
- `TrendSuggestion.SuggestedPlatforms` = derived from content type
- `TrendSuggestion.Status` = `TrendSuggestionStatus.Pending`
- Create `TrendSuggestionItem` join entities linking the suggestion to all `TrendItem` records in the cluster, with a `SimilarityScore` field

### 8. TrendAggregationProcessor Background Service

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TrendAggregationProcessor.cs`

This is a `BackgroundService` that runs the poll cycle on a timer. It is defined here but registered in section-10 alongside other background processors.

```csharp
/// <summary>
/// Background service that periodically polls trend sources, deduplicates,
/// scores relevance, clusters topics, and creates trend suggestions.
/// </summary>
public class TrendAggregationProcessor : BackgroundService
{
    // Constructor: IServiceScopeFactory, IOptions<TrendMonitoringOptions>, ILogger

    // ExecuteAsync: loop with delay of AggregationIntervalMinutes
    //   - Create scope, resolve ITrendMonitor
    //   - Call RefreshTrendsAsync
    //   - At Autonomous level: resolve IApplicationDbContext, check top suggestions,
    //     auto-call AcceptSuggestionAsync for highest-relevance pending suggestions
    //   - Catch all exceptions, log, continue loop (never crash the host)
}
```

**Autonomy behavior in the processor:**
- Resolve `AutonomyConfiguration` from DbContext to check current level
- **Autonomous**: After creating suggestions, auto-accept the top N (configurable, default 1) suggestions per cycle, which creates Content entities in Draft status
- **SemiAuto / Manual**: Create suggestions only; user triggers acceptance via API

**Error handling:**
- Each source poll is wrapped in try/catch -- a failing source does not prevent other sources from being polled
- Sidecar scoring errors result in items being skipped (score = 0.0)
- All errors logged with structured logging (source name, error type)
- The outer loop catches all exceptions to prevent background service termination

### 9. DbSet Additions Required

The following DbSet properties must exist on `IApplicationDbContext` (added by section-01):
- `DbSet<TrendSource> TrendSources`
- `DbSet<TrendItem> TrendItems`
- `DbSet<TrendSuggestion> TrendSuggestions`
- `DbSet<TrendSuggestionItem> TrendSuggestionItems`

### 10. Named HttpClient Registration

Register named HTTP clients for each trend source. This is wired up in section-12 (DI configuration), but the `TrendMonitor` service expects these names:

- `"TrendRadar"` -- base address from `TrendMonitoringOptions.TrendRadarApiUrl`
- `"FreshRSS"` -- base address from `TrendMonitoringOptions.FreshRssApiUrl`
- `"Reddit"` -- base address `https://www.reddit.com`, with custom `User-Agent` header
- `"HackerNews"` -- base address from `TrendMonitoringOptions.HackerNewsApiUrl`

---

## File Summary (Actual)

| File | Action | Description |
|------|--------|-------------|
| `src/.../Application/Common/Models/TrendMonitoringOptions.cs` | Create | Configuration POCO (added MaxAutoAcceptPerCycle, HackerNewsTopN, HackerNewsConcurrency) |
| `src/.../Application/Common/Interfaces/ITrendMonitor.cs` | Create | Service interface (4 methods) |
| `src/.../Infrastructure/Services/ContentServices/TrendMonitor.cs` | Create | Core service (~310 lines after poller extraction) |
| `src/.../Infrastructure/Services/ContentServices/TrendDeduplicator.cs` | Create | Pure static deduplication helpers (no input mutation per review fix H2) |
| `src/.../Infrastructure/Services/ContentServices/TrendPollers/ITrendSourcePoller.cs` | Create | Poller interface (public, SourceType + PollAsync) |
| `src/.../Infrastructure/Services/ContentServices/TrendPollers/TrendRadarPoller.cs` | Create | TrendRadar HTTP poller |
| `src/.../Infrastructure/Services/ContentServices/TrendPollers/FreshRssPoller.cs` | Create | FreshRSS GReader API poller |
| `src/.../Infrastructure/Services/ContentServices/TrendPollers/RedditPoller.cs` | Create | Reddit JSON API poller (per-subreddit) |
| `src/.../Infrastructure/Services/ContentServices/TrendPollers/HackerNewsPoller.cs` | Create | HackerNews poller (parallel via SemaphoreSlim) |
| `src/.../Infrastructure/BackgroundJobs/TrendAggregationProcessor.cs` | Create | Background service (interval-guarded) |
| `tests/.../Services/ContentServices/TrendMonitorTests.cs` | Create | 15 tests (CRUD + parsing) |
| `tests/.../Services/ContentServices/TrendDeduplicationTests.cs` | Create | 8 tests (URL canon, fuzzy match, dedup) |
| `tests/.../BackgroundJobs/TrendAggregationProcessorTests.cs` | Create | 3 tests (cycle, failure handling) |
| `tests/.../Common/Models/TrendMonitoringOptionsTests.cs` | Create | 2 tests (config binding, defaults) |

**Deviations from plan:**
- HTTP pollers extracted into `TrendPollers/` directory (4 separate classes) per code review M1
- Relevance scores carried via `Dictionary<int, float>` instead of mutating TrendItem.Description (H1 fix)
- DeduplicationKey set in caller (TrendMonitor), not in TrendDeduplicator (H2 fix)
- Cross-cycle dedup added: checks existing DeduplicationKeys in DB before inserting (M3 fix)
- Limit validation added to GetSuggestionsAsync (H4 fix)
- AggregationIntervalMinutes guarded with Math.Max(1, ...) (H5 fix)
- ParseRelevanceScores made internal for testability (3 unit tests added)
- Autonomy-level gating in processor deferred to section-10 registration

**Test count:** 28 tests (15 + 8 + 3 + 2). All 752 total tests pass.

---

## Key Design Decisions

1. **Deduplication is pure/static** -- The `TrendDeduplicator` class has no dependencies, making it trivially testable. URL canonicalization and title similarity are deterministic pure functions.

2. **Batch LLM scoring** -- Rather than scoring items one-by-one (expensive, slow), items are batched into a single sidecar prompt. The prompt asks for structured JSON output mapping indices to scores.

3. **Greedy clustering** -- A simple single-pass clustering approach is used rather than a full ML clustering algorithm. This keeps the implementation straightforward and avoids external ML library dependencies. The title similarity function used for deduplication is reused for clustering.

4. **Error isolation** -- Each trend source poll is independent. A failing Reddit API does not block TrendRadar or FreshRSS polling. Sidecar scoring failures result in zero-scored items that simply do not become suggestions.

5. **Autonomy gating at the processor level** -- The `TrendAggregationProcessor` checks autonomy before auto-accepting suggestions. The `TrendMonitor` service itself is autonomy-agnostic; it creates suggestions regardless of level. The gating decision (auto-accept vs wait for user) is made in the processor.