# Section 11: Platform Health Cards, Website Analytics Section, and Substack Section

## Overview

This section implements three frontend components that display per-platform metrics, website analytics (GA4/Search Console), and Substack RSS post listings. These are the lower detail sections of the analytics dashboard, rendered below the KPI cards and chart rows.

**Components to create:**
- `PlatformHealthCardsComponent` -- per-platform cards with brand colors, follower counts, avg engagement
- `WebsiteAnalyticsSectionComponent` -- GA4 overview metrics, top pages table, traffic sources, search queries
- `SubstackSectionComponent` -- RSS post listing with publish dates and summaries

**Design reference:** The approved mockup at `publish/analytics-dashboard.html` shows the platform health cards in a 5-column grid (`.platform-grid`) between the charts row and the bottom row. The website/substack sections are additional detail panels below the main dashboard grid.

---

## Dependencies

- **Section 07 (Frontend Models & Service):** TypeScript interfaces `PlatformSummary`, `WebsiteAnalyticsResponse`, `WebsiteOverview`, `PageViewEntry`, `TrafficSourceEntry`, `SearchQueryEntry`, `SubstackPost` must exist in `features/analytics/models/dashboard.model.ts`
- **Section 08 (Analytics Store):** The rewritten `AnalyticsStore` must expose `platformSummaries()`, `websiteData()`, and `substackPosts()` signals
- **Section 09 (Dashboard Page & KPIs):** The main `AnalyticsDashboardComponent` must be set up and ready to include these three child components in its template

---

## Tests First

All test files go under `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/`.

### PlatformHealthCardsComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts`

Tests to implement:

1. **Renders a card for each platform in the input array.** Provide 5 `PlatformSummary` objects (TwitterX, LinkedIn, YouTube, Instagram, Reddit). Expect 5 `.platform-card` elements in the DOM.

2. **Displays platform brand color as the top border.** For the TwitterX card (`--platform-color: #1DA1F2`), verify the CSS custom property is applied via the `[style]` binding on the card element.

3. **Shows follower count when available.** Provide a `PlatformSummary` with `followerCount: 2841`. Verify the rendered text contains "2,841" (formatted with locale number pipe).

4. **Shows "N/A" when followerCount is null.** Provide a `PlatformSummary` with `followerCount: null` (e.g., Reddit karma-only). Verify the follower row shows "N/A" or the specific label appropriate for that platform.

5. **Shows post count and average engagement.** Verify that `postCount` and `avgEngagement` values are rendered in the stat rows.

6. **Displays top post title when present.** Provide a summary with `topPostTitle: 'Why agent frameworks need a rethink'`. Verify that text appears in the `.p-best` section.

7. **Shows "Coming Soon" badge for LinkedIn.** Provide a `PlatformSummary` with `platform: 'LinkedIn'` and `isAvailable: false`. Verify a PrimeNG `p-tag` with "Coming Soon" severity warning is rendered, and the stat rows are hidden or dimmed.

8. **Shows "Data unavailable" for any non-LinkedIn platform with isAvailable=false.** Provide a summary with `isAvailable: false` for a non-LinkedIn platform. Verify a "Data unavailable" badge is shown.

Test setup pattern:
```typescript
// Stub: configure TestBed with the component, provide mock input data via signal inputs
// Use ComponentFixture, query DOM elements with nativeElement.querySelectorAll
```

### WebsiteAnalyticsSectionComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts`

Tests to implement:

1. **Renders overview metric cards.** Provide a `WebsiteAnalyticsResponse` with `overview: { activeUsers: 1200, sessions: 3400, pageViews: 8900, avgSessionDuration: 142.5, bounceRate: 45.2, newUsers: 800 }`. Verify 6 metric values appear in the rendered output (active users, sessions, page views, avg duration formatted as "2m 22s", bounce rate as "45.2%", new users).

2. **Renders top pages table.** Provide 5 `PageViewEntry` items. Verify a PrimeNG table renders with columns: Page Path, Views, Users. Verify all 5 rows are present.

3. **Renders traffic sources table.** Provide 4 `TrafficSourceEntry` items. Verify a table with Channel, Sessions, Users columns and all 4 rows.

