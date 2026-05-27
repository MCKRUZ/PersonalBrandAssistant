# Section 13: Frontend Connections

## Status: IMPLEMENTED

## Overview

This section builds the **Platform Connections** settings page in the Angular frontend. A new sub-page under Settings (`/settings/platforms`) where the user manages connections to publishing platforms.

## What Was Built

### Files Created

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.ts` | Container component — loads platforms, handles OAuth query params, manages connect/disconnect/credential submission |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.ts` | Presentational card with signal inputs, computed status/class/text, inline form toggling |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/medium-token-form/medium-token-form.component.ts` | Reactive Forms token entry with minLength(10) validation, signal inputs |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/substack-login-form/substack-login-form.component.ts` | Reactive Forms email/password, clears password on submit, signal inputs |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.ts` | HTTP service: getPlatforms, getStatus, getAuthorizeUrl, storeCredentials, disconnect |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.spec.ts` | 7 tests covering all service methods |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/models/platform-connection.model.ts` | TypeScript interfaces: ConnectionStatus, PlatformStatus, PlatformCapabilities, StoreCredentialsRequest, PlatformConfig |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts` | Child routes: general (default) and platforms |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/general/general-settings.component.ts` | Placeholder for future general settings |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.spec.ts` | 12 tests for card rendering, status badges, events, form toggling |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.spec.ts` | 7 tests for platform loading, error state, notifications, disconnect/credential flows |

### Files Modified

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` | Changed settings route from `loadComponent` to `loadChildren` pointing to `settings.routes.ts` |
| `src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts` | Transformed from placeholder to tabbed layout with RouterLink/RouterLinkActive/RouterOutlet |

### Pre-existing Test Fixes (included in this commit)

| File | Fix |
|------|-----|
| `src/PersonalBrandAssistant.Web/src/app/features/news/store/news-dismiss.spec.ts` | Full rewrite — old spec used `trends/suggestions` API that no longer exists; now uses Ideas API (`/api/ideas`, `/api/ideas/{id}/dismiss`) |
| `src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.spec.ts` | Added `description: null, url: null` to mockIdea |
| `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/save-idea-dialog/save-idea-dialog.component.spec.ts` | Added `description: null, url: null` to mockIdea |
| `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts` | Added `description: null, url: null` to mockIdea |
| `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-list/idea-list.component.spec.ts` | Added `description: null, url: null` to mockIdea |
| `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-grid/idea-grid.component.spec.ts` | Added `description: null, url: null` to both mockIdeas |

## Code Review Fixes Applied

- **HIGH-1**: Validate query param `connected` against known platform list before displaying in notification (prevents user-controlled text in UI)
- **HIGH-2**: Added `DestroyRef` + `takeUntilDestroyed` to queryParams subscription (prevents memory leak)
- **HIGH-3**: Converted all 3 presentational components from `@Input`/`@Output` decorators to signal inputs (`input()`, `output()`, `computed()`) — matches codebase convention
- **MEDIUM-3**: Use `inject(DOCUMENT)` instead of direct `window.location.href` for testability
- **MEDIUM-4**: Use spread instead of `delete` operator for immutable `credentialErrors` updates
- **MEDIUM-5**: Added error notification on disconnect failure

## Test Results

371/371 Angular tests passing (0 failures).

## Deviations from Plan

1. **Inline templates**: All components use inline templates/styles rather than separate `.html`/`.css` files — matches existing codebase pattern for small-medium components
2. **Signal inputs**: All presentational components use Angular signal inputs (`input()`, `output()`, `computed()`) instead of decorator-based `@Input`/`@Output` — aligns with codebase convention established in other features
3. **Brand colors**: Used project's actual brand colors (#c87156 primary, #141418 surface, #f0f0f5 text) rather than plan's GitHub-style colors
4. **No separate `.html` files**: Plan listed `.component.html` files but implementation uses inline templates consistent with existing components
