export interface WebsiteOverview {
  activeUsers: number;
  sessions: number;
  pageViews: number;
  avgSessionDuration: number;
  bounceRate: number;
  newUsers: number;
}

export interface PageViewEntry {
  pagePath: string;
  views: number;
  uniqueUsers: number;
}

export interface TrafficSourceEntry {
  channel: string;
  sessions: number;
  users: number;
}

export interface SearchQueryEntry {
  query: string;
  clicks: number;
  impressions: number;
  ctr: number;
  position: number;
}

export interface WebsiteAnalytics {
  overview: WebsiteOverview;
  topPages: PageViewEntry[];
  trafficSources: TrafficSourceEntry[];
  searchQueries: SearchQueryEntry[];
}

export interface AnalyticsHealth {
  ga4: boolean;
  searchConsole: boolean;
}

export type AnalyticsPeriod = '7d' | '30d' | '90d';
