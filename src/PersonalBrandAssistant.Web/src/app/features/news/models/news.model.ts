import { ContentType, PlatformType } from '../../../shared/models/enums';
import { Idea, IdeaSource, IdeaStatus } from '../../../models/idea.model';

export interface NewsFeedItem {
  readonly id: string;
  readonly sourceName: string;
  readonly title: string;
  readonly description?: string;
  readonly url?: string;
  readonly thumbnailUrl?: string;
  readonly sourceCategory: string;
  readonly topic: string;
  readonly suggestedContentType: ContentType;
  readonly suggestedPlatforms: readonly PlatformType[];
  readonly createdAt: string;
  readonly saved: boolean;
  readonly summary?: string;
  readonly tags: readonly string[];
}

export function ideaToFeedItem(idea: Idea): NewsFeedItem {
  return {
    id: idea.id,
    sourceName: idea.sourceName,
    title: idea.title,
    description: idea.description ?? undefined,
    url: idea.url ?? undefined,
    thumbnailUrl: idea.thumbnailUrl ?? undefined,
    sourceCategory: idea.category ?? 'Uncategorized',
    topic: idea.category ?? 'Uncategorized',
    suggestedContentType: 'Article',
    suggestedPlatforms: [],
    createdAt: idea.detectedAt,
    saved: idea.status === IdeaStatus.Saved,
    summary: idea.summary ?? undefined,
    tags: idea.tags,
  };
}

export interface SourceGroup {
  readonly sourceName: string;
  readonly items: readonly NewsFeedItem[];
}

export interface CategoryGroup {
  readonly category: string;
  readonly items: readonly NewsFeedItem[];
  readonly sourceGroups: readonly SourceGroup[];
}

export interface TopicCluster {
  readonly id: string;
  readonly topic: string;
  readonly rationale: string;
  readonly heat: number;
  readonly velocity: 'rising' | 'stable' | 'falling';
  readonly itemCount: number;
  readonly suggestedContentType: ContentType;
  readonly suggestedPlatforms: readonly PlatformType[];
  readonly articles: readonly NewsFeedItem[];
  readonly createdAt: string;
}

export interface ContentOpportunity {
  readonly id: string;
  readonly topic: string;
  readonly rationale: string;
  readonly score: number;
  readonly suggestedContentType: ContentType;
  readonly suggestedPlatforms: readonly PlatformType[];
  readonly timeliness: 'Urgent' | 'Timely' | 'Evergreen';
  readonly articles: readonly NewsFeedItem[];
  readonly createdAt: string;
}

export interface NewsSource {
  readonly id: string;
  readonly name: string;
  readonly isEnabled: boolean;
  readonly feedUrl?: string;
  readonly category: string;
  readonly itemCount: number;
  readonly lastPolledAt?: string;
  readonly lastSuccessAt?: string;
  readonly lastError?: string;
  readonly consecutiveFailures: number;
}

export function ideaSourceToNewsSource(s: IdeaSource): NewsSource {
  return {
    id: s.id,
    name: s.name,
    isEnabled: s.isEnabled,
    feedUrl: s.feedUrl ?? undefined,
    category: s.category,
    itemCount: s.ideaCount,
    lastPolledAt: s.lastPolledAt ?? undefined,
    lastSuccessAt: s.lastSuccessAt ?? undefined,
    lastError: s.lastError ?? undefined,
    consecutiveFailures: s.consecutiveFailures,
  };
}

export type FeedHealthStatus = 'healthy' | 'warning' | 'error' | 'unknown';

export function getFeedHealth(source: NewsSource): FeedHealthStatus {
  if (!source.lastPolledAt) return 'unknown';
  if (source.consecutiveFailures >= 3) return 'error';
  if (source.consecutiveFailures >= 1 || source.lastError) return 'warning';
  return 'healthy';
}

export const FEED_HEALTH_COLORS: Record<FeedHealthStatus, string> = {
  healthy: '#22c55e',
  warning: '#f59e0b',
  error: '#ef4444',
  unknown: '#6b7280',
};

export interface FeedTimeWindow {
  readonly label: string;
  readonly hours: number;
}

export const TIME_WINDOW_OPTIONS: readonly FeedTimeWindow[] = [
  { label: '6 hours', hours: 6 },
  { label: '12 hours', hours: 12 },
  { label: '1 day', hours: 24 },
  { label: '2 days', hours: 48 },
  { label: '3 days', hours: 72 },
  { label: '1 week', hours: 168 },
  { label: '30 days', hours: 720 },
  { label: 'All time', hours: 0 },
];

export interface NewsFeedFilters {
  readonly categories: readonly string[];
  readonly maxAgeHours: number;
  readonly searchQuery: string;
  readonly showSavedOnly: boolean;
}

export const SOURCE_COLORS: Record<string, string> = {
  RssFeed: '#22c55e',
  Reddit: '#ff4500',
  HackerNews: '#ff6600',
  YouTube: '#ff0000',
  Email: '#3b82f6',
  Manual: '#6366f1',
};

export const SOURCE_ICONS: Record<string, string> = {
  RssFeed: 'pi pi-rss',
  Reddit: 'pi pi-reddit',
  HackerNews: 'pi pi-bolt',
  YouTube: 'pi pi-youtube',
  Email: 'pi pi-envelope',
  Manual: 'pi pi-pencil',
};

export type GroupMode = 'category' | 'source';

export const CATEGORY_ORDER: readonly string[] = [
  'AI/ML',
  '.NET/C#',
  'Angular/Frontend',
  'Azure/Cloud',
  'Security',
  'DevOps',
  'Docker/Infra',
  'Data/Analytics',
  'ComfyUI/GenAI',
  'Content Creation',
  'General Tech',
  'Uncategorized',
];

export const CATEGORY_COLORS: Record<string, string> = {
  'AI/ML': '#8b5cf6',
  '.NET/C#': '#512bd4',
  'Angular/Frontend': '#dd0031',
  'Azure/Cloud': '#0078d4',
  'Security': '#ef4444',
  'DevOps': '#f97316',
  'Docker/Infra': '#2496ed',
  'Data/Analytics': '#14b8a6',
  'ComfyUI/GenAI': '#ec4899',
  'Content Creation': '#a855f7',
  'General Tech': '#6b7280',
  'Uncategorized': '#9ca3af',
};

export const CATEGORY_ICONS: Record<string, string> = {
  'AI/ML': 'pi pi-microchip-ai',
  '.NET/C#': 'pi pi-microsoft',
  'Angular/Frontend': 'pi pi-code',
  'Azure/Cloud': 'pi pi-cloud',
  'Security': 'pi pi-shield',
  'DevOps': 'pi pi-sync',
  'Docker/Infra': 'pi pi-server',
  'Data/Analytics': 'pi pi-chart-bar',
  'ComfyUI/GenAI': 'pi pi-image',
  'Content Creation': 'pi pi-pen-to-square',
  'General Tech': 'pi pi-desktop',
  'Uncategorized': 'pi pi-th-large',
};
