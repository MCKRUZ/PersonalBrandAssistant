import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AnalyticsService } from './analytics.service';
import { WebsiteAnalytics } from '../models/analytics.model';

describe('AnalyticsService', () => {
  let service: AnalyticsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AnalyticsService],
    });
    service = TestBed.inject(AnalyticsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests website analytics with the period param', () => {
    const stub: WebsiteAnalytics = {
      overview: { activeUsers: 1, sessions: 2, pageViews: 3, avgSessionDuration: 4, bounceRate: 5, newUsers: 6 },
      topPages: [], trafficSources: [], searchQueries: [],
    };

    let result: WebsiteAnalytics | undefined;
    service.getWebsite('30d').subscribe(r => (result = r));

    const req = httpMock.expectOne('/api/analytics/website?period=30d');
    expect(req.request.method).toBe('GET');
    req.flush(stub);

    expect(result?.overview.activeUsers).toBe(1);
  });

  it('requests health', () => {
    service.getHealth().subscribe();
    const req = httpMock.expectOne('/api/analytics/health');
    expect(req.request.method).toBe('GET');
    req.flush({ ga4: true, searchConsole: true });
  });

  it('requests overview with the period param', () => {
    service.getOverview('7d').subscribe();
    const req = httpMock.expectOne('/api/analytics/overview?period=7d');
    expect(req.request.method).toBe('GET');
    req.flush({ totalAudience: 0, combinedKpis: [], channels: [] });
  });

  it('requests channel analytics for a platform with the period param', () => {
    service.getChannel('youtube', '30d').subscribe();
    const req = httpMock.expectOne('/api/analytics/channel/youtube?period=30d');
    expect(req.request.method).toBe('GET');
    req.flush({ platform: 'YouTube', status: 'Connected', asOf: null, kpis: [], trends: [], recentPosts: [] });
  });

  it('requests the YouTube deep path with the period param', () => {
    service.getYouTubeDeep('90d').subscribe();
    const req = httpMock.expectOne('/api/analytics/youtube/deep?period=90d');
    expect(req.request.method).toBe('GET');
    req.flush({ daySeries: [], trafficSources: [], geography: [], demographics: [] });
  });
});