4. **Renders search queries table.** Provide 3 `SearchQueryEntry` items. Verify a table with Query, Clicks, Impressions, CTR, Position columns. Verify CTR is formatted as percentage.

5. **Shows skeleton placeholders when data is null.** Set the `websiteData` input to `null`. Verify `p-skeleton` elements are rendered instead of metric cards and tables.

6. **Handles empty arrays gracefully.** Provide a response with empty `topPages`, `trafficSources`, `searchQueries` arrays. Verify empty state messages appear (not broken tables).

### SubstackSectionComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts`

Tests to implement:

1. **Renders post list from RSS data.** Provide 3 `SubstackPost` objects. Verify 3 post entries are rendered with title, publish date, and summary.

2. **Post titles are clickable links.** Verify each post title is rendered as an anchor tag (`<a>`) with `href` set to the `url` field and `target="_blank"` for external opening.

3. **Publish dates are formatted as relative time.** Provide a post with `publishedAt: '2026-03-18T10:00:00Z'`. Verify the date renders in a human-readable format (e.g., "Mar 18, 2026" or relative like "6 days ago").

4. **Summary is displayed when present.** Provide a post with `summary: 'This is a post about AI agents.'`. Verify the summary text appears.

5. **Handles null summary gracefully.** Provide a post with `summary: null`. Verify no summary paragraph is rendered (not "null" text).

6. **Shows empty state when no posts.** Provide an empty array. Verify an empty state message like "No Substack posts found" is displayed.

7. **Substack branding.** Verify the section header includes the Substack icon (`pi pi-at`) and the brand color (`#ff6719`).

---

## Implementation Details

### PlatformHealthCardsComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.ts`

This is a standalone Angular 19 component that receives a readonly array of `PlatformSummary` as input and renders a grid of cards, one per platform.

**Input:** `platforms = input<readonly PlatformSummary[]>([]);`

**Template structure:**
- Outer container: CSS grid with responsive columns (5 at desktop, 3 at tablet, 1 at mobile) matching the mockup's `.platform-grid` layout
- Each card uses a `div` with `class="platform-card"` and a CSS custom property `--platform-color` set dynamically from `PLATFORM_COLORS[summary.platform]`
- Top 3px colored border via `::before` pseudo-element (CSS, using `var(--platform-color)`)
- Platform icon rendered in a small badge box using `PLATFORM_ICONS[summary.platform]`
- Platform name from `PLATFORM_LABELS[summary.platform]`
- Three stat rows: Followers (or "Karma" for Reddit, "Subscribers" for YouTube), Posts (or "Videos" for YouTube), Avg Engagement (or "Avg Views" for YouTube, "Avg Score" for Reddit)
- Bottom section: top post title, truncated with ellipsis if too long
- When `isAvailable === false`: overlay the card content with a `p-tag` badge ("Coming Soon" for LinkedIn, "Data unavailable" for others). Dim the stat rows with reduced opacity.

**Label customization per platform:** Use a helper function or computed map:
```typescript
// Stub: followerLabel(platform: string): string
// Returns 'Subscribers' for YouTube, 'Karma' for Reddit, 'Followers' for others

// Stub: engagementLabel(platform: string): string
// Returns 'Avg Views' for YouTube, 'Avg Score' for Reddit, 'Avg Eng.' for others
```

**Imports:** `CommonModule`, `Tag` (from `primeng/tag`), `DecimalPipe` or Angular `number` pipe for formatting. Use `PLATFORM_COLORS`, `PLATFORM_ICONS`, `PLATFORM_LABELS` from `shared/utils/platform-icons`.

**Styles:** Inline component styles matching the mockup's `.platform-card` CSS. Key rules:
- `position: relative; overflow: hidden;` on the card
- `::before` pseudo-element for the 3px top colored bar
- `.p-stat` rows as flex with space-between
- `.p-best` section with a top border separator
- Hover: `translateY(-1px)` subtle lift
- Responsive: use `:host { display: block; }` and let the parent grid control layout

### WebsiteAnalyticsSectionComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.ts`

Standalone component that receives `WebsiteAnalyticsResponse | null` as input and renders GA4 + Search Console data.

