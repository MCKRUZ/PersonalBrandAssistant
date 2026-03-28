import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AnalyticsService } from './analytics.service';
import {
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
} from '../models/dashboard.model';

describe('AnalyticsService', () => {
  let service: AnalyticsService;
  let httpMock: HttpTestingController;
  const baseUrl = 'http://localhost:5000/api';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AnalyticsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('getDashboardSummary', () => {
    const mockSummary: DashboardSummary = {
      totalEngagement: 500,
      previousEngagement: 400,
      totalImpressions: 10000,
      previousImpressions: 8000,
      engagementRate: 5.0,
      previousEngagementRate: 5.0,
      contentPublished: 10,
      previousContentPublished: 8,
      costPerEngagement: 0.02,
      previousCostPerEngagement: 0.03,
      websiteUsers: 1200,
      previousWebsiteUsers: 1000,
      generatedAt: '2026-03-25T00:00:00Z',
    };

    it('should call GET analytics/dashboard with period param', () => {
      service.getDashboardSummary('7d').subscribe((result) => {
        expect(result).toEqual(mockSummary);
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?period=7d`);
      expect(req.request.method).toBe('GET');
      req.flush(mockSummary);
    });

    it('should call GET analytics/dashboard with custom date range', () => {
      const range = { from: '2026-03-01', to: '2026-03-25' };
      service.getDashboardSummary(range).subscribe((result) => {
        expect(result).toEqual(mockSummary);
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?from=2026-03-01&to=2026-03-25`);
      expect(req.request.method).toBe('GET');
      req.flush(mockSummary);
    });

    it('should append refresh=true when refresh flag is set', () => {
      service.getDashboardSummary('7d', true).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?period=7d&refresh=true`);
      expect(req.request.method).toBe('GET');
      req.flush(mockSummary);
    });

    it('should propagate HTTP errors', () => {
      service.getDashboardSummary('7d').subscribe({
        next: () => fail('should have errored'),
        error: (err) => expect(err.status).toBe(500),
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/dashboard?period=7d`);
      req.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
    });
  });

  describe('getEngagementTimeline', () => {
    const mockTimeline: DailyEngagement[] = [
      {
        date: '2026-03-24',
        platforms: [{ platform: 'LinkedIn', likes: 10, comments: 5, shares: 2, total: 17 }],
        total: 17,
      },
    ];

    it('should call GET analytics/engagement-timeline with period param', () => {
      service.getEngagementTimeline('30d').subscribe((result) => {
        expect(result).toEqual(mockTimeline);
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/engagement-timeline?period=30d`);
      expect(req.request.method).toBe('GET');
      req.flush(mockTimeline);
    });

    it('should pass custom from/to when DashboardPeriod is a DateRange', () => {
      const range = { from: '2026-03-01', to: '2026-03-25' };
      service.getEngagementTimeline(range).subscribe((result) => {
        expect(result).toEqual(mockTimeline);
      });

      const req = httpMock.expectOne(
        `${baseUrl}/analytics/engagement-timeline?from=2026-03-01&to=2026-03-25`
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockTimeline);
    });

    it('should append refresh=true when refresh flag is set', () => {
      service.getEngagementTimeline('30d', true).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/analytics/engagement-timeline?period=30d&refresh=true`);
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  describe('getPlatformSummaries', () => {
    const mockSummaries: PlatformSummary[] = [
      {
        platform: 'LinkedIn',
        followerCount: 5000,
        postCount: 15,
        avgEngagement: 42.5,
        topPostTitle: 'AI in Enterprise',
        topPostUrl: 'https://linkedin.com/post/123',
        isAvailable: true,
      },
    ];

    it('should call GET analytics/platform-summary with period param', () => {
      service.getPlatformSummaries('30d').subscribe((result) => {
        expect(result).toEqual(mockSummaries);
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/platform-summary?period=30d`);
      expect(req.request.method).toBe('GET');
      req.flush(mockSummaries);
    });
  });

  describe('getWebsiteAnalytics', () => {
    const mockWebsite: WebsiteAnalyticsResponse = {
      overview: {
        activeUsers: 500,
        sessions: 800,
        pageViews: 2000,
        avgSessionDuration: 120.5,
        bounceRate: 45.2,
        newUsers: 300,
      },
      topPages: [{ pagePath: '/blog/ai-agents', views: 500, users: 300 }],
      trafficSources: [{ channel: 'Organic Search', sessions: 400, users: 350 }],
      searchQueries: [{ query: 'ai agents enterprise', clicks: 50, impressions: 1000, ctr: 5.0, position: 3.2 }],
    };

    it('should call GET analytics/website with period param', () => {
      service.getWebsiteAnalytics('30d').subscribe((result) => {
        expect(result).toEqual(mockWebsite);
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/website?period=30d`);
      expect(req.request.method).toBe('GET');
      req.flush(mockWebsite);
    });
  });

  describe('getSubstackPosts', () => {
    const mockPosts: SubstackPost[] = [
      {
        title: 'Building AI Agents',
        url: 'https://matthewkruczek.substack.com/p/building-ai-agents',
        publishedAt: '2026-03-20T10:00:00Z',
        summary: 'A deep dive into agent architectures.',
      },
    ];

    it('should call GET analytics/substack with no params', () => {
      service.getSubstackPosts().subscribe((result) => {
        expect(result).toEqual(mockPosts);
      });

      const req = httpMock.expectOne(`${baseUrl}/analytics/substack`);
      expect(req.request.method).toBe('GET');
      req.flush(mockPosts);
    });
  });
});
