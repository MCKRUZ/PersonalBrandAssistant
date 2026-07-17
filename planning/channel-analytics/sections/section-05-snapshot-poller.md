# section-05-snapshot-poller

## Goal

Build the daily background poller that captures cumulative channel-metric snapshots for each connected analytics credential. This is the write-side heartbeat of the analytics feature: once per day it walks every active `Analytics`-purpose `PlatformCredential`, ensures the token is fresh (refreshing / deactivating as needed), calls the platform's `IChannelAnalyticsService.PollAsync`, and persists `ChannelMetricSnapshot` rows idempotently. Trends are later derived at read time (section 06) from the deltas between consecutive daily snapshots; this section only produces the raw cumulative rows.

Deliverables:
- `ChannelMetricPollingService : BackgroundService`
- `ChannelAnalyticsOptions` (config binding)
- DI registration (`AddHostedService` + `Configure`)
- `appsettings.json` `ChannelAnalytics` section (defaults, per-platform gates false)

## Dependencies (already built — reference only)

This section is the last in the build order and consumes types from sections 02, 03, 04.

- **section-02-domain-persistence** provides:
  - `ChannelMetricSnapshot` entity (`Guid Id`, `Platform Platform`, `DateOnly SnapshotDate`, `SnapshotScope Scope`, `string VideoId` non-nullable with `""` sentinel for Account scope, `string? VideoTitle`, `IReadOnlyDictionary<string,long> Metrics` stored jsonb, `DateTimeOffset CapturedAt`).
  - `SnapshotScope { Account = 0, Video = 1 }`.
  - `CredentialPurpose { Publishing = 0, Analytics = 1 }` and `PlatformCredential.Purpose`.
  - `Platform` enum with `YouTube`, `Instagram`, `TikTok`.
  - `ApplicationDbContext.ChannelMetricSnapshots` DbSet with a unique index on `(Platform, SnapshotDate, Scope, VideoId)` — the `ON CONFLICT` target for idempotent upserts.
- **section-03-oauth-providers** provides:
  - Keyed `IOAuthProvider` per `Platform` with `bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now)` and `Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct)`.
  - `RefreshFailureReason { Revoked, Transient }` carried on the failure `Result`.
  - `OAuthService.RefreshTokenAsync(credential)` (coordinator) which decrypts the refresh token and delegates to the keyed provider, persisting rotated tokens.
- **section-04-analytics-services** provides:
  - Keyed `IChannelAnalyticsService` per `Platform`:
    ```
    interface IChannelAnalyticsService {
        Platform Platform { get; }
        Task<Result<ChannelPollResult>> PollAsync(PlatformCredential credential, int recentVideoCount, CancellationToken ct);
    }
    record ChannelPollResult(AccountMetrics Account, IReadOnlyList<VideoMetrics> RecentVideos);
    record AccountMetrics(IReadOnlyDictionary<string,long> Metrics, bool Provisional);
    record VideoMetrics(string VideoId, string? Title, IReadOnlyDictionary<string,long> Metrics, bool Provisional);
    ```

If any of these types are missing when you start, that dependency section is not done — stop and confirm.

## Existing pattern to mirror

`src/PBA.Infrastructure/Services/Radar/DigestService.cs` is the template for a daily `BackgroundService`. It:
- Takes `IServiceScopeFactory`, options, and `ILogger` in the primary constructor.
- Runs `while (!stoppingToken.IsCancellationRequested)`, checks `TimeOnly.FromDateTime(now.DateTime) >= _runAt` each hour, then `await Task.Delay(TimeSpan.FromHours(1), stoppingToken)`.
- Catches `Exception ex when (ex is not OperationCanceledException)` around the run.
- Opens a fresh DI scope **per run** (`using var scope = scopeFactory.CreateScope()`), resolves `ApplicationDbContext` from the scope (never inject scoped services into a `BackgroundService` constructor).
- Guards idempotency with a DB `AnyAsync(...)` existence check before doing work.
- Registered via `AddHostedService<DigestService>()` in `src/PBA.Infrastructure/DependencyInjection.cs`.

**Deliberate deviation from `DigestService`:** use `IOptionsMonitor<ChannelAnalyticsOptions>` (not `IOptions`) so `RunAtLocalTime` / `RecentVideoCount` / per-platform gates hot-reload. Read `.CurrentValue` at the top of each run. Also note `DigestService` uses `DateOnly.FromDateTime(now.UtcDateTime)` — **the poller must use host-local date instead** (see below), because `SnapshotDate` must line up with the local `RunAtLocalTime` clock and the read-side trend math.

