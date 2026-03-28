diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
index 25e4fb8..d1a1438 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
@@ -40,11 +40,11 @@ export class AnalyticsDashboardComponent implements OnInit {
   readonly store = inject(AnalyticsStore);
 
   ngOnInit() {
-    this.store.loadTopContent(this.store.dateRange());
+    this.store.loadDashboard();
   }
 
   onRangeChanged(range: { from: string; to: string }) {
-    this.store.loadTopContent(range);
+    this.store.setPeriod(range);
   }
 
   viewDetail(contentId: string) {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts
new file mode 100644
index 0000000..939aab7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts
@@ -0,0 +1,213 @@
+import { TestBed, fakeAsync, tick } from '@angular/core/testing';
+import { of, throwError } from 'rxjs';
+import { AnalyticsStore } from './analytics.store';
+import { AnalyticsService } from '../services/analytics.service';
+import {
+  DashboardSummary,
+  DailyEngagement,
+  PlatformSummary,
+  WebsiteAnalyticsResponse,
+  SubstackPost,
+} from '../models/dashboard.model';
+import { TopPerformingContent } from '../../../shared/models';
+
+describe('AnalyticsStore', () => {
+  let store: InstanceType<typeof AnalyticsStore>;
+  let mockService: jasmine.SpyObj<AnalyticsService>;
+
+  const mockSummary: DashboardSummary = {
+    totalEngagement: 150,
+    previousEngagement: 100,
+    totalImpressions: 10000,
+    previousImpressions: 8000,
+    engagementRate: 1.5,
+    previousEngagementRate: 1.25,
+    contentPublished: 12,
+    previousContentPublished: 10,
+    costPerEngagement: 0.05,
+    previousCostPerEngagement: 0.04,
+    websiteUsers: 1200,
+    previousWebsiteUsers: 1000,
+    generatedAt: '2026-03-25T00:00:00Z',
+  };
+
+  const mockTimeline: DailyEngagement[] = [
+    {
+      date: '2026-03-24',
+      platforms: [{ platform: 'LinkedIn', likes: 10, comments: 5, shares: 2, total: 17 }],
+      total: 17,
+    },
+  ];
+
+  const mockPlatforms: PlatformSummary[] = [
+    {
+      platform: 'LinkedIn',
+      followerCount: 5000,
+      postCount: 15,
+      avgEngagement: 42.5,
+      topPostTitle: 'AI Agents',
+      topPostUrl: 'https://linkedin.com/post/1',
+      isAvailable: true,
+    },
+  ];
+
+  const mockWebsite: WebsiteAnalyticsResponse = {
+    overview: { activeUsers: 500, sessions: 800, pageViews: 2000, avgSessionDuration: 120, bounceRate: 45, newUsers: 300 },
+    topPages: [{ pagePath: '/blog', views: 500, users: 300 }],
+    trafficSources: [{ channel: 'Organic', sessions: 400, users: 350 }],
+    searchQueries: [{ query: 'ai agents', clicks: 50, impressions: 1000, ctr: 5, position: 3 }],
+  };
+
+  const mockSubstack: SubstackPost[] = [
+    { title: 'AI Post', url: 'https://matthewkruczek.substack.com/p/ai', publishedAt: '2026-03-20T10:00:00Z', summary: null },
+  ];
+
+  const mockTopContent: TopPerformingContent[] = [
+    { contentId: '1', title: 'Test', contentType: 'BlogPost', totalEngagement: 100, platforms: ['LinkedIn'], publishedAt: '2026-03-01T00:00:00Z' },
+  ];
+
+  beforeEach(() => {
+    mockService = jasmine.createSpyObj('AnalyticsService', [
+      'getDashboardSummary', 'getEngagementTimeline', 'getPlatformSummaries',
+      'getWebsiteAnalytics', 'getSubstackPosts', 'getTopContent',
+      'getContentReport', 'refreshAnalytics',
+    ]);
+
+    mockService.getDashboardSummary.and.returnValue(of(mockSummary));
+    mockService.getEngagementTimeline.and.returnValue(of(mockTimeline));
+    mockService.getPlatformSummaries.and.returnValue(of(mockPlatforms));
+    mockService.getWebsiteAnalytics.and.returnValue(of(mockWebsite));
+    mockService.getSubstackPosts.and.returnValue(of(mockSubstack));
+    mockService.getTopContent.and.returnValue(of(mockTopContent));
+
+    TestBed.configureTestingModule({
+      providers: [{ provide: AnalyticsService, useValue: mockService }],
+    });
+    store = TestBed.inject(AnalyticsStore);
+  });
+
+  describe('loadDashboard', () => {
+    it('should call all 6 service methods and populate state', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(mockService.getDashboardSummary).toHaveBeenCalledTimes(1);
+      expect(mockService.getEngagementTimeline).toHaveBeenCalledTimes(1);
+      expect(mockService.getPlatformSummaries).toHaveBeenCalledTimes(1);
+      expect(mockService.getWebsiteAnalytics).toHaveBeenCalledTimes(1);
+      expect(mockService.getSubstackPosts).toHaveBeenCalledTimes(1);
+      expect(mockService.getTopContent).toHaveBeenCalledTimes(1);
+
+      expect(store.summary()).toEqual(mockSummary);
+      expect(store.timeline()).toEqual(mockTimeline);
+      expect(store.platformSummaries()).toEqual(mockPlatforms);
+      expect(store.websiteData()).toEqual(mockWebsite);
+      expect(store.substackPosts()).toEqual(mockSubstack);
+      expect(store.topContent()).toEqual(mockTopContent);
+    }));
+
+    it('should set loading false after all complete', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(store.loading()).toBe(false);
+    }));
+
+    it('should pass current period to service methods', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(mockService.getDashboardSummary).toHaveBeenCalledWith('30d', false);
+      expect(mockService.getEngagementTimeline).toHaveBeenCalledWith('30d', false);
+      expect(mockService.getPlatformSummaries).toHaveBeenCalledWith('30d', false);
+      expect(mockService.getWebsiteAnalytics).toHaveBeenCalledWith('30d', false);
+    }));
+
+    it('should handle partial API failure gracefully', fakeAsync(() => {
+      mockService.getDashboardSummary.and.returnValue(throwError(() => new Error('API error')));
+
+      store.loadDashboard();
+      tick();
+
+      expect(store.summary()).toBeNull();
+      expect(store.timeline()).toEqual(mockTimeline);
+      expect(store.platformSummaries()).toEqual(mockPlatforms);
+      expect(store.errors().summary).toBeTruthy();
+      expect(store.errors().timeline).toBeNull();
+      expect(store.loading()).toBe(false);
+    }));
+
+    it('should update lastRefreshedAt after load', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(store.lastRefreshedAt()).toBeTruthy();
+    }));
+  });
+
+  describe('refreshDashboard', () => {
+    it('should pass refresh=true to all service methods', fakeAsync(() => {
+      store.refreshDashboard();
+      tick();
+
+      expect(mockService.getDashboardSummary).toHaveBeenCalledWith('30d', true);
+      expect(mockService.getEngagementTimeline).toHaveBeenCalledWith('30d', true);
+      expect(mockService.getPlatformSummaries).toHaveBeenCalledWith('30d', true);
+      expect(mockService.getWebsiteAnalytics).toHaveBeenCalledWith('30d', true);
+    }));
+  });
+
+  describe('setPeriod', () => {
+    it('should update period state and trigger reload', fakeAsync(() => {
+      store.setPeriod('14d');
+      tick();
+
+      expect(store.period()).toBe('14d');
+      expect(mockService.getDashboardSummary).toHaveBeenCalledWith('14d', false);
+    }));
+
+    it('should accept custom date range object', fakeAsync(() => {
+      const range = { from: '2026-01-01', to: '2026-01-31' };
+      store.setPeriod(range);
+      tick();
+
+      expect(store.period()).toEqual(range);
+      expect(mockService.getDashboardSummary).toHaveBeenCalledWith(range, false);
+    }));
+  });
+
+  describe('computed signals', () => {
+    it('should compute engagementChange as correct percentage', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(store.engagementChange()).toBe(50);
+    }));
+
+    it('should return null engagementChange when previousEngagement is 0', fakeAsync(() => {
+      mockService.getDashboardSummary.and.returnValue(of({ ...mockSummary, previousEngagement: 0 }));
+      store.loadDashboard();
+      tick();
+
+      expect(store.engagementChange()).toBeNull();
+    }));
+
+    it('should compute impressionsChange as correct percentage', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(store.impressionsChange()).toBe(25);
+    }));
+
+    it('should return true for isStale when lastRefreshedAt is null', () => {
+      expect(store.isStale()).toBe(true);
+    });
+
+    it('should return false for isStale when recently refreshed', fakeAsync(() => {
+      store.loadDashboard();
+      tick();
+
+      expect(store.isStale()).toBe(false);
+    }));
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.ts
index b70123d..5749e9c 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.ts
@@ -1,51 +1,190 @@
-import { inject } from '@angular/core';
-import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
+import { computed, inject } from '@angular/core';
+import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
 import { rxMethod } from '@ngrx/signals/rxjs-interop';
