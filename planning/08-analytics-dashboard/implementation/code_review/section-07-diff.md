diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/models/dashboard.model.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/models/dashboard.model.ts
new file mode 100644
index 0000000..b7d4f69
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/models/dashboard.model.ts
@@ -0,0 +1,102 @@
+import { HttpParams } from '@angular/common/http';
+
+export type DashboardPeriod = '1d' | '7d' | '14d' | '30d' | '90d' | { readonly from: string; readonly to: string };
+
+export interface DashboardSummary {
+  readonly totalEngagement: number;
+  readonly previousEngagement: number;
+  readonly totalImpressions: number;
+  readonly previousImpressions: number;
+  readonly engagementRate: number;
+  readonly previousEngagementRate: number;
+  readonly contentPublished: number;
+  readonly previousContentPublished: number;
+  readonly costPerEngagement: number;
+  readonly previousCostPerEngagement: number;
+  readonly websiteUsers: number;
+  readonly previousWebsiteUsers: number;
+  readonly generatedAt: string;
+}
+
+export interface PlatformDailyMetrics {
+  readonly platform: string;
+  readonly likes: number;
+  readonly comments: number;
+  readonly shares: number;
+  readonly total: number;
+}
+
+export interface DailyEngagement {
+  readonly date: string;
+  readonly platforms: readonly PlatformDailyMetrics[];
+  readonly total: number;
+}
+
+export interface PlatformSummary {
+  readonly platform: string;
+  readonly followerCount: number | null;
+  readonly postCount: number;
+  readonly avgEngagement: number;
+  readonly topPostTitle: string | null;
+  readonly topPostUrl: string | null;
+  readonly isAvailable: boolean;
+}
+
+export interface WebsiteOverview {
+  readonly activeUsers: number;
+  readonly sessions: number;
+  readonly pageViews: number;
+  readonly avgSessionDuration: number;
+  readonly bounceRate: number;
+  readonly newUsers: number;
+}
+
+export interface PageViewEntry {
+  readonly pagePath: string;
+  readonly views: number;
+  readonly users: number;
+}
+
+export interface TrafficSourceEntry {
+  readonly channel: string;
+  readonly sessions: number;
+  readonly users: number;
+}
+
+export interface SearchQueryEntry {
+  readonly query: string;
+  readonly clicks: number;
+  readonly impressions: number;
+  readonly ctr: number;
+  readonly position: number;
+}
+
+export interface WebsiteAnalyticsResponse {
+  readonly overview: WebsiteOverview;
+  readonly topPages: readonly PageViewEntry[];
+  readonly trafficSources: readonly TrafficSourceEntry[];
+  readonly searchQueries: readonly SearchQueryEntry[];
+}
+
+export interface SubstackPost {
+  readonly title: string;
+  readonly url: string;
+  readonly publishedAt: string;
+  readonly summary: string | null;
+}
+
+export function periodToParams(period: DashboardPeriod, refresh = false): HttpParams {
+  let params = new HttpParams();
+
+  if (typeof period === 'string') {
+    params = params.set('period', period);
+  } else {
+    params = params.set('from', period.from).set('to', period.to);
+  }
+
+  if (refresh) {
+    params = params.set('refresh', 'true');
+  }
+
+  return params;
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts
new file mode 100644
index 0000000..5aa4659
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts
@@ -0,0 +1,180 @@
+import { TestBed } from '@angular/core/testing';
+import { provideHttpClient } from '@angular/common/http';
+import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
+import { AnalyticsService } from './analytics.service';
+import {
+  DashboardSummary,
+  DailyEngagement,
+  PlatformSummary,
+  WebsiteAnalyticsResponse,
+  SubstackPost,
+} from '../models/dashboard.model';
+
+describe('AnalyticsService', () => {
+  let service: AnalyticsService;
+  let httpMock: HttpTestingController;
+  const baseUrl = 'http://localhost:5000/api';
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      providers: [provideHttpClient(), provideHttpClientTesting()],
+    });
+    service = TestBed.inject(AnalyticsService);
+    httpMock = TestBed.inject(HttpTestingController);
+  });
+
+  afterEach(() => {
+    httpMock.verify();
+  });
+
+  describe('getDashboardSummary', () => {
+    const mockSummary: DashboardSummary = {
+      totalEngagement: 500,
+      previousEngagement: 400,
+      totalImpressions: 10000,
+      previousImpressions: 8000,
+      engagementRate: 5.0,
+      previousEngagementRate: 5.0,
+      contentPublished: 10,
+      previousContentPublished: 8,
+      costPerEngagement: 0.02,
+      previousCostPerEngagement: 0.03,
+      websiteUsers: 1200,
+      previousWebsiteUsers: 1000,
+      generatedAt: '2026-03-25T00:00:00Z',
+    };
+
+    it('should call GET analytics/dashboard with period param', () => {
+      service.getDashboardSummary('7d').subscribe((result) => {
+        expect(result).toEqual(mockSummary);
+      });
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?period=7d`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockSummary);
+    });
+
+    it('should call GET analytics/dashboard with custom date range', () => {
+      const range = { from: '2026-03-01', to: '2026-03-25' };
+      service.getDashboardSummary(range).subscribe((result) => {
+        expect(result).toEqual(mockSummary);
+      });
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?from=2026-03-01&to=2026-03-25`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockSummary);
+    });
+
+    it('should append refresh=true when refresh flag is set', () => {
+      service.getDashboardSummary('7d', true).subscribe();
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?period=7d&refresh=true`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockSummary);
+    });
+  });
+
+  describe('getEngagementTimeline', () => {
+    const mockTimeline: DailyEngagement[] = [
+      {
+        date: '2026-03-24',
+        platforms: [{ platform: 'LinkedIn', likes: 10, comments: 5, shares: 2, total: 17 }],
+        total: 17,
+      },
+    ];
+
+    it('should call GET analytics/engagement-timeline with period param', () => {
+      service.getEngagementTimeline('30d').subscribe((result) => {
+        expect(result).toEqual(mockTimeline);
+      });
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/engagement-timeline?period=30d`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockTimeline);
+    });
+
+    it('should pass custom from/to when DashboardPeriod is a DateRange', () => {
+      const range = { from: '2026-03-01', to: '2026-03-25' };
+      service.getEngagementTimeline(range).subscribe((result) => {
+        expect(result).toEqual(mockTimeline);
+      });
+
+      const req = httpMock.expectOne(
+        `${baseUrl}/analytics/engagement-timeline?from=2026-03-01&to=2026-03-25`
+      );
+      expect(req.request.method).toBe('GET');
+      req.flush(mockTimeline);
+    });
+  });
+
+  describe('getPlatformSummaries', () => {
+    const mockSummaries: PlatformSummary[] = [
+      {
+        platform: 'LinkedIn',
+        followerCount: 5000,
+        postCount: 15,
+        avgEngagement: 42.5,
+        topPostTitle: 'AI in Enterprise',
+        topPostUrl: 'https://linkedin.com/post/123',
+        isAvailable: true,
+      },
+    ];
+
+    it('should call GET analytics/platform-summary with period param', () => {
+      service.getPlatformSummaries('30d').subscribe((result) => {
+        expect(result).toEqual(mockSummaries);
+      });
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/platform-summary?period=30d`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockSummaries);
+    });
+  });
+
+  describe('getWebsiteAnalytics', () => {
+    const mockWebsite: WebsiteAnalyticsResponse = {
+      overview: {
+        activeUsers: 500,
+        sessions: 800,
+        pageViews: 2000,
+        avgSessionDuration: 120.5,
+        bounceRate: 45.2,
+        newUsers: 300,
+      },
+      topPages: [{ pagePath: '/blog/ai-agents', views: 500, users: 300 }],
+      trafficSources: [{ channel: 'Organic Search', sessions: 400, users: 350 }],
+      searchQueries: [{ query: 'ai agents enterprise', clicks: 50, impressions: 1000, ctr: 5.0, position: 3.2 }],
+    };
+
+    it('should call GET analytics/website with period param', () => {
+      service.getWebsiteAnalytics('30d').subscribe((result) => {
+        expect(result).toEqual(mockWebsite);
+      });
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/website?period=30d`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockWebsite);
+    });
+  });
+
+  describe('getSubstackPosts', () => {
+    const mockPosts: SubstackPost[] = [
+      {
+        title: 'Building AI Agents',
+        url: 'https://matthewkruczek.substack.com/p/building-ai-agents',
+        publishedAt: '2026-03-20T10:00:00Z',
+        summary: 'A deep dive into agent architectures.',
+      },
+    ];
+
+    it('should call GET analytics/substack with no params', () => {
+      service.getSubstackPosts().subscribe((result) => {
+        expect(result).toEqual(mockPosts);
+      });
+
+      const req = httpMock.expectOne(`${baseUrl}/analytics/substack`);
+      expect(req.request.method).toBe('GET');
+      req.flush(mockPosts);
+    });
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts
index b086e78..c8b0ecd 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts
@@ -3,6 +3,15 @@ import { HttpParams } from '@angular/common/http';
 import { Observable } from 'rxjs';
 import { ApiService } from '../../../core/services/api.service';
 import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
+import {
+  DashboardPeriod,
+  DashboardSummary,
+  DailyEngagement,
+  PlatformSummary,
+  WebsiteAnalyticsResponse,
+  SubstackPost,
+  periodToParams,
+} from '../models/dashboard.model';
 
 @Injectable({ providedIn: 'root' })
 export class AnalyticsService {
@@ -23,4 +32,24 @@ export class AnalyticsService {
   refreshAnalytics(contentId: string): Observable<void> {
     return this.api.post<void>(`analytics/content/${contentId}/refresh`, {});
   }
+
+  getDashboardSummary(period: DashboardPeriod, refresh = false): Observable<DashboardSummary> {
+    return this.api.get<DashboardSummary>('analytics/dashboard', periodToParams(period, refresh));
+  }
+
+  getEngagementTimeline(period: DashboardPeriod, refresh = false): Observable<DailyEngagement[]> {
+    return this.api.get<DailyEngagement[]>('analytics/engagement-timeline', periodToParams(period, refresh));
+  }
+
+  getPlatformSummaries(period: DashboardPeriod, refresh = false): Observable<PlatformSummary[]> {
+    return this.api.get<PlatformSummary[]>('analytics/platform-summary', periodToParams(period, refresh));
+  }
+
+  getWebsiteAnalytics(period: DashboardPeriod, refresh = false): Observable<WebsiteAnalyticsResponse> {
+    return this.api.get<WebsiteAnalyticsResponse>('analytics/website', periodToParams(period, refresh));
+  }
+
+  getSubstackPosts(): Observable<SubstackPost[]> {
+    return this.api.get<SubstackPost[]>('analytics/substack');
+  }
 }
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts b/src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts
index 2746d5e..946b341 100644
--- a/src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts
@@ -28,4 +28,6 @@ export interface TopPerformingContent {
   readonly totalEngagement: number;
   readonly platforms: readonly PlatformType[];
   readonly publishedAt?: string;
+  readonly impressions?: number;
+  readonly engagementRate?: number;
 }