## Files to create / modify

- **Create** `src/PBA.Infrastructure/Services/Analytics/ChannelMetricPollingService.cs`
- **Create** `src/PBA.Infrastructure/Configuration/ChannelAnalyticsOptions.cs`
- **Modify** `src/PBA.Infrastructure/DependencyInjection.cs` — add `services.Configure<ChannelAnalyticsOptions>(config.GetSection(ChannelAnalyticsOptions.SectionName))` and `services.AddHostedService<ChannelMetricPollingService>()`.
- **Modify** `src/PBA.Api/appsettings.json` — add the `ChannelAnalytics` section.
- **Create** tests at `tests/PBA.Infrastructure.Tests/Services/Analytics/ChannelMetricPollingServiceTests.cs` (mirror `tests/PBA.Infrastructure.Tests/Services/Radar/DigestServiceTests.cs`).

## `ChannelAnalyticsOptions`

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class ChannelAnalyticsOptions
{
    public const string SectionName = "ChannelAnalytics";

    public string RunAtLocalTime { get; init; } = "05:00";
    public int RecentVideoCount { get; init; } = 50;
    public bool YouTubeEnabled { get; init; } = false;
    public bool InstagramEnabled { get; init; } = false;
    public bool TikTokEnabled { get; init; } = false;
}
```

`appsettings.json` addition (per-platform gates ship **false** — poller is code-complete but dormant until a real token exists and the gate is flipped):
```json
"ChannelAnalytics": {
  "RunAtLocalTime": "05:00",
  "RecentVideoCount": 50,
  "YouTubeEnabled": false,
  "InstagramEnabled": false,
  "TikTokEnabled": false
}
```

Map each `Platform` to its gate: `YouTube -> YouTubeEnabled`, `Instagram -> InstagramEnabled`, `TikTok -> TikTokEnabled`. A disabled platform is skipped even if an active analytics credential exists.

## `ChannelMetricPollingService` behavior spec

Constructor: `(IServiceScopeFactory scopeFactory, IOptionsMonitor<ChannelAnalyticsOptions> optionsMonitor, ILogger<ChannelMetricPollingService> logger)`. `sealed`, primary constructor, `: BackgroundService`.

`ExecuteAsync`: same hourly self-timed loop as `DigestService`. Parse `RunAtLocalTime` (from `optionsMonitor.CurrentValue`) as `TimeOnly` with `"HH:mm"`/`CultureInfo.InvariantCulture`; when `TimeOnly.FromDateTime(now.DateTime) >= runAt`, invoke the run method. Wrap in the `catch (Exception ex) when (ex is not OperationCanceledException)` guard, then `Task.Delay(TimeSpan.FromHours(1), stoppingToken)`.

Expose the run body as an `internal async Task` method (so tests can invoke it directly with a supplied `DateTimeOffset now`). Signature: `internal async Task PollAllAsync(DateTimeOffset now, CancellationToken ct)`.

Per run:

1. **Host-local date.** `var today = DateOnly.FromDateTime(now.LocalDateTime);` (host-local, matching the `RunAtLocalTime` clock and `SnapshotDate`). Same `today` for both the guard and the rows written.

2. **Open a scope**, resolve from it: `ApplicationDbContext`, `IOAuthService`, and — resolved lazily per platform — the keyed `IChannelAnalyticsService` and keyed `IOAuthProvider` (`serviceProvider.GetRequiredKeyedService<IChannelAnalyticsService>(platform)` / `...<IOAuthProvider>(platform)`).

3. **Load candidate credentials:** active analytics credentials for enabled platforms:
   ```csharp
   db.PlatformCredentials.Where(c => c.IsActive && c.Purpose == CredentialPurpose.Analytics)
   ```
   Then filter in memory to the platforms whose gate is enabled (`YouTube|Instagram|TikTok`).

4. **For each candidate credential** (each wrapped so a failure logs and continues to the next platform — never throw out of the loop):

   a. **Idempotency guard.** Skip the platform if an Account-scope snapshot already exists for `(platform, today)`:
   ```csharp
   if (await db.ChannelMetricSnapshots.AnyAsync(
           s => s.Platform == platform && s.SnapshotDate == today && s.Scope == SnapshotScope.Account, ct))
       continue;
   ```
   The Account row is written **last** (step e), so its presence is a true completion sentinel — a mid-write crash leaves no Account row and the platform is re-polled next run.

   b. **Ensure a valid token.** If `provider.NeedsRefresh(credential, now)`, call `oAuthService.RefreshTokenAsync(credential)`:
   - **Success:** the coordinator persists any rotated refresh token; use the refreshed credential for the poll.
   - **Failure with `RefreshFailureReason.Revoked`:** set `credential.IsActive = false`, `SaveChangesAsync`, log a reconnect-required warning, and **skip** this platform.
   - **Failure with `RefreshFailureReason.Transient`:** leave `IsActive = true`, log, and **skip this run only** (retry tomorrow). Do **not** deactivate.

   c. **Poll.** `var result = await service.PollAsync(credential, options.RecentVideoCount, ct);` If failure, log and continue (do not write partial rows).

   d. **Cap videos.** `RecentVideos` capped at `RecentVideoCount` before writing (defensive).

   e. **Transactional write, Video rows first then Account row last.** Open `await db.Database.BeginTransactionAsync(ct)`. Upsert each `VideoMetrics` as a `ChannelMetricSnapshot` (`Scope = Video`, `VideoId = v.VideoId`, `VideoTitle = v.Title`, `Metrics = v.Metrics`), then upsert the single Account row (`Scope = Account`, `VideoId = ""`, `Metrics = account.Metrics`) **last**. `CapturedAt = now`, `SnapshotDate = today`. Commit. If the write throws mid-way, the transaction rolls back -> no Account row -> next run re-polls. Writes are idempotent **upserts** keyed on the unique index `(Platform, SnapshotDate, Scope, VideoId)`.

   Idempotent upsert approach (keep it simple, portable, testable): query existing rows for `(platform, today)` into memory, update matched entities' `Metrics`/`CapturedAt` and `Add` the rest, then `SaveChangesAsync` inside the transaction.

   Note the `Provisional` flag on `AccountMetrics`/`VideoMetrics` exists in the poll result but is **not** persisted — cumulative counts captured at an instant are always final. Ignore it here.

## Tests (write FIRST)

Location: `tests/PBA.Infrastructure.Tests/Services/Analytics/ChannelMetricPollingServiceTests.cs`. xUnit, `Method_Scenario_ExpectedResult`, in-memory/real DB `ApplicationDbContext`, mocked `IChannelAnalyticsService` / `IOAuthProvider` / `IOAuthService` (mock only these + supplied `now`; never mock the service under test). Follow the `Build(...)` helper shape in `DigestServiceTests.cs` — a static factory wiring a `Mock<IServiceScopeFactory>` whose scope's `ServiceProvider` resolves the keyed services and the DbContext. Invoke `PollAllAsync(now, ct)` directly.

Stubs:
```
# Poller_SkipsPlatform_WhenAccountSnapshotExistsForToday      (idempotency)
# Poller_TriggersRefresh_WhenProviderNeedsRefresh
# Poller_DeactivatesCredential_OnlyOnRevoked                  (Revoked -> IsActive=false)
# Poller_TransientRefreshFailure_SkipsRunWithoutDeactivating  (Transient -> IsActive stays true, no rows)
# Poller_PersistsRotatedRefreshToken_AfterRefresh
# Poller_WritesVideoRowsFirst_ThenAccountRowLast              (completion sentinel ordering)
# Poller_MidWriteFailure_LeavesNoAccountRow                   (transaction rollback; next run re-polls)
# Poller_CapsVideoSnapshots_AtRecentVideoCount
# Poller_UsesHostLocalDate_ForSnapshotDateAndGuard
# Poller_ServiceFailure_LogsAndContinues_ToNextPlatform
# Poller_SkipsDisabledPlatforms                               (per-platform gate false)
```

Testing notes:
- `Poller_WritesVideoRowsFirst_ThenAccountRowLast` / `Poller_MidWriteFailure_LeavesNoAccountRow`: inject a failure between the video writes and the account write and assert zero Account rows remain (so the guard re-polls). Assert the Account row's presence is what gates idempotency.
- `Poller_DeactivatesCredential_OnlyOnRevoked` and `Poller_TransientRefreshFailure_SkipsRunWithoutDeactivating` must both exist as **separate** tests — the Revoked-only deactivation is the single most important safety behavior.
- `Poller_UsesHostLocalDate_ForSnapshotDateAndGuard`: pass a `now` whose UTC date and local date differ and assert `SnapshotDate` equals the local date.

## Test-host isolation (required, cross-cutting)

The endpoint `WebApplicationFactory<Program>` (section 06's endpoint tests) must **strip `ChannelMetricPollingService`** so the poller does not start and hit external APIs during integration tests — the same treatment already applied to `DigestService`. Existing factories remove `IHostedService` descriptors:
```csharp
services.RemoveAll(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && ...);
```
Per-platform gates default `false` (second layer), but stripping the hosted service is the primary guard. Verified by section-06's `TestFactory_DoesNotStartChannelMetricPollingService` test.

## DI registration

In `src/PBA.Infrastructure/DependencyInjection.cs`, add alongside `AddHostedService<DigestService>()`:
```csharp
services.Configure<ChannelAnalyticsOptions>(configuration.GetSection(ChannelAnalyticsOptions.SectionName));
services.AddHostedService<ChannelMetricPollingService>();
```

## Verification

- `dotnet test` — all new poller tests green; existing suite unaffected.
- Structured logging via injected `ILogger` only.
- 80% coverage minimum on the new poller class.

Mirror pattern paths: `src/PBA.Infrastructure/Services/Radar/DigestService.cs` and its test `tests/PBA.Infrastructure.Tests/Services/Radar/DigestServiceTests.cs`.

---

## Implementation Outcome (as built)

Implemented as planned, with two deliberate, reviewer-validated deviations. Build clean; 13 poller tests green; full non-Docker Infrastructure suite 448 green; full solution builds.

### Files created / modified
- **Create** `src/PBA.Infrastructure/Services/Analytics/ChannelMetricPollingService.cs`, `src/PBA.Infrastructure/Configuration/ChannelAnalyticsOptions.cs`, `tests/PBA.Infrastructure.Tests/Services/Analytics/ChannelMetricPollingServiceTests.cs`
- **Modify** `src/PBA.Infrastructure/DependencyInjection.cs` (Configure + AddHostedService), `src/PBA.Api/appsettings.json` (`ChannelAnalytics` section, gates false)

### Deliberate deviations (both validated by review)
1. **Refresh via `provider.RefreshAsync` directly, NOT `IOAuthService.RefreshTokenAsync`.** Section-03 evolved the contract: the coordinator returns `Result<string>` (no `RefreshFailureReason`) and has a "null refresh token → deactivate" short-circuit that would wrongly deactivate Instagram (no refresh token). The poller calls the keyed provider directly, reads `OAuthRefreshResult.FailureReason` for revoked-only deactivation, and persists the refreshed tokens itself (re-encrypt access + rotated refresh, set expiry). This is exactly what section-03's interview doc reserved for section-05.
2. **No explicit DB transaction.** Video rows are written first (SaveChanges), then the Account row LAST (SaveChanges) as the completion sentinel; stale video rows are cleared first in a separate SaveChanges. This self-heals a crash between writes (next run re-polls, clears, rewrites — no duplicates) and is portable to the InMemory test provider. Reviewer confirmed the crash-safety invariant holds and that splitting delete/insert into separate SaveChanges correctly avoids the EF/Postgres insert-before-delete unique-key hazard.

### Review fixes applied (see `implementation/code_review/section-05-interview.md`)
- **HIGH:** dedup video rows by `VideoId` before the cap (a duplicate would violate the unique index → permanent per-platform snapshot failure on Postgres). Tested.
- **HIGH:** added a two-run self-heal test exercising the stale-clear branch (was untested).
- **MEDIUM:** strengthened the host-local date test with a conditional non-UTC assertion.

### Decisions (documented)
- **Hourly retry, not "retry tomorrow":** the poller mirrors `DigestService` — a platform without a completion sentinel is re-attempted each hourly tick until it succeeds or the day ends (same-day recovery, ≥1h spacing). Supersedes the plan's imprecise "retry tomorrow" wording.
- **One active analytics cred per platform** is enforced by section-02's `(Platform, Purpose)` filtered unique index, so the poller's platform+date guard is sufficient.

### Gate status
Poller is **code-complete but dormant** — per-platform gates ship `false` in `appsettings.json`. It stays inert until a real analytics token exists and the gate is flipped. Section-06's endpoint tests must strip `ChannelMetricPollingService` from the test host (per §"Test-host isolation").
