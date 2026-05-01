import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AnalyticsComponent } from './analytics.component';
import { AnalyticsStore } from './analytics.store';

describe('AnalyticsComponent', () => {
  let component: AnalyticsComponent;
  let fixture: ComponentFixture<AnalyticsComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnalyticsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(AnalyticsComponent);
    component = fixture.componentInstance;
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

  it('should create', () => {
    expect(component).toBeTruthy();
    flushAllRequests();
  });

  it('should show loading spinner when loading with no data', fakeAsync(() => {
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-loading-spinner')).toBeTruthy();
    flushAllRequests();
    tick();
  }));

  it('should show dashboard content after data loads', fakeAsync(() => {
    flushAllRequests();
    tick();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-dashboard-kpi-cards')).toBeTruthy();
    expect(el.querySelector('app-loading-spinner')).toBeFalsy();
  }));

  it('should have period options', () => {
    expect(component.periodOptions.length).toBe(3);
    expect(component.periodOptions[0].value).toBe('7d');
    flushAllRequests();
  });

  it('should change period via store', fakeAsync(() => {
    flushAllRequests();
    tick();
    component.onPeriodChange('7d');
    expect(component.store.period()).toBe('7d');
    flushAllRequests();
    tick();
  }));
});
