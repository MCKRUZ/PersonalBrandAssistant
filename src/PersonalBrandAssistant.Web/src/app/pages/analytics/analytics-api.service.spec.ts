import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AnalyticsApiService } from './analytics-api.service';

describe('AnalyticsApiService', () => {
  let service: AnalyticsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AnalyticsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch dashboard summary with period param', () => {
    service.getDashboardSummary('14d').subscribe(result => {
      expect(result.totalEngagement).toBe(100);
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/dashboard');
    expect(req.request.params.get('period')).toBe('14d');
    req.flush({ totalEngagement: 100 });
  });

  it('should pass refresh param when requested', () => {
    service.getDashboardSummary('7d', true).subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/analytics/dashboard');
    expect(req.request.params.get('refresh')).toBe('true');
    req.flush({});
  });

  it('should fetch engagement timeline', () => {
    service.getEngagementTimeline('30d').subscribe(result => {
      expect(result.length).toBe(1);
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/engagement-timeline');
    expect(req.request.params.get('period')).toBe('30d');
    req.flush([{ date: '2026-01-01', platforms: [], total: 5 }]);
  });

  it('should fetch platform summaries', () => {
    service.getPlatformSummaries('14d').subscribe(result => {
      expect(result.length).toBe(2);
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/platform-summary');
    req.flush([{ platform: 'linkedin' }, { platform: 'twitter' }]);
  });

  it('should fetch top content with date range', () => {
    service.getTopContent('2026-01-01', '2026-01-14').subscribe(result => {
      expect(result.length).toBe(1);
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/top');
    expect(req.request.params.get('from')).toBe('2026-01-01');
    expect(req.request.params.get('to')).toBe('2026-01-14');
    expect(req.request.params.get('limit')).toBe('10');
    req.flush([{ contentId: '1', totalEngagement: 50 }]);
  });

  it('should fetch best times heatmap', () => {
    service.getBestTimesHeatmap('14d').subscribe(result => {
      expect(result!.maxEngagement).toBe(20);
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/best-times');
    expect(req.request.params.get('period')).toBe('14d');
    req.flush({ cells: [{ day: 0, hour: 9, engagement: 20 }], maxEngagement: 20 });
  });

  it('should propagate heatmap error to caller', () => {
    service.getBestTimesHeatmap('7d').subscribe({
      error: (err) => {
        expect(err.status).toBe(0);
      },
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/best-times');
    req.error(new ProgressEvent('error'));
  });

  it('should fetch website analytics', () => {
    service.getWebsiteAnalytics('14d').subscribe(result => {
      expect(result.overview).toBeTruthy();
    });
    const req = httpMock.expectOne(r => r.url === '/api/analytics/website');
    req.flush({ overview: { activeUsers: 10 }, topPages: [], trafficSources: [], searchQueries: [] });
  });

  it('should fetch substack posts', () => {
    service.getSubstackPosts().subscribe(result => {
      expect(result.length).toBe(1);
    });
    const req = httpMock.expectOne('/api/analytics/substack');
    req.flush([{ title: 'Post 1', url: 'https://example.com', publishedAt: '2026-01-01', summary: null }]);
  });
});
