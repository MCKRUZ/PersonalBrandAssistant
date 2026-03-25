import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap, forkJoin, of, catchError, map, Observable } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { AnalyticsService } from '../services/analytics.service';
import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
import {
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
  DashboardPeriod,
} from '../models/dashboard.model';

interface DataOrError<T> {
  readonly data: T | null;
  readonly error: string | null;
}

interface DashboardErrors {
  readonly summary: string | null;
  readonly timeline: string | null;
  readonly platforms: string | null;
  readonly website: string | null;
  readonly substack: string | null;
  readonly topContent: string | null;
}

interface AnalyticsDashboardState {
  readonly summary: DashboardSummary | null;
  readonly timeline: readonly DailyEngagement[];
  readonly platformSummaries: readonly PlatformSummary[];
  readonly websiteData: WebsiteAnalyticsResponse | null;
  readonly substackPosts: readonly SubstackPost[];
  readonly topContent: readonly TopPerformingContent[];
  readonly selectedReport: ContentPerformanceReport | undefined;
  readonly period: DashboardPeriod;
  readonly loading: boolean;
  readonly lastRefreshedAt: string | null;
  readonly errors: DashboardErrors;
}

const initialErrors: DashboardErrors = {
  summary: null,
  timeline: null,
  platforms: null,
  website: null,
  substack: null,
  topContent: null,
};

const initialState: AnalyticsDashboardState = {
  summary: null,
  timeline: [],
  platformSummaries: [],
  websiteData: null,
  substackPosts: [],
  topContent: [],
  selectedReport: undefined,
  period: '30d',
  loading: false,
  lastRefreshedAt: null,
  errors: initialErrors,
};

function wrapCatchError<T>(obs: Observable<T>): Observable<DataOrError<T>> {
  return obs.pipe(
    map(data => ({ data, error: null as string | null })),
    catchError(err => of({ data: null as T | null, error: err?.message ?? 'Unknown error' })),
  );
}

function periodToDateRange(period: DashboardPeriod): { from: string; to: string } {
  if (typeof period !== 'string') {
    return period;
  }
  const days = parseInt(period.replace('d', ''), 10);
  const to = new Date();
  const from = new Date(to.getTime() - days * 86_400_000);
  return { from: from.toISOString(), to: to.toISOString() };
}

function fetchDashboard(
  analyticsService: AnalyticsService,
  period: DashboardPeriod,
  refresh: boolean,
) {
  const range = periodToDateRange(period);
  return forkJoin({
    summary: wrapCatchError(analyticsService.getDashboardSummary(period, refresh)),
    timeline: wrapCatchError(analyticsService.getEngagementTimeline(period, refresh)),
    platforms: wrapCatchError(analyticsService.getPlatformSummaries(period, refresh)),
    website: wrapCatchError(analyticsService.getWebsiteAnalytics(period, refresh)),
    substack: wrapCatchError(analyticsService.getSubstackPosts()),
    topContent: wrapCatchError(analyticsService.getTopContent(range.from, range.to)),
  });
}

function percentChange(current: number, previous: number): number | null {
  if (previous === 0) return null;
  return Math.round(((current - previous) / previous) * 10000) / 100;
}

export const AnalyticsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => ({
    engagementChange: computed(() => {
      const s = store.summary();
      return s ? percentChange(s.totalEngagement, s.previousEngagement) : null;
    }),
    impressionsChange: computed(() => {
      const s = store.summary();
      return s ? percentChange(s.totalImpressions, s.previousImpressions) : null;
    }),
    engagementRateChange: computed(() => {
      const s = store.summary();
      return s ? s.engagementRate - s.previousEngagementRate : null;
    }),
    contentPublishedChange: computed(() => {
      const s = store.summary();
      return s ? percentChange(s.contentPublished, s.previousContentPublished) : null;
    }),
    websiteUsersChange: computed(() => {
      const s = store.summary();
      return s ? percentChange(s.websiteUsers, s.previousWebsiteUsers) : null;
    }),
    costPerEngagementChange: computed(() => {
      const s = store.summary();
      return s ? percentChange(s.costPerEngagement, s.previousCostPerEngagement) : null;
    }),
    isStale: computed(() => {
      const ts = store.lastRefreshedAt();
      if (!ts) return true;
      return (Date.now() - new Date(ts).getTime()) > 30 * 60 * 1000;
    }),
  })),
  withMethods((store, analyticsService = inject(AnalyticsService)) => ({
    loadDashboard: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, errors: initialErrors })),
        switchMap(() => fetchDashboard(analyticsService, store.period(), false)),
        tap(results => patchState(store, {
          summary: results.summary.data,
          timeline: results.timeline.data ?? [],
          platformSummaries: results.platforms.data ?? [],
          websiteData: results.website.data,
          substackPosts: results.substack.data ?? [],
          topContent: results.topContent.data ?? [],
          loading: false,
          lastRefreshedAt: new Date().toISOString(),
          errors: {
            summary: results.summary.error,
            timeline: results.timeline.error,
            platforms: results.platforms.error,
            website: results.website.error,
            substack: results.substack.error,
            topContent: results.topContent.error,
          },
        })),
      ),
    ),

    refreshDashboard: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, errors: initialErrors })),
        switchMap(() => fetchDashboard(analyticsService, store.period(), true)),
        tap(results => patchState(store, {
          summary: results.summary.data,
          timeline: results.timeline.data ?? [],
          platformSummaries: results.platforms.data ?? [],
          websiteData: results.website.data,
          substackPosts: results.substack.data ?? [],
          topContent: results.topContent.data ?? [],
          loading: false,
          lastRefreshedAt: new Date().toISOString(),
          errors: {
            summary: results.summary.error,
            timeline: results.timeline.error,
            platforms: results.platforms.error,
            website: results.website.error,
            substack: results.substack.error,
            topContent: results.topContent.error,
          },
        })),
      ),
    ),

    loadContentReport: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(id =>
          analyticsService.getContentReport(id).pipe(
            tapResponse({
              next: report => patchState(store, { selectedReport: report, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    setPeriod(period: DashboardPeriod) {
      patchState(store, { period });
      this.loadDashboard();
    },
  })),
);
