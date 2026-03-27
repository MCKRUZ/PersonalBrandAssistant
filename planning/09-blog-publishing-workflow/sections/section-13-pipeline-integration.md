# Section 13: Pipeline Integration

## Overview

Wires everything together: modifies the content pipeline wizard to route BlogPost content through the chat authoring step, adds Substack Prep and Blog Prep tabs to the content detail view, updates the workflow panel with blog-specific states, and adds navigation to the blog dashboard. Final integration tests.

**Depends on:** All previous sections (07, 08, 09, 10, 11, 12)
**Blocks:** Nothing (final section)

---

## Tests (Write First)

### Modified Pipeline Wizard Tests
File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/content-pipeline-dialog.component.spec.ts`

```typescript
// Test: Pipeline wizard routes to chat step for ContentType.BlogPost
// Test: Pipeline wizard routes to outline+draft steps for other content types
// Test: Finalize step calls ExtractFinalDraft and generates both format versions
// Test: Publish Prep step shows Substack fields with copy buttons
// Test: Publish Prep step shows blog HTML preview
// Test: "Publish to Blog" button disabled when SubstackPostUrl is null
```

### Workflow Panel Tests
File: `src/PersonalBrandAssistant.Web/src/app/features/content/components/content-workflow-panel.component.spec.ts`

```typescript
// Test: Workflow panel shows "Authoring" during active chat
// Test: Workflow panel shows "Ready for Substack" after finalization
// Test: Workflow panel shows "Awaiting Substack Publication" before detection
// Test: Workflow panel shows "Substack Live" after RSS detection
// Test: Workflow panel shows "Blog Scheduled" with date after scheduling
// Test: Workflow panel shows "Blog Ready" when scheduled date reached
// Test: Workflow panel shows "Published" when both platforms live
```

### Integration Tests (Backend)
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/BlogPublishingWorkflowTests.cs`

```csharp
// Test: Full flow: create blog post → chat → finalize → prep both formats
// Test: Substack detection: simulate RSS entry → notification created
// Test: Blog scheduling: confirm schedule → ContentPlatformStatus.ScheduledAt set
// Test: Blog publish: trigger deploy → GitHub API called → status updated
```

---

## Implementation Details

### 1. Modified Pipeline Wizard

Modify: `src/PersonalBrandAssistant.Web/src/app/features/content/components/content-pipeline-dialog.component.ts`

The existing wizard has steps: Topic → Outline → Draft → Review. For `ContentType.BlogPost`:

**New flow:**
1. **Topic** -- same as today (user enters topic/brief)
2. **Chat Authoring** -- embed `<app-blog-chat>` component. Replace outline+draft steps.
3. **Finalize** -- triggered by blog-chat's `(finalized)` output. Generate both Substack prep and blog HTML by calling the respective APIs.
4. **Publish Prep** -- new step with two tabs:
   - Substack tab: embed `<app-substack-prep>` component
   - Blog tab: embed `<app-blog-publish>` component
5. **Track** -- cross-platform status (optional, or integrated into content detail)

**Detection logic:** Check `ContentType` in the wizard's initialization. If `BlogPost`, show steps 1,2,3,4. If other types, show the existing 1 (Topic), 2 (Outline), 3 (Draft), 4 (Review) flow.

### 2. Content Detail View Enhancement

Modify: `src/PersonalBrandAssistant.Web/src/app/features/content/components/content-detail.component.ts`

For content with `ContentType.BlogPost`, add two new tabs to the detail view:
- **Substack Prep** tab: `<app-substack-prep [contentId]="content.id">`
- **Blog Prep** tab: `<app-blog-publish [contentId]="content.id" [substackPostUrl]="content.substackPostUrl">`

These tabs appear alongside existing tabs (Details, History, etc.). They are only rendered for BlogPost content.

### 3. Workflow Panel Update

Modify: `src/PersonalBrandAssistant.Web/src/app/features/content/components/content-workflow-panel.component.ts`

For BlogPost content, compute a blog-specific workflow state based on:
- Chat conversation exists but not finalized → "Authoring"
- Finalized, SubstackPostUrl is null → "Ready for Substack"
- SubstackPostUrl is null, waiting → "Awaiting Substack Publication"
- SubstackPostUrl set → "Substack Live"
- PersonalBlog ContentPlatformStatus.Status == Scheduled → "Blog Scheduled (date)"
- Blog scheduled date reached, pending user trigger → "Blog Ready"
- PersonalBlog ContentPlatformStatus.Status == Published → "Published"

Display as a horizontal stepper or status pills showing progression through the stages.

### 4. Navigation

Modify: the sidebar or navigation component to add a "Blog Publishing" link that routes to the blog dashboard (section 12).

### 5. Notification Integration

Add a notification indicator to the app shell/header that shows pending notification count. When clicked, shows a dropdown with pending notifications and their action buttons. The blog workflow notifications ("Schedule blog deploy?", "Blog ready for deployment") appear here with inline action buttons.

If PBA already has a notification UI, integrate with it. If not, add a minimal notification badge + dropdown.

### 6. Content Model Updates

Modify: `src/PersonalBrandAssistant.Web/src/app/features/content/models/content.models.ts`

Add the new fields to the Content interface:
```typescript
substackPostUrl?: string;
blogPostUrl?: string;
blogDeployCommitSha?: string;
blogDelayOverride?: string; // TimeSpan as ISO duration
blogSkipped?: boolean;
```

Update the content store and API service to include these fields.

---

## Files

### Modify
| File | Change |
|------|--------|
| `content-pipeline-dialog.component.ts` | Route BlogPost to chat step, add publish prep step |
| `content-detail.component.ts` | Add Substack Prep and Blog Prep tabs for BlogPost |
| `content-workflow-panel.component.ts` | Add blog-specific workflow states |
| `content.models.ts` | Add new Content fields |
| `content.store.ts` | Update to handle new fields |
| Navigation component | Add Blog Publishing link |
| App shell/header | Add notification indicator (if not existing) |
| `app.routes.ts` | Ensure blog-publishing route registered |

### Test Files
| File | Purpose |
|------|---------|
| `content-pipeline-dialog.component.spec.ts` | Modify: add BlogPost routing tests |
| `content-workflow-panel.component.spec.ts` | Modify: add blog state tests |
| `BlogPublishingWorkflowTests.cs` | Create: end-to-end integration tests |
