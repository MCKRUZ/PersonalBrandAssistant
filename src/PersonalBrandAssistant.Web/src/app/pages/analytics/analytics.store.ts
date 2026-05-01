import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState, withHooks } from '@ngrx/signals';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  DashboardPeriod,
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
} from '../../features/analytics/models/dashboard.model';
import { TopPerformingContent } from '../../shared/models';
import { BestTimesHeatmap } from './heatmap.model';
import { AnalyticsApiService } from './analytics-api.service';

interface DashboardErrors {
  readonly summary: string | null;
  readonly timeline: string | null;
  readonly platforms: string | null;
  readonly topContent: string | null;
  readonly heatmap: string | null;
  readonly website: string | null;
  readonly substack: string | null;
}

interface AnalyticsState {
  readonly summary: DashboardSummary | null;
  readonly timeline: readonly DailyEngagement[];
  readonly platformSummaries: readonly PlatformSummary[];
  readonly topContent: readonly TopPerformingContent[];
  readonly heatmap: BestTimesHeatmap | null;
  readonly websiteData: WebsiteAnalyticsResponse | null;
  readonly substackPosts: readonly SubstackPost[];
  readonly period: DashboardPeriod;
  readonly loading: boolean;
  readonly lastRefreshedAt: string | null;
  readonly errors: DashboardErrors;
}

const NO_ERRORS: DashboardErrors = {
  summary: null, timeline: null, platforms: null,
  topContent: null, heatmap: null, website: null, substack: null,
};

const initialState: AnalyticsState = {
  summary: null,
  timeline: [],
  platformSummaries: [],
  topContent: [],
  heatmap: null,
  websiteData: null,
  substackPosts: [],
  period: '14d',
  loading: false,
  lastRefreshedAt: null,
  errors: NO_ERRORS,
};

function periodToDateRange(period: DashboardPeriod): { from: string; to: string } {
  const to = new Date();
  const from = new Date();
  const days = period === '7d' ? 7 : period === '14d' ? 14 : 30;
  from.setDate(from.getDate() - days);
  return { from: from.toISOString().split('T')[0], to: to.toISOString().split('T')[0] };
}

export const AnalyticsStore = signalStore(
  withState(initialState),
  withComputed(store => ({
    engagementChange: computed(() => {
      const s = store.summary();
      if (!s || !s.previousEngagement) return 0;
      return Math.round(((s.totalEngagement - s.previousEngagement) / s.previousEngagement) * 100);
    }),
    hasData: computed(() => store.summary() !== null),
    isStale: computed(() => {
      const last = store.lastRefreshedAt();
      if (!last) return true;
      return Date.now() - new Date(last).getTime() > 30 * 60 * 1000;
    }),
  })),
  withMethods((store, api = inject(AnalyticsApiService)) => ({
    loadDashboard(refresh = false) {
      const period = store.period();
      const { from, to } = periodToDateRange(period);
      patchState(store, { loading: true, errors: NO_ERRORS });

      forkJoin({
        summary: api.getDashboardSummary(period, refresh).pipe(catchError(e => of(null as DashboardSummary | null))),
        timeline: api.getEngagementTimeline(period, refresh).pipe(catchError(() => of([] as DailyEngagement[]))),
        platforms: api.getPlatformSummaries(period, refresh).pipe(catchError(() => of([] as PlatformSummary[]))),
        topContent: api.getTopContent(from, to).pipe(catchError(() => of([] as TopPerformingContent[]))),
        heatmap: api.getBestTimesHeatmap(period).pipe(catchError(() => of(null as BestTimesHeatmap | null))),
        website: api.getWebsiteAnalytics(period, refresh).pipe(catchError(() => of(null as WebsiteAnalyticsResponse | null))),
        substack: api.getSubstackPosts().pipe(catchError(() => of([] as SubstackPost[]))),
      }).subscribe(result => {
        patchState(store, {
          summary: result.summary,
          timeline: result.timeline,
          platformSummaries: result.platforms,
          topContent: result.topContent,
          heatmap: result.heatmap,
          websiteData: result.website,
          substackPosts: result.substack,
          loading: false,
          lastRefreshedAt: new Date().toISOString(),
          errors: {
            ...NO_ERRORS,
            summary: result.summary === null ? 'Failed to load summary' : null,
            website: result.website === null ? 'Failed to load website data' : null,
          },
        });
      });
    },

    refreshDashboard() {
      this.loadDashboard(true);
    },

    setPeriod(period: DashboardPeriod) {
      patchState(store, { period });
      this.loadDashboard();
    },
  })),
  withHooks({
    onInit(store) {
      store.loadDashboard();
    },
  }),
);
