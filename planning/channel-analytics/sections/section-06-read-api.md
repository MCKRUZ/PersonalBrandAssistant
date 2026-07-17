# section-06-read-api — Read API (Queries + Endpoints)

## Goal

Expose the channel-analytics data (persisted by the daily poller) to the frontend through three MediatR queries and one endpoint group. Two read paths, deliberately separated:

1. **Snapshot-backed reads** (the default) — the current dashboard and all trend series are computed from the `ChannelMetricSnapshot` history already in the DB. No live external API call on page load: fast, resilient, rate-limit-free.
2. **One live read** — the YouTube tab's *deep* analytics (watch time, average view duration, traffic sources, demographics) is queried live from the YouTube Analytics API for the selected range, because that API keeps its own history and we do not snapshot it. Wrapped in `Result<T>` so the tab degrades gracefully if the live call fails.

This section builds:
- MediatR queries `GetChannelAnalytics`, `GetAnalyticsOverview`, `GetYouTubeDeepAnalytics`
- The read DTOs + `ConnectionStatus` enum
- `ChannelAnalyticsEndpoints` group + `Program.cs` wiring

It does **not** build the poller, the analytics services, or the domain/persistence — those are prior sections.

## Dependencies (already implemented — reference only)

**From section-02-domain-persistence:**
- `ChannelMetricSnapshot` entity (`PBA.Domain.Entities`) — init-only POCO: `Guid Id`, `Platform Platform`, `DateOnly SnapshotDate` (host-local capture date), `SnapshotScope Scope` (`Account | Video`), `string VideoId` (non-nullable; sentinel `""` for Account scope), `string? VideoTitle`, `IReadOnlyDictionary<string,long> Metrics` (jsonb), `DateTimeOffset CapturedAt`. **Every value in `Metrics` is an integer cumulative count** (or whole seconds). Ratios are never stored — computed here at read time.
- `SnapshotScope` enum (`{ Account = 0, Video = 1 }`).
- `CredentialPurpose` enum (`{ Publishing = 0, Analytics = 1 }`) and `PlatformCredential.Purpose`.
- `Platform` enum members `YouTube (=5)`, `Instagram`, `TikTok`.
- `IAppDbContext` exposes `DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots` **and** `DbSet<PlatformCredential> PlatformCredentials`. Snapshot-backed handlers depend only on `IAppDbContext`.

**From section-04-analytics-services:**
- Keyed `IChannelAnalyticsService` per platform. The live `GetYouTubeDeepAnalytics` handler resolves the keyed YouTube service plus its `PlatformCredential (Purpose=Analytics)`.
- The YouTube service exposes the **deep day-analytics path** over YouTube Analytics API v2 `reports.query`, returning labeled series/breakdowns for a selected range. Separate from `PollAsync`. Confirm the exact method name/DTO the section-04 YouTube service exposes and map it into `YouTubeDeepAnalyticsDto` here; do not duplicate the HTTP logic in the query handler.

## Existing patterns this section mirrors

**Query convention** — `src/PBA.Application/Features/Analytics/Queries/GetWebsiteAnalytics.cs`:
```csharp
public static class GetWebsiteAnalytics
{
    public record Query(DateTimeOffset From, DateTimeOffset To) : IRequest<Result<WebsiteAnalyticsDto>>;

    public sealed class Handler(IGoogleAnalyticsService ga) : IRequestHandler<Query, Result<WebsiteAnalyticsDto>>
    {
        public async Task<Result<WebsiteAnalyticsDto>> Handle(Query request, CancellationToken ct) { ... }
    }
}
```
Static wrapper class holding a `Query` record (`: IRequest<Result<TDto>>`) and a nested `Handler` with primary-constructor DI. MediatR auto-registers by assembly scan.

**Endpoint convention** — `src/PBA.Api/Endpoints/AnalyticsEndpoints.cs`:
```csharp
public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/analytics").WithTags("Analytics");

    group.MapGet("/website", async (string? period, DateTimeOffset? from, DateTimeOffset? to,
        ISender sender, CancellationToken ct) =>
    {
        if (!TryResolveRange(period, from, to, out var range))
            return Results.BadRequest("Invalid period ...");
        var result = await sender.Send(new GetWebsiteAnalytics.Query(range.From, range.To), ct);
        return result.ToApiResult();
    });
    ...
}
```
`app.MapGroup(...)`, `ISender.Send`, `result.ToApiResult()`. No endpoint auth (repo convention for the analytics surface).

