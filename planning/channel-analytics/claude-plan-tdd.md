# Channel Analytics — TDD Plan

Test stubs to write **before** implementing each part, organized by the build sequence (plan §17). Backend
= xUnit (`Method_Scenario_ExpectedResult`), `WebApplicationFactory<Program>` + in-memory/real DB for
endpoint/pipeline tests, mock only external HTTP clients + `TimeProvider`. Frontend = Jasmine/Karma +
`HttpTestingController` with `afterEach(() => httpMock.verify())`. 80% coverage minimum. These are stubs —
the implementer writes the assertions.

## Step 1 — OAuth refactor + regression (plan §7)

Regression (guard existing publishing — write these FIRST, they must stay green through the refactor):
```
# LinkedInOAuthProvider_BuildAuthorization_ProducesUnchangedUrlScopesAndState
# TwitterOAuthProvider_BuildAuthorization_IncludesPkceS256ChallengeAndPersistsVerifier
# TwitterOAuthProvider_ExchangeCode_UsesBasicAuthAndCodeVerifier_Unchanged
# LinkedInOAuthProvider_ExchangeCode_ReturnsTokenResult_Unchanged
# LinkedInOAuthProvider_RefreshAsync_RefreshesToken_BehaviorUnchanged   (highest-traffic path)
# TwitterOAuthProvider_RefreshAsync_UsesBasicAuth_BehaviorUnchanged
# OAuthService_GetAuthorizationUrl_ResolvesKeyedProviderAndPersistsStateAdditions
# OAuthService_ExchangeCode_UnknownPlatform_ReturnsNotSupported
# OAuthService_ExchangeCode_DefaultsPurposeToPublishing
```
Boundary:
```
# OAuthService_PersistsTwitterCodeVerifier_FromProviderStateAdditions   (no Twitter logic leaks into coordinator)
# OAuthProviderMap_ResolvesOneProviderPerPlatform_ViaKeyedDI
```

## Step 2 — Domain + persistence (plan §4, §5)

```
# Platform_Enum_YouTubeInstagramTikTok_HaveStableNumericValues   (no renumber of existing members)
# ChannelMetricSnapshot_AccountScope_UsesEmptyStringVideoIdSentinel
# PlatformCredentialConfiguration_AllowsActivePublishingAndAnalyticsForSamePlatform   (composite index)
# PlatformCredentialConfiguration_RejectsTwoActiveAnalyticsCredentials_SamePlatform
# ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateAccountRow_SamePlatformDate
# ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateVideoRow_SamePlatformDateVideo
# ChannelMetricSnapshotConfiguration_MetricsBag_RoundTripsThroughJsonb
# Migration_AddChannelAnalytics_AppliesAndReverts_OnCleanDb   (or assert model snapshot delta)
```

## Step 3 — New OAuth providers (plan §7.1, §7.3)

Per provider (YouTube, Instagram, TikTok):
```
# {Provider}_BuildAuthorization_IncludesCorrectScopesAndRedirectUri
# {Provider}_ExchangeCode_MapsTokenResponseToOAuthTokenResult   (canned token JSON)
# YouTubeProvider_RefreshAsync_UsesRefreshToken
# TikTokProvider_RefreshAsync_PersistsRotatedRefreshToken
# InstagramProvider_RefreshAsync_ExtendsLongLivedAccessToken_NoRefreshToken   (C3 guard: does NOT fail on missing refresh token)
# {Provider}_NeedsRefresh_ReturnsTrue_WithinProviderLeadTime   (Google ~1h, TikTok ~24h, IG ~50d)
# {Provider}_RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked
# {Provider}_RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient
# OAuthEndpoints_AllowsYouTubeInstagramTikTok_Authorize
# OAuthEndpoints_Authorize_WithPurposeAnalytics_StoresCredentialAsAnalytics
```

## Step 4 — Per-platform analytics services (plan §6)

