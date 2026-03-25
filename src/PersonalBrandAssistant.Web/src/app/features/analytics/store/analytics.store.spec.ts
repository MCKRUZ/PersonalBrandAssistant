import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AnalyticsStore } from './analytics.store';
import { AnalyticsService } from '../services/analytics.service';
import {
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
} from '../models/dashboard.model';
import { TopPerformingContent } from '../../../shared/models';

describe('AnalyticsStore', () => {
  let store: InstanceType<typeof AnalyticsStore>;
  let mockService: jasmine.SpyObj<AnalyticsService>;

  const mockSummary: DashboardSummary = {
    totalEngagement: 150,
    previousEngagement: 100,
    totalImpressions: 10000,
    previousImpressions: 8000,
    engagementRate: 1.5,
    previousEngagementRate: 1.25,
    contentPublished: 12,
    previousContentPublished: 10,
    costPerEngagement: 0.05,
    previousCostPerEngagement: 0.04,
    websiteUsers: 1200,
    previousWebsiteUsers: 1000,
    generatedAt: '2026-03-25T00:00:00Z',
  };

  const mockTimeline: DailyEngagement[] = [
    {
      date: '2026-03-24',
      platforms: [{ platform: 'LinkedIn', likes: 10, comments: 5, shares: 2, total: 17 }],
      total: 17,
    },
  ];

  const mockPlatforms: PlatformSummary[] = [
    {
      platform: 'LinkedIn',
      followerCount: 5000,
      postCount: 15,
      avgEngagement: 42.5,
      topPostTitle: 'AI Agents',
      topPostUrl: 'https://linkedin.com/post/1',
      isAvailable: true,
    },
  ];

  const mockWebsite: WebsiteAnalyticsResponse = {
    overview: { activeUsers: 500, sessions: 800, pageViews: 2000, avgSessionDuration: 120, bounceRate: 45, newUsers: 300 },
    topPages: [{ pagePath: '/blog', views: 500, users: 300 }],
    trafficSources: [{ channel: 'Organic', sessions: 400, users: 350 }],
    searchQueries: [{ query: 'ai agents', clicks: 50, impressions: 1000, ctr: 5, position: 3 }],
  };

  const mockSubstack: SubstackPost[] = [
    { title: 'AI Post', url: 'https://matthewkruczek.substack.com/p/ai', publishedAt: '2026-03-20T10:00:00Z', summary: null },
  ];

  const mockTopContent: TopPerformingContent[] = [
    { contentId: '1', title: 'Test', contentType: 'BlogPost', totalEngagement: 100, platforms: ['LinkedIn'], publishedAt: '2026-03-01T00:00:00Z' },
  ];

  beforeEach(() => {
    mockService = jasmine.createSpyObj('AnalyticsService', [
      'getDashboardSummary', 'getEngagementTimeline', 'getPlatformSummaries',
      'getWebsiteAnalytics', 'getSubstackPosts', 'getTopContent',
      'getContentReport', 'refreshAnalytics',
    ]);

    mockService.getDashboardSummary.and.returnValue(of(mockSummary));
    mockService.getEngagementTimeline.and.returnValue(of(mockTimeline));
    mockService.getPlatformSummaries.and.returnValue(of(mockPlatforms));
    mockService.getWebsiteAnalytics.and.returnValue(of(mockWebsite));
    mockService.getSubstackPosts.and.returnValue(of(mockSubstack));
    mockService.getTopContent.and.returnValue(of(mockTopContent));

    TestBed.configureTestingModule({
      providers: [{ provide: AnalyticsService, useValue: mockService }],
    });
    store = TestBed.inject(AnalyticsStore);
  });

  describe('loadDashboard', () => {
    it('should call all 6 service methods and populate state', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(mockService.getDashboardSummary).toHaveBeenCalledTimes(1);
      expect(mockService.getEngagementTimeline).toHaveBeenCalledTimes(1);
      expect(mockService.getPlatformSummaries).toHaveBeenCalledTimes(1);
      expect(mockService.getWebsiteAnalytics).toHaveBeenCalledTimes(1);
      expect(mockService.getSubstackPosts).toHaveBeenCalledTimes(1);
      expect(mockService.getTopContent).toHaveBeenCalledTimes(1);

      expect(store.summary()).toEqual(mockSummary);
      expect(store.timeline()).toEqual(mockTimeline);
      expect(store.platformSummaries()).toEqual(mockPlatforms);
      expect(store.websiteData()).toEqual(mockWebsite);
      expect(store.substackPosts()).toEqual(mockSubstack);
      expect(store.topContent()).toEqual(mockTopContent);
    }));

    it('should set loading false after all complete', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(store.loading()).toBe(false);
    }));

    it('should pass current period to service methods', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(mockService.getDashboardSummary).toHaveBeenCalledWith('30d', false);
      expect(mockService.getEngagementTimeline).toHaveBeenCalledWith('30d', false);
      expect(mockService.getPlatformSummaries).toHaveBeenCalledWith('30d', false);
      expect(mockService.getWebsiteAnalytics).toHaveBeenCalledWith('30d', false);
    }));

    it('should handle partial API failure gracefully', fakeAsync(() => {
      mockService.getDashboardSummary.and.returnValue(throwError(() => new Error('API error')));

      store.loadDashboard();
      tick();

      expect(store.summary()).toBeNull();
      expect(store.timeline()).toEqual(mockTimeline);
      expect(store.platformSummaries()).toEqual(mockPlatforms);
      expect(store.errors().summary).toBeTruthy();
      expect(store.errors().timeline).toBeNull();
      expect(store.loading()).toBe(false);
    }));

    it('should update lastRefreshedAt after load', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(store.lastRefreshedAt()).toBeTruthy();
    }));
  });

  describe('refreshDashboard', () => {
    it('should pass refresh=true to all service methods', fakeAsync(() => {
      store.refreshDashboard();
      tick();

      expect(mockService.getDashboardSummary).toHaveBeenCalledWith('30d', true);
      expect(mockService.getEngagementTimeline).toHaveBeenCalledWith('30d', true);
      expect(mockService.getPlatformSummaries).toHaveBeenCalledWith('30d', true);
      expect(mockService.getWebsiteAnalytics).toHaveBeenCalledWith('30d', true);
    }));
  });

  describe('setPeriod', () => {
    it('should update period state and trigger reload', fakeAsync(() => {
      store.setPeriod('14d');
      tick();

      expect(store.period()).toBe('14d');
      expect(mockService.getDashboardSummary).toHaveBeenCalledWith('14d', false);
    }));

    it('should accept custom date range object', fakeAsync(() => {
      const range = { from: '2026-01-01', to: '2026-01-31' };
      store.setPeriod(range);
      tick();

      expect(store.period()).toEqual(range);
      expect(mockService.getDashboardSummary).toHaveBeenCalledWith(range, false);
    }));
  });

  describe('computed signals', () => {
    it('should compute engagementChange as correct percentage', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(store.engagementChange()).toBe(50);
    }));

    it('should return null engagementChange when previousEngagement is 0', fakeAsync(() => {
      mockService.getDashboardSummary.and.returnValue(of({ ...mockSummary, previousEngagement: 0 }));
      store.loadDashboard();
      tick();

      expect(store.engagementChange()).toBeNull();
    }));

    it('should compute impressionsChange as correct percentage', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(store.impressionsChange()).toBe(25);
    }));

    it('should return true for isStale when lastRefreshedAt is null', () => {
      expect(store.isStale()).toBe(true);
    });

    it('should return false for isStale when recently refreshed', fakeAsync(() => {
      store.loadDashboard();
      tick();

      expect(store.isStale()).toBe(false);
    }));
  });
});