**Period resolution** — there is **no backend `AnalyticsPeriod` enum today**. The Website endpoint resolves a `string? period` (`"7d" | "30d" | "90d"`) into a date range via a private `TryResolveRange` helper (default 30d). Reuse the exact same string-period vocabulary. Resolve the period string to a `(DateOnly From, DateOnly To)` window (snapshots are keyed by `DateOnly SnapshotDate`) and pass it into the queries. **Do not introduce an `AnalyticsPeriod` enum unless it genuinely simplifies the three query signatures** — a resolved date window threaded into each `Query` record matches the existing pattern and is the lighter choice.

**`Program.cs` wiring** — `MapAnalyticsEndpoints()` is called at `src/PBA.Api/Program.cs:72`. Add `app.MapChannelAnalyticsEndpoints();` immediately beside it.

## Tests FIRST

Handler tests seed `ChannelMetricSnapshot` + `PlatformCredential` rows into an in-memory / real DB via `IAppDbContext`; endpoint tests use `WebApplicationFactory<Program>` with the poller stripped. Mock only external HTTP clients + `TimeProvider`. Naming `MethodName_Scenario_ExpectedResult`.

**Handlers over seeded snapshots (in-memory DB):**
```
# GetChannelAnalytics_BuildsKpis_LatestCumulativeWithDeltaPct
# GetChannelAnalytics_TrendSeries_AreDeltasBetweenConsecutiveSnapshots
# GetChannelAnalytics_EngagementRate_UsesReachWhenPresent_ElseFollowers
# GetChannelAnalytics_ResolvesConnectionStatus_FromAnalyticsCredential
# GetChannelAnalytics_NotConnected_ReturnsEmptyWithNotConnectedStatus
# GetAnalyticsOverview_AggregatesTotalAudience_AcrossConnectedChannels
# GetAnalyticsOverview_BuildsPerPlatformFollowerSparklines
# GetYouTubeDeepAnalytics_LiveCallFails_ReturnsResultFail_TabDegrades
```

**Endpoints (`WebApplicationFactory<Program>`, poller stripped):**
```
# ChannelAnalyticsEndpoints_Overview_ReturnsOk
# ChannelAnalyticsEndpoints_Channel_InvalidPlatform_ReturnsBadRequest
# ChannelAnalyticsEndpoints_Channel_ParsesPeriod
# ChannelAnalyticsEndpoints_MapsResultFailure_ViaToApiResult
# TestFactory_DoesNotStartChannelMetricPollingService   (M4 isolation)
```

### Test isolation note (M4)

The endpoint `WebApplicationFactory` **must strip `ChannelMetricPollingService`** so the poller does not start and hit external APIs during integration tests. Per-platform gates default `false`, but the hosted service must not be registered in the test host regardless. Follow whatever the existing factory does for `DigestService`. `TestFactory_DoesNotStartChannelMetricPollingService` asserts this.

### Key assertion semantics (encode WHY, not just WHAT)

- `TrendSeries_AreDeltasBetweenConsecutiveSnapshots`: snapshots store **cumulative** counts; a trend point for day *N* is `metrics[N] - metrics[N-1]`. Seed cumulative `subscribers` `[100, 105, 111]` over three consecutive days -> the trend series must be `[+5, +6]` (the first day seeds the baseline, not a point). This is the one delta mechanism used across **all three platforms**.
- `EngagementRate_UsesReachWhenPresent_ElseFollowers`: `Rate = interactions / reach` where a `reach` metric exists (Instagram), else `interactions / followers` (YouTube, TikTok), where `interactions = likes + comments + shares (+ saves)`. Assert both branches. `Rate` is `null` when the denominator is zero/absent.
- `BuildsKpis_LatestCumulativeWithDeltaPct`: `KpiCard.Value` = latest cumulative; `DeltaPct` = percent change vs the prior snapshot in-range (`null` when no prior).
- `ResolvesConnectionStatus_FromAnalyticsCredential` / `NotConnected_ReturnsEmptyWithNotConnectedStatus`: status comes from the `PlatformCredential` with `Purpose = Analytics`: active -> `Connected`; exists-but-inactive (poller deactivated on `Revoked`) -> `ReconnectRequired`; none -> `NotConnected`, and the DTO returns empty collections (not an error).
- `GetYouTubeDeepAnalytics_LiveCallFails_ReturnsResultFail_TabDegrades`: when the live YouTube Analytics client throws/returns failure, the handler returns `Result.Fail` (not an exception).