YouTube (canned Data API v3 / Analytics v2 JSON via stub client):
```
# YouTubeAnalyticsService_PollAsync_MapsChannelStatistics_ToCumulativeAccountKeys
# YouTubeAnalyticsService_PollAsync_DiscoversRecentVideos_ViaUploadsPlaylist_NotSearch
# YouTubeAnalyticsService_PollAsync_BatchesVideosList_MaxFiftyIds
# YouTubeAnalyticsService_PollAsync_CapsRecentVideos_AtN
# YouTubeDeepAnalytics_Query_MapsReportsRows_ToLabeledSeries   (live path, mocked client)
```
Instagram (canned Graph JSON):
```
# InstagramAnalyticsService_PollAsync_MapsAccountInsights_ToCanonicalKeys
# InstagramAnalyticsService_PollAsync_DropsUnknownDeprecatedMetric_WithoutFailing   (deprecation tolerance)
# InstagramAnalyticsService_PollAsync_MapsPerMediaInsights
```
TikTok (canned Display JSON):
```
# TikTokAnalyticsService_PollAsync_MapsUserInfoStats_ToAccountKeys
# TikTokAnalyticsService_PollAsync_PaginatesVideoList_ToReachN   (20/page cursor loop)
# TikTokAnalyticsService_PollAsync_MapsPerVideoCounts
```
Common:
```
# {Service}_PollAsync_ApiError_ReturnsResultFail_NotThrow
# {Service}_PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics
```

## Step 5 — Poller (plan §8)

`ChannelMetricPollingService` with mocked services + keyed providers + in-memory DB:
```
# Poller_SkipsPlatform_WhenAccountSnapshotExistsForToday   (idempotency)
# Poller_TriggersRefresh_WhenProviderNeedsRefresh
# Poller_DeactivatesCredential_OnlyOnRevoked   (Transient leaves IsActive=true)
# Poller_TransientRefreshFailure_SkipsRunWithoutDeactivating
# Poller_PersistsRotatedRefreshToken_AfterRefresh
# Poller_WritesVideoRowsFirst_ThenAccountRowLast   (completion sentinel)
# Poller_MidWriteFailure_LeavesNoAccountRow   (transaction rollback; next run re-polls)
# Poller_CapsVideoSnapshots_AtRecentVideoCount
# Poller_UsesHostLocalDate_ForSnapshotDateAndGuard
# Poller_ServiceFailure_LogsAndContinues_ToNextPlatform
# Poller_SkipsDisabledPlatforms   (per-platform gate false)
```

## Step 6 — Read queries + endpoints (plan §9, §10)

Handlers over seeded snapshots (in-memory DB):
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
Endpoints (`WebApplicationFactory`, poller stripped):
```
# ChannelAnalyticsEndpoints_Overview_ReturnsOk
# ChannelAnalyticsEndpoints_Channel_InvalidPlatform_ReturnsBadRequest
# ChannelAnalyticsEndpoints_Channel_ParsesPeriod
# ChannelAnalyticsEndpoints_MapsResultFailure_ViaToApiResult
# TestFactory_DoesNotStartChannelMetricPollingService   (M4 isolation)
```

## Step 7 — Frontend (plan §12)

Service (`HttpTestingController`):
```
# analytics.service getOverview() GETs /api/analytics/overview?period=
# analytics.service getChannel(platform) GETs /api/analytics/channel/{platform}?period=
# analytics.service getYouTubeDeep() GETs /api/analytics/youtube/deep?period=
```
Components:
```
# overview.component renders total audience (labeled approximate) + per-platform sparklines from mocked data
# channel-analytics.component renders KPI row + trend charts + recent-posts table
# channel-analytics.component shows Connect affordance when status = NotConnected
# channel-analytics.component shows Reconnect affordance when status = ReconnectRequired
# youtube tab renders deep-analytics panel from getYouTubeDeep(); hides/degrades on error
# website tab renders unchanged (regression)
# analytics shell switches source tabs and lazy-loads each source's data on activation
```

## Runbook (plan §13)
No automated tests (documentation deliverable). Manual verification checklist lives in the runbook itself.
