# Section 12: Docker Compose & DI Configuration

## Overview

Final integration section for the Content Engine phase. Wires remaining services into DI, adds Docker Compose definitions for sidecar/TrendRadar/FreshRSS, and adds configuration binding for options classes.

## Files Created

- `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/ContentEngineServiceRegistrationTests.cs` — 12 tests (DI resolution, options binding, hosted service registration)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Docker/DockerComposeValidationTests.cs` — 1 test (sidecar port security)

## Files Modified

- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` — Added ITrendMonitor, IEngagementAggregator scoped registrations + SidecarOptions/TrendMonitoringOptions config binding + 4 hosted services
- `src/PersonalBrandAssistant.Api/appsettings.json` — Added Sidecar, ContentEngine, TrendMonitoring config sections
- `docker-compose.yml` — Added sidecar, trendradar, freshrss services + internal network + volume declarations
- `docker-compose.override.yml` — Added sidecar port (3001) and freshrss port (8080) for dev only

## Deviations from Original Plan

1. **Options classes already existed:** SidecarOptions, ContentEngineOptions, TrendMonitoringOptions were created in prior sections — no creation needed
2. **MockSidecarClient already existed:** Created in section 02 — no new mock needed
3. **IChatClientFactory already removed:** Section 03 (Agent Refactoring) handled this — no removal needed
4. **SidecarHealthCheck not added:** Class was never created in section 02, so health check registration skipped
5. **FreshRSS port moved to override:** Code review caught that port 8080 was exposed in base compose file — moved to override for production security
6. **BackgroundServices test uses descriptor capture:** DiTestFactory strips hosted services, so test captures `IHostedService` descriptors from service collection before removal via `ConfigureTestServices`
7. **ISidecarClient singleton test added:** Code review identified this was missing from spec test list

## Key Design Decisions

- **Internal Docker network:** Sidecar and TrendRadar services join `internal` (bridge, `internal: true`) network only — no external access
- **API joins both networks:** `default` (for web/db) + `internal` (for sidecar/trendradar/freshrss)
- **Environment-based config override:** Docker Compose sets `Sidecar__WebSocketUrl`, `TrendMonitoring__TrendRadarApiUrl`, `TrendMonitoring__FreshRssApiUrl` to use Docker DNS names
- **Pinned image versions:** TrendRadar 0.3.0, FreshRSS 1.24.3 — no `latest` tags

## Test Coverage (13 tests)

| Group | Tests |
|-------|-------|
| DI Resolution | 7 (ISidecarClient singleton, IContentPipeline, IRepurposingService, IContentCalendarService, IBrandVoiceService, ITrendMonitor, IEngagementAggregator) |
| Options Binding | 3 (SidecarOptions, ContentEngineOptions, TrendMonitoringOptions) |
| Hosted Services | 1 (all 4 background processors registered) |
| Docker Compose | 1 (sidecar no published ports in base compose) |
| ISidecarClient | 1 (singleton verification via Assert.Same) |

## Code Review Fixes Applied

- HIGH-01: Moved FreshRSS port from base docker-compose.yml to override
- HIGH-04: Added ISidecarClient_Resolves_AsSingleton test
