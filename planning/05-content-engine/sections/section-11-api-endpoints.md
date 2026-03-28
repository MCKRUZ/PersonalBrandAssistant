# Section 11: API Endpoints

## Overview

Six Minimal API endpoint groups exposing all Content Engine services via HTTP. Each follows the established convention: static class with `Map*Endpoints` extension, registered in `Program.cs`.

## Files Created

- `src/PersonalBrandAssistant.Api/Endpoints/ContentPipelineEndpoints.cs` — `/api/content-pipeline` (create, outline, draft, submit)
- `src/PersonalBrandAssistant.Api/Endpoints/RepurposingEndpoints.cs` — `/api/repurposing` (repurpose, suggestions, tree)
- `src/PersonalBrandAssistant.Api/Endpoints/CalendarEndpoints.cs` — `/api/calendar` (slots, series, manual slots, assign, auto-fill)
- `src/PersonalBrandAssistant.Api/Endpoints/BrandVoiceEndpoints.cs` — `/api/brand-voice` (score)
- `src/PersonalBrandAssistant.Api/Endpoints/TrendEndpoints.cs` — `/api/trends` (suggestions, accept, dismiss, refresh)
- `src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs` — `/api/analytics` (performance, top content, refresh)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEngineEndpointsTests.cs` — 18 tests

## Files Modified

- `src/PersonalBrandAssistant.Api/Program.cs` — Added 6 `Map*Endpoints()` registrations

## Deviations from Original Plan

1. **RepurposingEndpoints route:** Changed from `/api/content` to `/api/repurposing` to avoid route collision with existing ContentEndpoints
2. **ValidateVoice removed from ContentPipelineEndpoints:** Duplicate of BrandVoiceEndpoints.GetScore — kept only in BrandVoiceEndpoints
3. **Single test file instead of 6:** Combined all endpoint tests into `ContentEngineEndpointsTests.cs` using WebApplicationFactory with mock service injection (matching AgentEndpointsTests pattern)
4. **BrandVoice update profile endpoint not implemented:** Plan spec'd `PUT /api/brand-voice/profile` but no `UpdateProfile` method exists on `IBrandVoiceService`

## Key Design Decisions

- **Autonomy enforcement at API layer:** CalendarEndpoints.AutoFill and TrendEndpoints.RefreshTrends check `AutonomyConfiguration.GlobalLevel` before calling services
- **Input validation:** GetSlots and AutoFill validate `from < to` and `range <= 90 days`
- **Auth via global middleware:** Consistent with all existing endpoints — no per-route `.RequireAuthorization()`

## Test Coverage (18 tests)

| Group | Tests |
|-------|-------|
| ContentPipeline | 5 (create 201, create no auth 401, outline 200, draft 200, submit 404) |
| Repurposing | 3 (repurpose 200, no auth 401, tree 404) |
| Calendar | 2 (get slots 200, create series 201) |
| BrandVoice | 2 (score 200, score 404) |
| Trends | 3 (suggestions 200, accept 404, refresh no auth 401) |
| Analytics | 3 (performance 200, top 200, refresh 202) |

## Code Review Fixes Applied

- CRITICAL-02: Added from/to validation to AutoFill endpoint
- HIGH-02: Changed RepurposingEndpoints route to `/api/repurposing`
- HIGH-03: Removed duplicate ValidateVoice from ContentPipelineEndpoints
- HIGH-05: Fixed TrendEndpoints.RefreshTrends error handling
