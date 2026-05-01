import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AnalyticsStore } from './analytics.store';

describe('AnalyticsStore', () => {
  let store: InstanceType<typeof AnalyticsStore>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AnalyticsStore, provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(AnalyticsStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushAllRequests() {
    httpMock.expectOne(r => r.url === '/api/analytics/dashboard').flush({
      totalEngagement: 100, previousEngagement: 80,
      totalImpressions: 500, previousImpressions: 400,
      engagementRate: 0.2, previousEngagementRate: 0.18,
      contentPublished: 5, previousContentPublished: 3,
      costPerEngagement: 0.5, previousCostPerEngagement: 0.6,
      websiteUsers: 200, previousWebsiteUsers: 150,
      generatedAt: '2026-01-01',
    });
    httpMock.expectOne(r => r.url === '/api/analytics/engagement-timeline').flush([]);
    httpMock.expectOne(r => r.url === '/api/analytics/platform-summary').flush([]);
    httpMock.expectOne(r => r.url === '/api/analytics/top').flush([]);
    httpMock.expectOne(r => r.url === '/api/analytics/best-times').flush({ cells: [], maxEngagement: 0 });
    httpMock.expectOne(r => r.url === '/api/analytics/website').flush(null);
    httpMock.expectOne(r => r.url === '/api/analytics/substack').flush([]);
  }

  it('should have initial state', () => {
    expect(store.loading()).toBe(true);
    expect(store.period()).toBe('14d');
    expect(store.summary()).toBeNull();
    flushAllRequests();
  });

  it('should load dashboard on init', fakeAsync(() => {
    flushAllRequests();
    tick();
    expect(store.loading()).toBe(false);
    expect(store.summary()).toBeTruthy();
    expect(store.hasData()).toBe(true);
  }));

  it('should compute engagement change', fakeAsync(() => {
    flushAllRequests();
    tick();
    expect(store.engagementChange()).toBe(25);
  }));

  it('should update period and reload', fakeAsync(() => {
    flushAllRequests();
    tick();
    store.setPeriod('7d');
    expect(store.period()).toBe('7d');
    expect(store.loading()).toBe(true);
    flushAllRequests();
    tick();
    expect(store.loading()).toBe(false);
  }));

  it('should handle partial failures gracefully', fakeAsync(() => {
    httpMock.expectOne(r => r.url === '/api/analytics/dashboard').flush({
      totalEngagement: 50, previousEngagement: 50,
      totalImpressions: 100, previousImpressions: 100,
      engagementRate: 0.5, previousEngagementRate: 0.5,
      contentPublished: 2, previousContentPublished: 2,
      costPerEngagement: 1, previousCostPerEngagement: 1,
      websiteUsers: 50, previousWebsiteUsers: 50,
      generatedAt: '2026-01-01',
    });
    httpMock.expectOne(r => r.url === '/api/analytics/engagement-timeline').error(new ProgressEvent('error'));
    httpMock.expectOne(r => r.url === '/api/analytics/platform-summary').error(new ProgressEvent('error'));
    httpMock.expectOne(r => r.url === '/api/analytics/top').error(new ProgressEvent('error'));
    httpMock.expectOne(r => r.url === '/api/analytics/best-times').error(new ProgressEvent('error'));
    httpMock.expectOne(r => r.url === '/api/analytics/website').error(new ProgressEvent('error'));
    httpMock.expectOne(r => r.url === '/api/analytics/substack').error(new ProgressEvent('error'));
    tick();
    expect(store.loading()).toBe(false);
    expect(store.hasData()).toBe(true);
    expect(store.timeline()).toEqual([]);
  }));

  it('should refresh dashboard with refresh flag', fakeAsync(() => {
    flushAllRequests();
    tick();
    store.refreshDashboard();
    const req = httpMock.expectOne(r => r.url === '/api/analytics/dashboard');
    expect(req.request.params.get('refresh')).toBe('true');
    req.flush({
      totalEngagement: 100, previousEngagement: 80,
      totalImpressions: 500, previousImpressions: 400,
      engagementRate: 0.2, previousEngagementRate: 0.18,
      contentPublished: 5, previousContentPublished: 3,
      costPerEngagement: 0.5, previousCostPerEngagement: 0.6,
      websiteUsers: 200, previousWebsiteUsers: 150,
      generatedAt: '2026-01-01',
    });
    httpMock.expectOne(r => r.url === '/api/analytics/engagement-timeline').flush([]);
    httpMock.expectOne(r => r.url === '/api/analytics/platform-summary').flush([]);
    httpMock.expectOne(r => r.url === '/api/analytics/top').flush([]);
    httpMock.expectOne(r => r.url === '/api/analytics/best-times').flush({ cells: [], maxEngagement: 0 });
    httpMock.expectOne(r => r.url === '/api/analytics/website').flush(null);
    httpMock.expectOne(r => r.url === '/api/analytics/substack').flush([]);
    tick();
    expect(store.loading()).toBe(false);
  }));
});
