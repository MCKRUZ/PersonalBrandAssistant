# Section 14: Frontend Editor — Platform Targets, Character Counts, Publish Modal, and Status Badges

## Status: IMPLEMENTED

## Overview

This section adds multi-platform publishing UI to the content editor and content list. Four areas changed:

1. **Platform target checkboxes** on the content editor for selecting which platforms to publish to
2. **Character count indicators** per platform below the editor toolbar
3. **Publish confirmation modal** with per-platform toggle and connection status
4. **Content list status badges** showing per-platform publish results with retry

## What Was Built

### Files Created

| File | Purpose |
|------|---------|
| `features/content/content-editor/platform-targets/platform-targets.component.ts` | Horizontal row of platform checkboxes with character/word counts, signal inputs |
| `features/content/content-editor/platform-targets/platform-targets.component.spec.ts` | 9 tests: checkbox rendering, connected/disconnected state, primary locked, toggle on/off, char counts, word counts, over-limit highlight |
| `features/content/content-editor/publish-modal/publish-modal.component.ts` | Custom modal for publish/schedule confirmation with per-platform toggles, connection status, ARIA attributes |
| `features/content/content-editor/publish-modal/publish-modal.component.spec.ts` | 8 tests: primary display, connection status, toggle secondary, primary locked, confirm/cancel, schedule header |

All paths relative to `src/PersonalBrandAssistant.Web/src/app/`.

### Files Modified

| File | Change |
|------|--------|
| `features/content/models/content.model.ts` | Added Medium to Platform enum, PUBLISHABLE_PLATFORMS (readonly), PLATFORM_CHAR_LIMITS, PublishStatus enum, targetPlatforms/platformPublishes to Content, PlatformPublish retryCount/nextRetryAt, PlatformPublishSummary, PublishRequest, PublishStatusResponse, PlatformConnectionStatus, PlatformCapabilities, ContentFilterState |
| `features/content/services/content.service.ts` | Updated `publish()` to accept optional `PublishRequest`, added `getPublishStatus()`, `retryPlatform()`, `getPlatforms()` methods |
| `features/content/services/content.service.spec.ts` | 4 new tests (publish with targetPlatforms, getPublishStatus, retryPlatform, getPlatforms), updated existing mockDetail |
| `features/content/stores/content-editor.store.ts` | Added `targetPlatforms` to `autoSave()` UpdateContentRequest |
| `features/content/stores/content-editor.store.spec.ts` | Updated mockDetail and autoSave assertion to include targetPlatforms |
| `features/content/stores/content.store.spec.ts` | Added targetPlatforms/platformPublishes to mockContent |
| `features/content/content-editor/content-editor.component.ts` | Imported PlatformTargetsComponent + PublishModalComponent, added connectedPlatforms signal, wordCount computed, publishModalVisible/publishMode signals, replaced onPublish/onSchedule with modal flow, added onPublishConfirm/onTargetPlatformsChange handlers |
| `features/content/content-editor/content-editor.component.spec.ts` | Updated mockContent with targetPlatforms, added getPlatforms to service spy |
| `features/content/content-editor/sidecar-chat/sidecar-chat.component.spec.ts` | Added targetPlatforms to mockContent |
| `features/content/content-list/content-card/content-card.component.ts` | Added publish status badges with data-status CSS, retry button, retry output |
| `features/content/content-list/content-card/content-card.component.spec.ts` | Added targetPlatforms/platformPublishes to mockContent, 5 new tests for badges/retry |
| `features/content/content-list/content-list.component.spec.ts` | Added targetPlatforms/platformPublishes to mockContent |
| `features/content/content-list/content-display.utils.ts` | Added Medium to icon map, added publishStatusSeverity utility using PublishStatus enum |

## Code Review Fixes Applied

- **HIGH-1**: Added `effect()` in PublishModalComponent to reset `selected` and `scheduledAt` signals when modal opens (prevents stale state)
- **HIGH-2**: Disabled confirm button in schedule mode when no date entered (prevents silent publish-instead-of-schedule)
- **HIGH-3**: Added error handling to `onPublishConfirm` subscribe calls (reload state on failure)
- **MEDIUM**: Removed dead `visibleChange` output from PublishModalComponent
- **MEDIUM**: Removed dead `pubSeverity` property from ContentCardComponent
- **MEDIUM**: Used `PublishStatus` enum values in `publishStatusSeverity` instead of string literals
- **LOW**: Made `PUBLISHABLE_PLATFORMS` readonly
- **LOW**: Added `role="dialog" aria-modal="true"` to publish modal

## Test Results

397/397 Angular tests passing (0 failures).

## Deviations from Plan

1. **Custom modal instead of PrimeNG Dialog**: Used a custom overlay with ARIA attributes instead of PrimeNG's DialogModule — matches the lightweight pattern used elsewhere in the codebase and avoids heavy dependency
2. **Primary platform always included**: The publish modal always includes the primary platform in selectedPlatforms. The "disable confirm if no platforms" test was adjusted since primary can't be deselected
3. **Signal-based state in modal**: Used internal `selected` signal with computed derivation instead of two-way binding on checkboxes
4. **No separate schedule endpoint with targetPlatforms**: The schedule path reuses the existing schedule endpoint (date only), while the publish path sends targetPlatforms. Multi-platform scheduling would need a backend change