## Implementation

### 1. DTOs — `src/PBA.Application/Features/ChannelAnalytics/Dtos/ChannelAnalyticsDtos.cs` (new)

Immutable records (`IReadOnlyList`/`IReadOnlyDictionary` on public surfaces):

```csharp
public enum ConnectionStatus { NotConnected = 0, Connected = 1, ReconnectRequired = 2 }

public record MetricPoint(DateOnly Date, long Value);
public record TrendSeries(string Metric, IReadOnlyList<MetricPoint> Points);   // deltas derived from cumulative snapshots
public record KpiCard(string Key, string Label, long Value, double? DeltaPct, double? Rate);  // Rate = engagement rate fraction, null when N/A
public record RecentPost(string VideoId, string? Title, IReadOnlyDictionary<string,long> Metrics);

public record ChannelAnalyticsDto(
    Platform Platform,
    ConnectionStatus Status,               // Connected | ReconnectRequired | NotConnected
    DateOnly? AsOf,
    IReadOnlyList<KpiCard> Kpis,
    IReadOnlyList<TrendSeries> Trends,
    IReadOnlyList<RecentPost> RecentPosts);

public record OverviewChannel(Platform Platform, ConnectionStatus Status, long? Followers, IReadOnlyList<MetricPoint> FollowerSparkline);
public record OverviewDto(
    long TotalAudience,                    // sum of followers/subscribers across connected channels (APPROXIMATE — YouTube rounds)
    IReadOnlyList<KpiCard> CombinedKpis,
    IReadOnlyList<OverviewChannel> Channels);
```

Plus `YouTubeDeepAnalyticsDto` for the live path — labeled series + breakdowns (day series for `views`, `estimatedMinutesWatched`, `averageViewDuration`, `subscribersGained/Lost`, `likes`, `comments`, `shares`; plus traffic-source / geography / demographics breakdowns). Shape it to whatever the section-04 YouTube deep path returns; keep it a flat set of labeled series so the frontend can chart them directly.

`namespace PBA.Application.Features.ChannelAnalytics.Dtos;`

### 2. Query — `GetChannelAnalytics.cs` (new)

```
GetChannelAnalytics.Query(Platform Platform, <resolved date window>) -> Result<ChannelAnalyticsDto>
```
Handler (primary-ctor DI on `IAppDbContext`):
1. Resolve `ConnectionStatus` from the `PlatformCredential` where `Platform == request.Platform && Purpose == Analytics` (active -> `Connected`; inactive -> `ReconnectRequired`; none -> `NotConnected` -> return empty DTO with that status, `AsOf = null`).
2. Read Account-scope snapshots for the platform over the window, ordered by `SnapshotDate` (`Scope == Account`, `VideoId == ""`). Range-fetch of ~90 rows; the `(Platform, SnapshotDate)` btree index covers it.
3. **KPIs:** for each canonical account metric key, `Value` = latest cumulative, `DeltaPct` = pct change vs the prior in-range snapshot. Add the engagement-rate KPI computing `Rate` per the reach-else-followers rule.
4. **Trends:** for each tracked metric, produce `TrendSeries` of consecutive deltas (`m[N] - m[N-1]`), one `MetricPoint` per day starting at the second in-range snapshot. Includes a synthetic `subscribersGained/Lost`-style series derived from `subscribers`/`followers` deltas.
5. **RecentPosts:** latest Video-scope snapshots for the platform (`Scope == Video`), mapped to `RecentPost` with their cumulative metric bags.
6. `AsOf` = latest `SnapshotDate` present.

Depends **only** on `IAppDbContext` — no live API call.

### 3. Query — `GetAnalyticsOverview.cs` (new)

