# Section 12: Blog Publishing Dashboard

## Overview

Dedicated Angular page showing all blog posts across the two-stage publishing pipeline. Table/card list with Substack + Blog status badges, timeline visualization, stats header, filtering, and action buttons (schedule, override delay, skip, publish).

**Depends on:** Section 07 (Blog Pipeline API), Section 08 (Notification System)
**Blocks:** Section 13 (Pipeline Integration)

---

## Tests (Write First)

File: `src/PersonalBrandAssistant.Web/src/app/features/blog-publishing/blog-dashboard.component.spec.ts`

```typescript
// Test: renders list of blog posts with correct status badges
// Test: shows stats header (X in pipeline, Y scheduled, Z published)
// Test: Substack status badge colors: gray (draft), blue (ready), green (published)
// Test: Blog status badge colors: gray (waiting), yellow (scheduled), blue (ready), green (published), red (skipped)
// Test: timeline visualization shows dots at correct positions
// Test: timeline colors: green (published), yellow (scheduled), gray (pending)
// Test: filters by status
// Test: filters by date range
// Test: Schedule button calls POST /api/blog-pipeline/{id}/schedule
// Test: Skip Blog button calls POST /api/blog-pipeline/{id}/skip-blog and updates UI
// Test: Publish button disabled when Substack not published
// Test: delay display shows "7 days" default or custom override
```

---

## Implementation Details

### Component Structure

```
Web/src/app/features/blog-publishing/
  blog-dashboard.component.ts          # Main dashboard page
  blog-dashboard.component.html
  blog-dashboard.component.scss
  blog-pipeline-card.component.ts      # Individual post card with timeline
  blog-pipeline-card.component.html
  blog-timeline.component.ts           # Horizontal timeline visualization
  blog-timeline.component.html
  blog-dashboard.store.ts              # NgRx signal store
  blog-dashboard.service.ts            # HTTP service
  blog-dashboard.models.ts             # TypeScript interfaces
```

### Dashboard Layout

**Header**: "Blog Publishing" title. Stats row: `X in pipeline | Y scheduled | Z published`.

**Filter bar**: Status dropdown (All, Pending Substack, Scheduled Blog, Published), date range picker.

**Content area**: Card list (not table -- cards provide more room for timeline). Each card shows:
- Title (linked to content detail)
- Substack status badge + published date
- Blog status badge + scheduled/published date
- Delay indicator ("7 days" or "3 days (custom)")
- Timeline visualization
- Action buttons

### Timeline Component

Horizontal line with two dots:
- Left dot: Substack publish date (green if published, gray if pending)
- Connecting line: delay period, with label ("7 days")
- Right dot: Blog publish date (green if published, yellow if scheduled, gray if pending)

SVG-based for clean rendering. Width proportional to delay.

### NgRx Signal Store
File: `blog-dashboard.store.ts`

```typescript
export const BlogDashboardStore = signalStore(
    withState<{ items: BlogPipelineItem[]; loading: boolean; filter: DashboardFilter }>(...),
    withMethods((store, service = inject(BlogDashboardService)) => ({
        loadItems: rxMethod<DashboardFilter>(...),
        scheduleItem: (id: string) => ...,
        skipItem: (id: string) => ...,
        updateDelay: (id: string, delay: string) => ...,
    }))
);
```

### Service
File: `blog-dashboard.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class BlogDashboardService {
    getItems(filter?: DashboardFilter): Observable<BlogPipelineItem[]>;
    schedule(contentId: string): Observable<{ scheduledAt: string }>;
    updateDelay(contentId: string, delay: string): Observable<void>;
    skipBlog(contentId: string): Observable<void>;
}
```

### Route Registration

Add route in the content feature routing or a new blog-publishing feature module:
```typescript
{ path: 'blog-publishing', component: BlogDashboardComponent }
```

Add navigation link in the sidebar/nav component.

---

## Files
| File | Action |
|------|--------|
| `Web/src/app/features/blog-publishing/blog-dashboard.component.ts` | Create |
| `Web/src/app/features/blog-publishing/blog-dashboard.component.html` | Create |
| `Web/src/app/features/blog-publishing/blog-dashboard.component.scss` | Create |
| `Web/src/app/features/blog-publishing/blog-pipeline-card.component.ts` | Create |
| `Web/src/app/features/blog-publishing/blog-pipeline-card.component.html` | Create |
| `Web/src/app/features/blog-publishing/blog-timeline.component.ts` | Create |
| `Web/src/app/features/blog-publishing/blog-timeline.component.html` | Create |
| `Web/src/app/features/blog-publishing/blog-dashboard.store.ts` | Create |
| `Web/src/app/features/blog-publishing/blog-dashboard.service.ts` | Create |
| `Web/src/app/features/blog-publishing/blog-dashboard.models.ts` | Create |
| `Web/src/app/app.routes.ts` (or feature routes) | Modify |
| Navigation component | Modify (add link) |