-import { pipe, switchMap, tap } from 'rxjs';
+import { pipe, switchMap, tap, forkJoin, of, catchError, map, Observable } from 'rxjs';
 import { tapResponse } from '@ngrx/operators';
-import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
 import { AnalyticsService } from '../services/analytics.service';
+import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
+import {
+  DashboardSummary,
+  DailyEngagement,
+  PlatformSummary,
+  WebsiteAnalyticsResponse,
+  SubstackPost,
+  DashboardPeriod,
+} from '../models/dashboard.model';
+
+interface DataOrError<T> {
+  readonly data: T | null;
+  readonly error: string | null;
+}
 
-interface DateRange {
-  readonly from: string;
-  readonly to: string;
+interface DashboardErrors {
+  readonly summary: string | null;
+  readonly timeline: string | null;
+  readonly platforms: string | null;
+  readonly website: string | null;
+  readonly substack: string | null;
+  readonly topContent: string | null;
 }
 
-interface AnalyticsState {
+interface AnalyticsDashboardState {
+  readonly summary: DashboardSummary | null;
+  readonly timeline: readonly DailyEngagement[];
+  readonly platformSummaries: readonly PlatformSummary[];
+  readonly websiteData: WebsiteAnalyticsResponse | null;
+  readonly substackPosts: readonly SubstackPost[];
   readonly topContent: readonly TopPerformingContent[];
   readonly selectedReport: ContentPerformanceReport | undefined;
-  readonly dateRange: DateRange;
+  readonly period: DashboardPeriod;
   readonly loading: boolean;
+  readonly lastRefreshedAt: string | null;
+  readonly errors: DashboardErrors;
 }
 
-function defaultDateRange(): DateRange {
-  const to = new Date();
-  const from = new Date(to.getTime() - 30 * 86_400_000);
-  return { from: from.toISOString(), to: to.toISOString() };
-}
+const initialErrors: DashboardErrors = {
+  summary: null,
+  timeline: null,
+  platforms: null,
+  website: null,
+  substack: null,
+  topContent: null,
+};
 
-const initialState: AnalyticsState = {
+const initialState: AnalyticsDashboardState = {
+  summary: null,
+  timeline: [],
+  platformSummaries: [],
+  websiteData: null,
+  substackPosts: [],
   topContent: [],
   selectedReport: undefined,
-  dateRange: defaultDateRange(),
+  period: '30d',
   loading: false,
+  lastRefreshedAt: null,
+  errors: initialErrors,
 };
 
+function wrapCatchError<T>(obs: Observable<T>): Observable<DataOrError<T>> {
+  return obs.pipe(
+    map(data => ({ data, error: null as string | null })),
+    catchError(err => of({ data: null as T | null, error: err?.message ?? 'Unknown error' })),
+  );
+}
+
+function periodToDateRange(period: DashboardPeriod): { from: string; to: string } {
+  if (typeof period !== 'string') {
+    return period;
+  }
+  const days = parseInt(period.replace('d', ''), 10);
+  const to = new Date();
+  const from = new Date(to.getTime() - days * 86_400_000);
+  return { from: from.toISOString(), to: to.toISOString() };
+}
+
+function fetchDashboard(
+  analyticsService: AnalyticsService,
+  period: DashboardPeriod,
+  refresh: boolean,
+) {
+  const range = periodToDateRange(period);
+  return forkJoin({
+    summary: wrapCatchError(analyticsService.getDashboardSummary(period, refresh)),
+    timeline: wrapCatchError(analyticsService.getEngagementTimeline(period, refresh)),
+    platforms: wrapCatchError(analyticsService.getPlatformSummaries(period, refresh)),
+    website: wrapCatchError(analyticsService.getWebsiteAnalytics(period, refresh)),
+    substack: wrapCatchError(analyticsService.getSubstackPosts()),
+    topContent: wrapCatchError(analyticsService.getTopContent(range.from, range.to)),
+  });
+}
+
+function percentChange(current: number, previous: number): number | null {
+  if (previous === 0) return null;
+  return ((current - previous) / previous) * 100;
+}
+
 export const AnalyticsStore = signalStore(
   { providedIn: 'root' },
   withState(initialState),
+  withComputed((store) => ({
+    engagementChange: computed(() => {
+      const s = store.summary();
+      return s ? percentChange(s.totalEngagement, s.previousEngagement) : null;
+    }),
+    impressionsChange: computed(() => {
+      const s = store.summary();
+      return s ? percentChange(s.totalImpressions, s.previousImpressions) : null;
+    }),
+    engagementRateChange: computed(() => {
+      const s = store.summary();
+      return s ? s.engagementRate - s.previousEngagementRate : null;
+    }),
+    contentPublishedChange: computed(() => {
+      const s = store.summary();
+      return s ? percentChange(s.contentPublished, s.previousContentPublished) : null;
+    }),
+    websiteUsersChange: computed(() => {
+      const s = store.summary();
+      return s ? percentChange(s.websiteUsers, s.previousWebsiteUsers) : null;
+    }),
+    costPerEngagementChange: computed(() => {
+      const s = store.summary();
+      return s ? percentChange(s.costPerEngagement, s.previousCostPerEngagement) : null;
+    }),
+    isStale: computed(() => {
+      const ts = store.lastRefreshedAt();
+      if (!ts) return true;
+      return (Date.now() - new Date(ts).getTime()) > 30 * 60 * 1000;
+    }),
+  })),
   withMethods((store, analyticsService = inject(AnalyticsService)) => ({
-    loadTopContent: rxMethod<DateRange>(
+    loadDashboard: rxMethod<void>(
       pipe(
-        tap(range => patchState(store, { loading: true, dateRange: range })),
-        switchMap(range =>
-          analyticsService.getTopContent(range.from, range.to).pipe(
-            tapResponse({
-              next: topContent => patchState(store, { topContent, loading: false }),
-              error: () => patchState(store, { loading: false }),
-            }),
-          ),
-        ),
+        tap(() => patchState(store, { loading: true, errors: initialErrors })),
+        switchMap(() => fetchDashboard(analyticsService, store.period(), false)),
+        tap(results => patchState(store, {
+          summary: results.summary.data,
+          timeline: results.timeline.data ?? [],
+          platformSummaries: results.platforms.data ?? [],
+          websiteData: results.website.data,
+          substackPosts: results.substack.data ?? [],
+          topContent: results.topContent.data ?? [],
+          loading: false,
+          lastRefreshedAt: new Date().toISOString(),
+          errors: {
+            summary: results.summary.error,
+            timeline: results.timeline.error,
+            platforms: results.platforms.error,
+            website: results.website.error,
+            substack: results.substack.error,
+            topContent: results.topContent.error,
+          },
+        })),
+      ),
+    ),
+
+    refreshDashboard: rxMethod<void>(
+      pipe(
+        tap(() => patchState(store, { loading: true, errors: initialErrors })),
+        switchMap(() => fetchDashboard(analyticsService, store.period(), true)),
+        tap(results => patchState(store, {
+          summary: results.summary.data,
+          timeline: results.timeline.data ?? [],
+          platformSummaries: results.platforms.data ?? [],
+          websiteData: results.website.data,
+          substackPosts: results.substack.data ?? [],
+          topContent: results.topContent.data ?? [],
+          loading: false,
+          lastRefreshedAt: new Date().toISOString(),
+          errors: {
+            summary: results.summary.error,
+            timeline: results.timeline.error,
+            platforms: results.platforms.error,
+            website: results.website.error,
+            substack: results.substack.error,
+            topContent: results.topContent.error,
+          },
+        })),
       ),
     ),
 
@@ -63,8 +202,9 @@ export const AnalyticsStore = signalStore(
       ),
     ),
 
-    setDateRange(range: DateRange) {
-      patchState(store, { dateRange: range });
+    setPeriod(period: DashboardPeriod) {
+      patchState(store, { period });
+      this.loadDashboard();
     },
   })),
 );
