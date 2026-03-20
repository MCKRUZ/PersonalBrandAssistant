import { ContentType, PlatformType } from './enums';

export interface EngagementSnapshot {
  readonly platform: PlatformType;
  readonly likes: number;
  readonly shares: number;
  readonly comments: number;
  readonly views: number;
  readonly clicks: number;
  readonly impressions: number;
  readonly collectedAt: string;
}

export interface ContentPerformanceReport {
  readonly contentId: string;
  readonly title?: string;
  readonly contentType: ContentType;
  readonly publishedAt?: string;
  readonly totalEngagement: number;
  readonly engagementByPlatform: readonly EngagementSnapshot[];
  readonly generatedAt: string;
}

export interface TopPerformingContent {
  readonly contentId: string;
  readonly title?: string;
  readonly contentType: ContentType;
  readonly totalEngagement: number;
  readonly platforms: readonly PlatformType[];
  readonly publishedAt?: string;
}
