import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { signal } from '@angular/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { AnalyticsDashboardComponent } from './analytics-dashboard.component';
import { AnalyticsStore } from './store/analytics.store';
import { AnalyticsService } from './services/analytics.service';
import { DashboardSummary, DashboardPeriod } from './models/dashboard.model';

describe('AnalyticsDashboardComponent', () => {
  let component: AnalyticsDashboardComponent;
  let fixture: ComponentFixture<AnalyticsDashboardComponent>;

  const mockSummary: DashboardSummary = {
    totalEngagement: 500,
    previousEngagement: 400,
    totalImpressions: 10000,
    previousImpressions: 8000,
    engagementRate: 5.0,
    previousEngagementRate: 4.0,
    contentPublished: 10,
    previousContentPublished: 8,
    costPerEngagement: 0.02,
    previousCostPerEngagement: 0.03,
    websiteUsers: 1200,
    previousWebsiteUsers: 1000,
    generatedAt: '2026-03-25T00:00:00Z',
  };

  const loadDashboardSpy = jasmine.createSpy('loadDashboard');
  const refreshDashboardSpy = jasmine.createSpy('refreshDashboard');
  const setPeriodSpy = jasmine.createSpy('setPeriod');

  const summarySignal = signal<DashboardSummary | null>(mockSummary);
  const loadingSignal = signal(false);
  const periodSignal = signal<DashboardPeriod>('30d');
  const lastRefreshedSignal = signal<string | null>(new Date().toISOString());
  const isStaleSignal = signal(false);
  const topContentSignal = signal<readonly any[]>([]);
  const timelineSignal = signal<readonly any[]>([]);
  const platformSummariesSignal = signal<readonly any[]>([]);
  const errorsSignal = signal({ summary: null, timeline: null, platforms: null, website: null, substack: null, topContent: null });

  const mockStore = {
    summary: summarySignal,
    loading: loadingSignal,
    period: periodSignal,
    lastRefreshedAt: lastRefreshedSignal,
    isStale: isStaleSignal,
    topContent: topContentSignal,
    timeline: timelineSignal,
    platformSummaries: platformSummariesSignal,
    errors: errorsSignal,
    loadDashboard: loadDashboardSpy,
    refreshDashboard: refreshDashboardSpy,
    setPeriod: setPeriodSpy,
    loadContentReport: jasmine.createSpy('loadContentReport'),
  };

  beforeEach(async () => {
    loadDashboardSpy.calls.reset();
    refreshDashboardSpy.calls.reset();
    setPeriodSpy.calls.reset();

    await TestBed.configureTestingModule({
      imports: [AnalyticsDashboardComponent, NoopAnimationsModule],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: AnalyticsStore, useValue: mockStore },
        { provide: AnalyticsService, useValue: jasmine.createSpyObj('AnalyticsService', ['getContentReport']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AnalyticsDashboardComponent);
    component = fixture.componentInstance;
  });

  it('should call store.loadDashboard on init', () => {
    fixture.detectChanges();
    expect(loadDashboardSpy).toHaveBeenCalledTimes(1);
  });

  it('should show loading state when store.loading is true', () => {
    loadingSignal.set(true);
    summarySignal.set(null);
    fixture.detectChanges();

    const skeletons = fixture.nativeElement.querySelectorAll('p-skeleton');
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it('should render KPI cards when summary is available', () => {
    summarySignal.set(mockSummary);
    loadingSignal.set(false);
    fixture.detectChanges();

    const kpiCards = fixture.nativeElement.querySelector('app-dashboard-kpi-cards');
    expect(kpiCards).toBeTruthy();
  });

  it('should show staleness indicator when data is stale', () => {
    isStaleSignal.set(true);
    lastRefreshedSignal.set(new Date(Date.now() - 60 * 60 * 1000).toISOString());
    fixture.detectChanges();

    const staleText = fixture.nativeElement.querySelector('.staleness-text.stale');
    expect(staleText).toBeTruthy();
  });

  it('should trigger refreshDashboard on refresh button click', () => {
    fixture.detectChanges();
    component.onRefresh();
    expect(refreshDashboardSpy).toHaveBeenCalledTimes(1);
  });

  it('should propagate period changes to store', () => {
    fixture.detectChanges();
    component.onPeriodChanged('7d');
    expect(setPeriodSpy).toHaveBeenCalledWith('7d');
  });
});
