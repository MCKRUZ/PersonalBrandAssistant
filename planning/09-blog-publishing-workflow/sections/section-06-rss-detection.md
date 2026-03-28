# Section 06: RSS Publication Detection

## Overview

Enhances the existing SubstackService with publication detection: 15-minute BackgroundService poller with conditional GET, sliding window dedup, content hashing, and content matching via title similarity. Creates SubstackDetection records and fires domain events.

**Depends on:** Section 01 (SubstackDetection entity, MatchConfidence enum, SubstackOptions extensions)
**Blocks:** Section 07 (Staggered Scheduling)

---

## Tests (Write First)

### SubstackContentMatcher Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/SubstackContentMatcherTests.cs`

```csharp
// Test: MatchAsync returns High confidence on exact title match with BlogPost content
// Test: MatchAsync returns Medium confidence on fuzzy title match within 48h window
// Test: MatchAsync returns None when no matching content found
// Test: MatchAsync only matches content with ContentType.BlogPost
// Test: MatchAsync only matches content with Substack in TargetPlatforms or Status = Approved
// Test: MatchAsync skips content that already has SubstackPostUrl set
// Test: MatchAsync handles empty title gracefully
// Test: Fuzzy match: "Agent-First Enterprise Part 5" matches "Agent-First Enterprise: Part 5"
// Test: Fuzzy match: "Weekly Notes #12" does NOT match "Weekly Notes #13"
```

### SubstackPublicationPoller Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/SubstackPublicationPollerTests.cs`

```csharp
// Test: Poller parses RSS 2.0 XML correctly
// Test: Poller deduplicates on guid (skips already-seen entries)
// Test: Poller uses sliding window (14 days, not just since last poll)
// Test: Poller sends conditional GET headers (If-None-Match, If-Modified-Since)
// Test: Poller skips processing on 304 Not Modified
// Test: Poller falls back to full processing when conditional headers not supported
// Test: Poller stores SHA-256 content hash for edit detection
// Test: Poller detects content edits via changed hash on same guid
// Test: Poller creates SubstackDetection record on new match
// Test: Poller creates UserNotification on high-confidence match
// Test: Poller logs unmatched entries without creating notification
// Test: Poller handles malformed RSS XML gracefully
// Test: Poller handles network errors without crashing
```

---

## Implementation Details

### Models
File: `src/PersonalBrandAssistant.Application/Common/Models/SubstackRssEntry.cs`
```csharp
public record SubstackRssEntry(string Guid, string Title, string Link, DateTimeOffset PublishedAt, string? ContentEncoded, string ContentHash);
public record FeedFetchResult(bool NotModified, string? ETag, DateTimeOffset? LastModified, IReadOnlyList<SubstackRssEntry> Entries);
```

### Enhanced ISubstackService
Modify: `src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackService.cs` -- Add:
```csharp
Task<Result<FeedFetchResult>> FetchFeedEntriesAsync(string? etag, DateTimeOffset? ifModifiedSince, CancellationToken ct);
```

### Enhanced SubstackService
Modify: `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackService.cs`

Add `FetchFeedEntriesAsync`: conditional GET headers, RSS parsing, `content:encoded` extraction, SHA-256 hashing, sliding window filter (14 days).

### ISubstackContentMatcher
File: `src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackContentMatcher.cs`
```csharp
public interface ISubstackContentMatcher { Task<ContentMatchResult> MatchAsync(SubstackRssEntry entry, CancellationToken ct); }
public record ContentMatchResult(Guid? ContentId, MatchConfidence Confidence, string MatchReason);
```

### SubstackContentMatcher
File: `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackContentMatcher.cs`

Matching algorithm:
1. Exact title match (case-insensitive) against BlogPost content without SubstackPostUrl → High
2. Fuzzy match (Levenshtein < 20% of title length) + pubDate within 48h of CreatedAt → Medium
3. No match → None

Levenshtein distance: private static method, standard DP approach.

### SubstackPublicationPoller
File: `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/SubstackPublicationPoller.cs`

- `BackgroundService` with `PeriodicTimer` (interval from SubstackOptions.PollingIntervalMinutes)
- Creates DI scope per tick
- Stores `_lastETag` and `_lastModified` in memory
- Deduplicates via DB check on `SubstackDetection.RssGuid`
- Creates SubstackDetection for all entries (ContentId=null if unmatched)
- Creates notification + updates Content.SubstackPostUrl for matches above threshold
- Fires `SubstackPublicationDetectedEvent` domain event
- Error handling: try-catch per tick, log and continue

---

## Files
| File | Action |
|------|--------|
| `Application/Common/Models/SubstackRssEntry.cs` | Create |
| `Application/Common/Interfaces/ISubstackContentMatcher.cs` | Create |
| `Infrastructure/Services/AnalyticsServices/SubstackContentMatcher.cs` | Create |
| `Infrastructure/BackgroundJobs/SubstackPublicationPoller.cs` | Create |
| `Application/Common/Interfaces/ISubstackService.cs` | Modify |
| `Infrastructure/Services/AnalyticsServices/SubstackService.cs` | Modify |
| `Application/Common/Models/SubstackOptions.cs` | Modify |
| `Infrastructure/DependencyInjection.cs` | Modify |
