import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DashboardStore } from './dashboard.store';
import { DashboardApiService } from './dashboard-api.service';
import { environment } from '../../environments/environment';

describe('DashboardStore', () => {
  let store: InstanceType<typeof DashboardStore>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DashboardStore,
        DashboardApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(DashboardStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should have correct initial state', () => {
    expect(store.kpis()).toBeUndefined();
    expect(store.schedule()).toEqual([]);
    expect(store.recentItems()).toEqual([]);
    expect(store.suggestions()).toEqual([]);
    expect(store.isLoading()).toBe(false);
    expect(store.error()).toBeUndefined();
  });

  it('should set isLoading when load is called', () => {
    store.load();
    expect(store.isLoading()).toBe(true);

    const today = new Date().toISOString().split('T')[0];
    httpMock.expectOne(`${environment.apiUrl}/analytics/dashboard`).flush({});
    httpMock.expectOne(r => r.url === `${environment.apiUrl}/calendar`).flush([]);
    httpMock.expectOne(r => r.url === `${environment.apiUrl}/content`).flush({ items: [] });
    httpMock.expectOne(`${environment.apiUrl}/integration/briefing/summary`).flush([]);
  });

  it('should populate state on successful load', () => {
    const kpis = { pendingCount: 2, publishedCount: 5, reach: 1200, aiCost: 0.45 };
    const schedule = [{ id: 's1', scheduledAt: '2026-04-30T09:00:00Z', platform: 'LinkedIn', contentTitle: 'Test' }];
    const items = [{ id: 'c1', title: 'Post 1', body: '', type: 'SocialPost', status: 'Draft', platform: 'LinkedIn', createdAt: '2026-04-30T08:00:00Z', updatedAt: '2026-04-30T08:00:00Z', version: 1, capturedAutonomyLevel: 'Semi' }];
    const suggestions = [{ topic: 'AI trends', platform: 'LinkedIn', source: 'news' }];

    store.load();

    httpMock.expectOne(`${environment.apiUrl}/analytics/dashboard`).flush(kpis);
    httpMock.expectOne(r => r.url === `${environment.apiUrl}/calendar`).flush(schedule);
    httpMock.expectOne(r => r.url === `${environment.apiUrl}/content`).flush({ items });
    httpMock.expectOne(`${environment.apiUrl}/integration/briefing/summary`).flush(suggestions);

    expect(store.isLoading()).toBe(false);
    expect(store.kpis()).toEqual(kpis);
    expect(store.schedule().length).toBe(1);
    expect(store.recentItems().length).toBe(1);
    expect(store.suggestions().length).toBe(1);
  });

  it('should gracefully handle individual endpoint failures', () => {
    store.load();

    httpMock.expectOne(`${environment.apiUrl}/analytics/dashboard`).error(new ProgressEvent('error'));
    httpMock.expectOne(r => r.url === `${environment.apiUrl}/calendar`).flush([]);
    httpMock.expectOne(r => r.url === `${environment.apiUrl}/content`).flush({ items: [] });
    httpMock.expectOne(`${environment.apiUrl}/integration/briefing/summary`).flush([]);

    expect(store.isLoading()).toBe(false);
    expect(store.kpis()).toBeUndefined();
    expect(store.schedule()).toEqual([]);
    expect(store.error()).toBeUndefined();
  });
});
