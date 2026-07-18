// TS mirrors of the section-06 read-API DTOs (PBA.Application/Features/ChannelAnalytics/Dtos).
// JSON is camelCase; enums serialize as their string NAME (global JsonStringEnumConverter),
// so `platform` is "YouTube"/"Instagram"/"TikTok" and `status` is one of ConnectionStatus below.

export type ConnectionStatus = 'Connected' | 'ReconnectRequired' | 'NotConnected';

// Lowercase route token used in /api/analytics/channel/{platform} and /api/auth/{platform}/authorize.
export type ChannelPlatform = 'youtube' | 'instagram' | 'tiktok';

// Snapshot-derived point: C# MetricPoint(DateOnly Date, long Value) -> { date: "2026-07-01", value }.
export interface MetricPoint {
  date: string;
  value: number;
}

export interface TrendSeries {
  metric: string;
  points: MetricPoint[];
}

export interface KpiCard {
  key: string;
  label: string;
  value: number;
  deltaPct: number | null;
  rate: number | null; // engagement-rate fraction, null except on the engagement card
}

export interface RecentPost {
  videoId: string;
  title: string | null;
  metrics: Record<string, number>;
}

export interface ChannelAnalytics {
  platform: string; // enum string name, e.g. "YouTube"
  status: ConnectionStatus;
  asOf: string | null;
  kpis: KpiCard[];
  trends: TrendSeries[];
  recentPosts: RecentPost[];
}

export interface OverviewChannel {
  platform: string;
  status: ConnectionStatus;
  followers: number | null;
  followerSparkline: MetricPoint[];
}

export interface AnalyticsOverview {
  totalAudience: number; // sum across connected channels — labeled APPROXIMATE in the UI
  combinedKpis: KpiCard[];
  channels: OverviewChannel[];
}

// Live YouTube Analytics v2 deep path. Actual JSON (verified against YouTubeDeepAnalyticsDto):
// four labeled-series arrays, each element = { metric, points: [{ day, value }] }.
export interface YouTubeMetricPoint {
  day: string;
  value: number;
}

export interface YouTubeMetricSeries {
  metric: string;
  points: YouTubeMetricPoint[];
}

export interface YouTubeDeepAnalytics {
  daySeries: YouTubeMetricSeries[];
  trafficSources: YouTubeMetricSeries[];
  geography: YouTubeMetricSeries[];
  demographics: YouTubeMetricSeries[];
}