```
GetAnalyticsOverview.Query(<resolved date window>) -> Result<OverviewDto>
```
Fans out across `Platform.YouTube | Instagram | TikTok`: reuse the same Account-snapshot read + status resolution per platform, then aggregate:
- `TotalAudience` = sum of latest followers/subscribers across **connected** channels only.
- `CombinedKpis` = cross-platform combined KPI cards.
- `Channels` = per-platform `OverviewChannel` with latest `Followers` and a `FollowerSparkline` (delta series over the window).

Snapshot-backed only (`IAppDbContext`). Share the per-platform read logic with `GetChannelAnalytics` via a small private helper (do not over-abstract — YAGNI).

### 4. Query — `GetYouTubeDeepAnalytics.cs` (new) — the one LIVE path

```
GetYouTubeDeepAnalytics.Query(<resolved date window>) -> Result<YouTubeDeepAnalyticsDto>
```
Handler resolves the **keyed** `IChannelAnalyticsService` for `Platform.YouTube` (`[FromKeyedServices(Platform.YouTube)]` or `IServiceProvider.GetRequiredKeyedService`) plus the YouTube `PlatformCredential (Purpose=Analytics)`. Calls the deep day-analytics path (YouTube Analytics v2 `reports.query`) for the window, maps rows -> `YouTubeDeepAnalyticsDto`. **Wrap the whole thing in `Result`** — on any client failure return `Result.Fail(...)` (do not throw). If the YouTube analytics credential is missing/inactive, return `Result.Fail` with a clear reason.

### 5. Endpoints — `src/PBA.Api/Endpoints/ChannelAnalyticsEndpoints.cs` (new)

```csharp
public static class ChannelAnalyticsEndpoints
{
    public static void MapChannelAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/overview", async (string? period, ISender sender, CancellationToken ct) => { ... });
        group.MapGet("/channel/{platform}", async (string platform, string? period, ISender sender, CancellationToken ct) => { ... });
        group.MapGet("/youtube/deep", async (string? period, ISender sender, CancellationToken ct) => { ... });  // live
    }
}
```
Route contract:
```
GET  /api/analytics/overview?period=            -> GetAnalyticsOverview.Query      -> OverviewDto
GET  /api/analytics/channel/{platform}?period=  -> GetChannelAnalytics.Query       -> ChannelAnalyticsDto
GET  /api/analytics/youtube/deep?period=        -> GetYouTubeDeepAnalytics.Query   -> YouTubeDeepAnalyticsDto (live)
```
- `{platform}` parsed to the `Platform` enum, restricted to `YouTube | Instagram | TikTok`. Anything else (unknown, or a non-analytics platform like `LinkedIn`) -> `Results.BadRequest(...)`.
- `period` resolved with the **same** `7d|30d|90d` vocabulary as the Website endpoint (default 30d). Factor resolution to match `AnalyticsEndpoints.TryResolveRange` semantics; do not modify the Website endpoint.
- All results via `result.ToApiResult()`.
- Reuses the **same `/api/analytics` group prefix** — the existing `/api/analytics/website` + `/health` are untouched.

### 6. `Program.cs` wiring

Add beside `app.MapAnalyticsEndpoints();` (currently `src/PBA.Api/Program.cs:72`):
```csharp
app.MapChannelAnalyticsEndpoints();
```

## Notes / constraints

- **No new DI registrations for the queries** — MediatR auto-registers handlers by assembly scan. The keyed `IChannelAnalyticsService` is registered in section-04.
- **No endpoint auth** — matches the existing analytics surface convention.
- **Immutability** — DTOs are records with `IReadOnlyList`/`IReadOnlyDictionary`.
- **`TotalAudience` is approximate** — YouTube `subscriberCount` is rounded to 3 significant figures. The frontend (section-07) labels the aggregate approximate; no special handling here beyond the DTO comment.
- **File size** — keep each query in its own file (one class per file). Shared per-platform snapshot-read helper -> a small internal static helper under `Features/ChannelAnalytics/`.

## Definition of done

- All Step 6 handler + endpoint tests green.
- `TestFactory_DoesNotStartChannelMetricPollingService` green (poller stripped from the test host).
- `dotnet test` green overall (no regression in existing Website analytics tests).
- `GET /api/analytics/overview`, `/api/analytics/channel/{platform}`, `/api/analytics/youtube/deep` reachable and mapping `Result` -> HTTP via `ToApiResult()`; the existing `/api/analytics/website` + `/health` unchanged.
