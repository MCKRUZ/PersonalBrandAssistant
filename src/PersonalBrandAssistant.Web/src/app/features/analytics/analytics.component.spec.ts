import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AnalyticsComponent } from './analytics.component';
import { WebsiteAnalytics } from './models/analytics.model';

describe('AnalyticsComponent', () => {
  let httpMock: HttpTestingController;

  const stub: WebsiteAnalytics = {
    overview: { activeUsers: 123, sessions: 200, pageViews: 500, avgSessionDuration: 90, bounceRate: 0.4, newUsers: 80 },
    topPages: [{ pagePath: '/blog', views: 50, uniqueUsers: 30 }],
    trafficSources: [{ channel: 'Organic Search', sessions: 100, users: 80 }],
    searchQueries: [{ query: 'ai tools', clicks: 50, impressions: 1000, ctr: 0.05, position: 3.2 }],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AnalyticsComponent, HttpClientTestingModule],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads website analytics on init and renders active users', () => {
    const fixture = TestBed.createComponent(AnalyticsComponent);
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
    const health = httpMock.match('/api/analytics/health');
    health.forEach(r => r.flush({ ga4: true, searchConsole: true }));

    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('123');
  });

  it('shows an unavailable banner when a health source is down', () => {
    const fixture = TestBed.createComponent(AnalyticsComponent);
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
    const health = httpMock.match('/api/analytics/health');
    health.forEach(r => r.flush({ ga4: false, searchConsole: true }));

    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('unavailable');
  });
});
