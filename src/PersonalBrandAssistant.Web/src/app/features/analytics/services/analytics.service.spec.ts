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
});