**Input:** `data = input<WebsiteAnalyticsResponse | null>(null);`

**Template structure:**

1. **Section header:** "Website Analytics" with `pi pi-globe` icon and the purple brand color (`#8b5cf6`)

2. **Overview metric cards row** (when `data()` is not null):
   - 6 small KPI-style cards in a responsive grid (3x2 or 6x1 depending on viewport)
   - Active Users, Sessions, Page Views, Avg Duration (formatted as `Xm Ys`), Bounce Rate (as `X.X%`), New Users
   - Each card: label (uppercase, small, muted) + large bold value
   - Use `p-skeleton` (PrimeNG Skeleton) when `data()` is null

3. **Top Pages table:**
   - PrimeNG `p-table` with columns: Page Path, Views, Users
   - `[value]` bound to `mutableTopPages()` (computed spreading `data().topPages` for PrimeNG mutability)
   - Max 10 rows, sorted by views descending (server-side, but add `[sortField]="'views'"` client-side too)
   - Page path column: truncate long paths, show as monospace font

4. **Two-column grid below the top pages table:**
   - Left: **Traffic Sources** table (Channel, Sessions, Users)
   - Right: **Search Queries** table (Query, Clicks, Impressions, CTR as %, Position rounded to 1 decimal)

5. **Empty/loading states:**
   - When `data()` is null: show `p-skeleton` rectangles matching the layout structure
   - When arrays are empty: show small "No data for this period" text

**Duration formatting helper:**
```typescript
// Stub: formatDuration(seconds: number): string
// e.g., 142.5 => '2m 22s', 45 => '0m 45s', 3661 => '61m 1s'
```

**Imports:** `CommonModule`, `TableModule` (primeng/table), `Card` (primeng/card), `Skeleton` (primeng/skeleton), `DecimalPipe`, `PercentPipe`.

### SubstackSectionComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.ts`

Standalone component that receives a readonly array of `SubstackPost` and renders them as a post listing.

**Input:** `posts = input<readonly SubstackPost[]>([]);`

**Template structure:**

1. **Section header:** "Substack" with `pi pi-at` icon and the Substack brand color (`#ff6719`). Include a small external link icon that opens the Substack page in a new tab.

2. **Post list:** Rendered as a vertical list (not a table). Each post entry includes:
   - **Title** as a clickable link (`<a [href]="post.url" target="_blank" rel="noopener noreferrer">`) styled with hover underline
   - **Publish date** formatted using Angular's `DatePipe` with format `'mediumDate'` (e.g., "Mar 18, 2026")
   - **Summary** paragraph (if not null), styled in muted text color, truncated to ~2 lines with CSS `line-clamp: 2`
   - Separator between entries (border-bottom on each post except the last)

3. **Empty state:** When `posts()` is empty, show an `EmptyStateComponent` or inline empty message: "No Substack posts found" with a `pi pi-at` icon.

4. **Visual style:**
   - Wrap in a PrimeNG `p-card` with a subtle left-side accent border in Substack orange
   - Post titles: 0.9rem, font-weight 600, primary text color
   - Dates: 0.75rem, muted color, displayed next to or below the title
   - Summaries: 0.8rem, dim color, max 2 lines

**Imports:** `CommonModule`, `Card` (primeng/card), `DatePipe`. Optionally `EmptyStateComponent` from shared.

### Integrating into the Dashboard Page

The main `AnalyticsDashboardComponent` (from section 09) needs to import and include these three components in its template. The relevant template additions look like:

```html
<!-- Platform Health Cards (below charts row) -->
<app-platform-health-cards [platforms]="store.platformSummaries()" />

<!-- Two-column bottom layout: Website + Substack -->
<div class="grid mt-3">
  <div class="col-12 lg:col-9">
    <app-website-analytics-section [data]="store.websiteData()" />
  </div>
  <div class="col-12 lg:col-3">
    <app-substack-section [posts]="store.substackPosts()" />
  </div>
</div>
```

The dashboard component's `imports` array must add `PlatformHealthCardsComponent`, `WebsiteAnalyticsSectionComponent`, and `SubstackSectionComponent`.

---

## File Paths Summary

