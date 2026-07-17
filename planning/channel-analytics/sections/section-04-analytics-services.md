# section-04-analytics-services

## Goal

Build one **keyed `IChannelAnalyticsService` per platform** (YouTube, Instagram, TikTok), each a thin facade over an injectable HTTP/SDK client. Each service's `PollAsync` pulls the current **cumulative** account snapshot plus recent-video snapshots, returning a `ChannelPollResult` whose metric bags are ready to be persisted as `ChannelMetricSnapshot` rows. Also build the **live** YouTube Analytics v2 deep-path client used later by the read API (not snapshotted).

Everything is developed and fully tested against **canned JSON with injected clients** — no real token needed to go green. Mirrors the existing GA4 facade/thin-client split so clients are trivially mockable.

## Dependencies

- **section-02-domain-persistence** (required): provides `Platform.Instagram` / `Platform.TikTok` enum members, `PlatformCredential.Purpose` (`CredentialPurpose`), `ChannelMetricSnapshot`, `SnapshotScope`. This section consumes `PlatformCredential` as `PollAsync` input and produces metric bags the poller writes into `ChannelMetricSnapshot`. It does **not** itself write to the DB.
- **Blocks:** section-05 (poller resolves these keyed services) and section-06 (read API's `GetYouTubeDeepAnalytics` resolves the YouTube service for the live path).

Register the three keyed services in `src/PBA.Infrastructure/DependencyInjection.cs` as part of this section so tests can resolve them.

## Background: the pattern to mirror

- **Facade** `GoogleAnalyticsService` (`src/PBA.Infrastructure/Services/Analytics/GoogleAnalyticsService.cs`): sealed class, primary-constructor DI, `IOptions<T>`, `ILogger<T>`, orchestration + DTO mapping, `try/catch` -> `Result<T>.Fail($"... {ex.Message}")` with `logger.LogError`. Returns `Result<T>` (from `PBA.Domain.Common`).
- **Thin client** `IGa4Client` (`src/PBA.Application/Common/Interfaces/IGa4Client.cs`): a one-method seam over the SDK so report mapping is unit-testable. Implementation `Ga4Client` in `src/PBA.Infrastructure/Services/Analytics/`.
- **Interfaces live in** `src/PBA.Application/Common/Interfaces`. **Implementations live in** `src/PBA.Infrastructure/Services/Analytics`.
- **Result contract:** `Result<T>.Success(value)`, `Result<T>.Fail(message)` (namespace `PBA.Domain.Common`).

Follow this split per platform: a `{Platform}AnalyticsService` facade over a `{Platform}...Client` thin seam.

## Files to create

Interfaces + value objects (`src/PBA.Application/Common/Interfaces/`):
- `IChannelAnalyticsService.cs`
- `IYouTubeApiClient.cs`
- `IInstagramGraphClient.cs`
- `ITikTokDisplayClient.cs`
- `ChannelPollResult.cs` (records grouped)

Facades + clients (`src/PBA.Infrastructure/Services/Analytics/`):
- `YouTubeAnalyticsService.cs`, `YouTubeApiClient.cs`
- `InstagramAnalyticsService.cs`, `InstagramGraphClient.cs`
- `TikTokAnalyticsService.cs`, `TikTokDisplayClient.cs`

DI (`src/PBA.Infrastructure/DependencyInjection.cs`): three `AddKeyedScoped<IChannelAnalyticsService, ...>(Platform.X)` plus HTTP/SDK client registrations.

NuGet: add `Google.Apis.YouTube.v3` and `Google.Apis.YouTubeAnalytics.v2` references to `src/PBA.Infrastructure`.

Tests (`tests/PBA.Infrastructure.Tests/Services/Analytics/`) — the **live, non-orphaned** test project is `tests/PBA.Infrastructure.Tests` (ignore `tests/PersonalBrandAssistant.*.Tests/`). Store canned JSON fixtures under a `Fixtures/` subfolder or inline const strings.

## Common contracts

```csharp
// src/PBA.Application/Common/Interfaces/IChannelAnalyticsService.cs
public interface IChannelAnalyticsService
{
    Platform Platform { get; }

    // Pull the current cumulative account snapshot + recent-video snapshots for today's poll.
    Task<Result<ChannelPollResult>> PollAsync(
        PlatformCredential credential, int recentVideoCount, CancellationToken ct);
}

// value objects (records)
public record ChannelPollResult(AccountMetrics Account, IReadOnlyList<VideoMetrics> RecentVideos);
public record AccountMetrics(IReadOnlyDictionary<string, long> Metrics, bool Provisional);
public record VideoMetrics(string VideoId, string? Title, IReadOnlyDictionary<string, long> Metrics, bool Provisional);
```

`Provisional` is always `false` for these platforms (cumulative counts are final at capture); it exists on the record for the poller's sentinel logic — set `false`. (It is not persisted; the `ChannelMetricSnapshot` entity has no provisional field.)

**Invariant to enforce in mapping:** every value in a `Metrics` bag is an **integer count** (or whole seconds). No fractions ever enter a bag — engagement rates and ratios are computed at read time (section-06), never here.

Keyed DI registration (assert via test `AnalyticsServices_ResolveByPlatformKey_ViaKeyedDI`):
```csharp
services.AddKeyedScoped<IChannelAnalyticsService, YouTubeAnalyticsService>(Platform.YouTube);
services.AddKeyedScoped<IChannelAnalyticsService, InstagramAnalyticsService>(Platform.Instagram);
services.AddKeyedScoped<IChannelAnalyticsService, TikTokAnalyticsService>(Platform.TikTok);
```

The `credential` argument carries the decrypted-at-use OAuth access token. The poller (section-05) ensures the token is fresh before calling `PollAsync`; here you consume `credential`'s access token to authorize the client.

## YouTube (`YouTubeAnalyticsService` + `YouTubeApiClient`)

Thin client `IYouTubeApiClient` wraps **Data API v3** (`Google.Apis.YouTube.v3`), authorized with the stored OAuth access token; public reads may use the configured API key:

1. `channels.list?part=statistics,contentDetails` — gets account statistics (`subscriberCount`, `viewCount`, `videoCount`) and the **uploads playlist id** (`contentDetails.relatedPlaylists.uploads`).
2. `playlistItems.list` on the uploads playlist (1 unit, newest-first) to discover recent video IDs. **Do NOT use `search.list`** (100 units, unreliable ordering).
3. `videos.list?part=statistics,snippet` batched **<= 50 ids per call** to fetch per-video statistics + titles.

No ETag / `If-None-Match` (negligible value for a once-daily poll — do not implement it despite the TDD stub name mentioning ETag; assert instead that the batch call path is used).

**Cumulative account keys:** `subscribers`, `views`, `videos`.
**Per-video keys:** `views`, `likes`, `comments`.
Cap recent videos at `recentVideoCount` (N).

Thin client seams roughly:
```csharp
public interface IYouTubeApiClient
{
    Task<ChannelStatsResult> GetChannelAsync(string accessToken, CancellationToken ct);      // stats + uploads playlist id
    Task<IReadOnlyList<string>> GetRecentVideoIdsAsync(string uploadsPlaylistId, int max, string accessToken, CancellationToken ct);
    Task<IReadOnlyList<VideoStat>> GetVideosAsync(IReadOnlyList<string> ids, string accessToken, CancellationToken ct); // batches <=50 internally
}
```
Keep SDK types behind these seams so the facade maps plain records and tests feed canned responses without the SDK.

### YouTube deep path (live, NOT snapshotted)

Separate call over **Analytics API v2** (`Google.Apis.YouTubeAnalytics.v2`) `reports.query`: `ids=channel==MINE`, `dimensions=day`, metrics `views`, `estimatedMinutesWatched`, `averageViewDuration`, `subscribersGained`, `subscribersLost`, `likes`, `comments`, `shares`; plus traffic-source / geography / demographics dimension variants, for a user-selected range. Add a method to the YouTube client (e.g. `RunReportAsync(YouTubeReportRequest, accessToken, ct)`) returning row data. Consumed live by `GetYouTubeDeepAnalytics` (section-06); **not** written to `ChannelMetricSnapshot`. `subscribersGained/Lost` on other tabs is derived from cumulative `subscribers` deltas.

Build the client method + a mappable row shape here; the query/DTO that calls it belongs to section-06. Cover it with `YouTubeDeepAnalytics_Query_MapsReportsRows_ToLabeledSeries` at the client-mapping level.

## Instagram (`InstagramAnalyticsService` + `InstagramGraphClient`)

HTTP client `IInstagramGraphClient` against `graph.instagram.com` (Instagram-Login model), injected `HttpClient`. Endpoints: account insights `/{ig-user-id}/insights?metric_type=total_value&period=day`, plus `follower_count`, plus per-media insights.

**Canonical account keys:** `followers`, `reach`, `views`, `accounts_engaged`, `total_interactions`, `likes`, `comments`, `saves`, `shares`, `profile_links_taps`.
**Per-media keys:** `reach`, `views`, `likes`, `comments`, `saves`, `shares`.

**Deprecation tolerance (critical):** the requested metric list is **data-driven** — a constant array. The client requests those metrics and **silently drops any the API rejects as unknown/deprecated** (log at `Debug`), never failing the whole poll. `impressions` is intentionally absent (removed by Meta; `views` replaces it). Do not hardcode positional parsing — map by returned metric name so a missing/dropped metric just doesn't appear in the bag.

## TikTok (`TikTokAnalyticsService` + `TikTokDisplayClient`)

HTTP client `ITikTokDisplayClient` against `open.tiktokapis.com` v2, injected `HttpClient`:
- `/v2/user/info/` — fields incl. `follower_count`, `following_count`, `likes_count`, `video_count`.
- `/v2/video/list/` — fields incl. `view_count`, `like_count`, `comment_count`, `share_count`, `title`, `id`. This endpoint caps `max_count <= 20/page`, so the client **loops on the returned `cursor` / `has_more`** to accumulate up to N videos.

**Canonical account keys:** `followers`, `following`, `likes`, `videos`.
**Per-video keys:** `views`, `likes`, `comments`, `shares`.

Document the ceiling in code comments: no reach/demographics/retention available; all TikTok trends are snapshot deltas only.

## Error handling (all three services)

Every facade: return `Result<ChannelPollResult>`; catch API exceptions -> `Result<ChannelPollResult>.Fail(...)` with a typed reason (never throw out of `PollAsync`); log via `ILogger`. Empty data -> `Result.Success` with empty metric bags (not a failure). Constructor takes the injected client(s) so tests feed canned responses. Match the `GoogleAnalyticsService` try/catch/log shape exactly.

## Tests (write FIRST — TDD Step 4)

Backend xUnit, naming `Method_Scenario_ExpectedResult`. Mock only the external HTTP/SDK client (feed canned JSON); no real network. Stubs:

YouTube:
```
# YouTubeAnalyticsService_PollAsync_MapsChannelStatistics_ToCumulativeAccountKeys
# YouTubeAnalyticsService_PollAsync_DiscoversRecentVideos_ViaUploadsPlaylist_NotSearch
# YouTubeAnalyticsService_PollAsync_BatchesVideosList_MaxFiftyIds
# YouTubeAnalyticsService_PollAsync_CapsRecentVideos_AtN
# YouTubeDeepAnalytics_Query_MapsReportsRows_ToLabeledSeries   (live path, mocked client)
```
Instagram:
```
# InstagramAnalyticsService_PollAsync_MapsAccountInsights_ToCanonicalKeys
# InstagramAnalyticsService_PollAsync_DropsUnknownDeprecatedMetric_WithoutFailing   (deprecation tolerance)
# InstagramAnalyticsService_PollAsync_MapsPerMediaInsights
```
TikTok:
```
# TikTokAnalyticsService_PollAsync_MapsUserInfoStats_ToAccountKeys
# TikTokAnalyticsService_PollAsync_PaginatesVideoList_ToReachN   (20/page cursor loop)
# TikTokAnalyticsService_PollAsync_MapsPerVideoCounts
```
Common (parametrize across all three):
```
# {Service}_PollAsync_ApiError_ReturnsResultFail_NotThrow
# {Service}_PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics
```
Plus DI resolution:
```
# AnalyticsServices_ResolveByPlatformKey_ViaKeyedDI
```

Key assertions to encode WHY:
- **Cumulative keys are correct AND integer-only** — assert exact key names and that no fractional value appears in any bag.
- **Uploads-playlist discovery, not search** — assert the stub's `search.list` seam is never called and `playlistItems.list` is.
- **<=50 batching** — feed >50 discovered ids, assert `videos.list` is called with chunked id lists each <=50.
- **N cap** — feed more videos than N, assert `RecentVideos.Count == recentVideoCount`.
- **IG deprecation tolerance** — canned response omits/rejects one requested metric; assert the poll still succeeds and the bag simply lacks that key.
- **TikTok cursor loop** — canned multi-page response (`has_more=true` then `false`); assert accumulation reaches N across pages.

## Definition of done

- All Step 4 tests green under `dotnet test`.
- Three keyed `IChannelAnalyticsService` implementations resolvable by `Platform` key.
- No DB writes and no live network in any test.
- Metric bags contain integer-only values with the canonical key names above.
- YouTube deep-path client method exists and maps canned Analytics v2 rows (query/DTO wiring deferred to section-06).

---

## Implementation Outcome (as built)

Implemented as planned. Build clean; 35 analytics tests green (19 facade/mapper/DI + 4 review-fix + existing GA4). Full non-Docker Infrastructure suite 433 green; full solution builds.

### Design interpretation (faithful to plan intent)
The thin-client seams (`IYouTubeApiClient`, `IInstagramGraphClient`, `ITikTokDisplayClient`) map ~1:1 to individual API calls; the **orchestration** (uploads-playlist paging, ≤50 videos.list batching, N-cap, IG canonical metric lists, TikTok cursor loop) lives in the **facades**, so the plan's key logic is unit-testable by mocking the seams. The client implementations (`YouTubeApiClient` = Google SDK; `InstagramGraphClient`/`TikTokDisplayClient` = HttpClient) are **untested seams by design**.

### Files created
- Interfaces/value objects: `src/PBA.Application/Common/Interfaces/{ChannelPollResult, IChannelAnalyticsService, IYouTubeApiClient, IInstagramGraphClient, ITikTokDisplayClient}.cs`
- Deep-path: `src/PBA.Application/Features/Analytics/Dtos/YouTubeDeepAnalyticsSeries.cs`, `src/PBA.Application/Features/Analytics/YouTubeDeepAnalyticsMapper.cs`
- Facades + clients: `src/PBA.Infrastructure/Services/Analytics/{YouTube,Instagram,TikTok}AnalyticsService.cs` + `{YouTubeApiClient, InstagramGraphClient, TikTokDisplayClient}.cs`
- Tests: `tests/PBA.Infrastructure.Tests/Services/Analytics/{YouTube,Instagram,TikTok}AnalyticsServiceTests.cs`, `YouTubeDeepAnalyticsMapperTests.cs`, `InstagramGraphClientTests.cs`, `AnalyticsServicesDiTests.cs`

### Files modified
- `src/PBA.Infrastructure/PBA.Infrastructure.csproj` — added `Google.Apis.YouTube.v3` (1.75.0.4207) + `Google.Apis.YouTubeAnalytics.v2` (1.74.0.3106).
- `src/PBA.Infrastructure/DependencyInjection.cs` — three keyed `IChannelAnalyticsService` + thin-client registrations (IG/TikTok as typed HttpClients with BaseAddress).

### Notes / deviations
- **SDK name collision:** my facade `YouTubeAnalyticsService` collides with the SDK's `Google.Apis.YouTubeAnalytics.v2.YouTubeAnalyticsService`; resolved with a namespace alias in `YouTubeApiClient`.
- **Facades decrypt** `credential.EncryptedAccessToken` via injected `ITokenEncryptor` (the poller ensures freshness; the facade decrypts at use).
- **Deep-path mapper generalized** (review fix #2) to label by the report's DIMENSION column, not a hardcoded `day`, so section-06 can feed it traffic-source/geography/demographics variants.

### Review fixes applied (see `implementation/code_review/section-04-interview.md`)
Added YouTube multi-page paging test, generalized + tested the deep mapper for non-day dimensions, real IG client-level tests (deprecation-drop + media parse), max-page guards on both paging loops, exact-key-count assertion, and moved the IG token to the `Authorization` header.

### Before prod — untested-seam smoke test (real credentials)
Confirm: IG media-insights response shape (`values[]` vs `total_value`) and the graph.instagram.com version-less path; TikTok error-in-200-body handling; YouTube SDK field mappings (subscriber/view/like counts, uploads playlist id). No DB writes and no live network occur in any test.
