import { HttpParams } from '@angular/common/http';

export type DashboardPeriod = '1d' | '7d' | '14d' | '30d' | '90d' | { readonly from: string; readonly to: string };

export interface DashboardSummary {
  readonly totalEngagement: number;
  readonly previousEngagement: number;
  readonly totalImpressions: number;
  readonly previousImpressions: number;
  readonly engagementRate: number;
  readonly previousEngagementRate: number;
  readonly contentPublished: number;
  readonly previousContentPublished: number;
  readonly costPerEngagement: number;
  readonly previousCostPerEngagement: number;
  readonly websiteUsers: number;
  readonly previousWebsiteUsers: number;
  readonly generatedAt: string;
}

export interface PlatformDailyMetrics {
  readonly platform: string;
  readonly likes: number;
  readonly comments: number;
  readonly shares: number;
  readonly total: number;
}

export interface DailyEngagement {
  readonly date: string;
  readonly platforms: readonly PlatformDailyMetrics[];
  readonly total: number;
}

export interface PlatformSummary {
  readonly platform: string;
  readonly followerCount: number | null;
  readonly postCount: number;
  readonly avgEngagement: number;
  readonly topPostTitle: string | null;
  readonly topPostUrl: string | null;
  readonly isAvailable: boolean;
}

export interface WebsiteOverview {
  readonly activeUsers: number;
  readonly sessions: number;
  readonly pageViews: number;
  readonly avgSessionDuration: number;
  readonly bounceRate: number;
  readonly newUsers: number;
}

export interface PageViewEntry {
  readonly pagePath: string;
  readonly views: number;
  readonly users: number;
}

export interface TrafficSourceEntry {
  readonly channel: string;
  readonly sessions: number;
  readonly users: number;
}

export interface SearchQueryEntry {
  readonly query: string;
  readonly clicks: number;
  readonly impressions: number;
  readonly ctr: number;
  readonly position: number;
}

export interface WebsiteAnalyticsResponse {
  readonly overview: WebsiteOverview;
  readonly topPages: readonly PageViewEntry[];
  readonly trafficSources: readonly TrafficSourceEntry[];
  readonly searchQueries: readonly SearchQueryEntry[];
}

export interface SubstackPost {
  readonly title: string;
  readonly url: string;
  readonly publishedAt: string;
  readonly summary: string | null;
}

export function periodToParams(period: DashboardPeriod, refresh = false): HttpParams {
  let params = new HttpParams();

  if (typeof period === 'string') {
    params = params.set('period', period);
  } else {
    params = params.set('from', period.from).set('to', period.to);
  }

  if (refresh) {
    params = params.set('refresh', 'true');
  }

  return params;
}