### New files to create:
| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.ts` | Platform health card grid |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts` | Tests for platform health cards |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.ts` | GA4/Search Console detail section |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts` | Tests for website analytics section |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.ts` | Substack RSS post listing |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts` | Tests for Substack section |

### Files to modify:
| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts` | Add imports for the three new components and include them in template |

---

## Platform Data Availability Reference

This table drives the conditional rendering logic in `PlatformHealthCardsComponent`:

| Platform | Followers Label | Engagement Label | isAvailable | Notes |
|----------|----------------|------------------|-------------|-------|
| TwitterX | Followers | Avg Eng. | true | Full metrics |
| LinkedIn | Followers | Avg Eng. | false | Shows "Coming Soon" |
| YouTube | Subscribers | Avg Views | true | Views instead of engagement |
| Instagram | Followers | Avg Eng. | true | Full metrics |
| Reddit | Karma | Avg Score | true | No impressions, karma only |

---

## TypeScript Interfaces Required (from section 07)

These must already exist in `features/analytics/models/dashboard.model.ts`:

```typescript
interface PlatformSummary {
  readonly platform: string;
  readonly followerCount: number | null;
  readonly postCount: number;
  readonly avgEngagement: number;
  readonly topPostTitle: string | null;
  readonly topPostUrl: string | null;
  readonly isAvailable: boolean;
}

interface WebsiteAnalyticsResponse {
  readonly overview: WebsiteOverview;
  readonly topPages: readonly PageViewEntry[];
  readonly trafficSources: readonly TrafficSourceEntry[];
  readonly searchQueries: readonly SearchQueryEntry[];
}

interface WebsiteOverview {
  readonly activeUsers: number;
  readonly sessions: number;
  readonly pageViews: number;
  readonly avgSessionDuration: number;
  readonly bounceRate: number;
  readonly newUsers: number;
}

interface PageViewEntry {
  readonly pagePath: string;
  readonly views: number;
  readonly users: number;
}

interface TrafficSourceEntry {
  readonly channel: string;
  readonly sessions: number;
  readonly users: number;
}

interface SearchQueryEntry {
  readonly query: string;
  readonly clicks: number;
  readonly impressions: number;
  readonly ctr: number;
  readonly position: number;
}

interface SubstackPost {
  readonly title: string;
  readonly url: string;
  readonly publishedAt: string;
  readonly summary: string | null;
}
```

---

## Shared Utilities Used

From `src/PersonalBrandAssistant.Web/src/app/shared/utils/platform-icons.ts`:
- `PLATFORM_COLORS` -- maps `PlatformType` to hex color strings (e.g., `TwitterX: '#1DA1F2'`)
- `PLATFORM_ICONS` -- maps `PlatformType` to PrimeNG icon classes (e.g., `TwitterX: 'pi pi-twitter'`)
- `PLATFORM_LABELS` -- maps `PlatformType` to display names (e.g., `TwitterX: 'Twitter/X'`)

---

## Responsive Grid Behavior

The mockup defines responsive breakpoints for the platform grid:
- **Desktop (>1200px):** 5 columns (`repeat(5, 1fr)`)
- **Tablet (900-1200px):** 3 columns
- **Small tablet (600-900px):** 2 columns
- **Mobile (<600px):** 1 column

Used custom CSS grid with `@media` breakpoints (matching the component pattern in the codebase) rather than PrimeFlex grid classes.

---

## Implementation Notes

- **Code review deviation:** Refactored `PlatformHealthCardsComponent` from template method calls (`getColor()`, `getIcon()`, etc.) to a single `readonly cards = computed(...)` signal. Matches the established pattern in `dashboard-kpi-cards.component.ts`.
- **formatDuration fix:** Changed from `Math.round(seconds % 60)` to `Math.round(total seconds)` then split, preventing "1m 60s" at boundary values.
- **ARIA accessibility:** Added `role="group"`, `role="region"`, `aria-label`, and `aria-hidden` attributes across all three components for screen reader support.
- **Dashboard layout:** Platform health cards placed between charts row and top content table. Website + Substack sections in a 3:1 grid below the content table.
- **Test count:** 21 tests (8 platform cards + 6 website analytics + 7 substack). All passing.