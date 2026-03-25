import { ContentType, PlatformType, TrendSourceType, TrendSuggestionStatus } from '../../../shared/models/enums';

export interface NewsFeedItem {
  readonly id: string;
  readonly suggestionId: string;
  readonly source: TrendSourceType;
  readonly sourceName?: string;
  readonly title: string;
  readonly description?: string;
  readonly url?: string;
  readonly thumbnailUrl?: string;
  readonly sourceCategory?: string;
  readonly score: number;
  readonly relevanceScore: number;
  readonly topic: string;
  readonly suggestedContentType: ContentType;
  readonly suggestedPlatforms: readonly PlatformType[];
  readonly createdAt: string;
  readonly saved: boolean;
  readonly trendItemId: string;
  readonly summary?: string;
}

export interface SourceGroup {
  readonly source: string;
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
  readonly relevanceScore: number;
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
  readonly type: TrendSourceType;
  readonly isEnabled: boolean;
  readonly feedUrl?: string;
  readonly category?: string;
  readonly itemCount: number;
  readonly lastSync?: string;
  readonly comingSoon: boolean;
}

export interface InterestKeyword {
  readonly id: string;
  readonly keyword: string;
  readonly weight: number;
  readonly matchCount: number;
  readonly createdAt: string;
}

export interface SavedNewsItem {
  readonly id: string;
  readonly trendItemId: string;
  readonly title: string;
  readonly url?: string;
  readonly source: TrendSourceType;
  readonly savedAt: string;
  readonly notes?: string;
}

export interface TrendSettings {
  readonly relevanceFilterEnabled: boolean;
  readonly relevanceScoreThreshold: number;
  readonly maxSuggestionsPerCycle: number;
}

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
  readonly sourceTypes: readonly TrendSourceType[];
  readonly categories: readonly string[];
  readonly maxAgeHours: number;
  readonly minRelevance: number;
  readonly searchQuery: string;
  readonly showSavedOnly: boolean;
  readonly showAnalyzedOnly: boolean;
}

export const SOURCE_COLORS: Record<string, string> = {
  FreshRSS: '#f97316',
  Reddit: '#ff4500',
  HackerNews: '#ff6600',
  TrendRadar: '#8b5cf6',
  YouTube: '#ff0000',
  Email: '#3b82f6',
  BrowserHistory: '#6366f1',
  RssFeed: '#22c55e',
};

export const SOURCE_ICONS: Record<string, string> = {
  FreshRSS: 'pi pi-rss',
  Reddit: 'pi pi-reddit',
  HackerNews: 'pi pi-bolt',
  TrendRadar: 'pi pi-wave-pulse',
  YouTube: 'pi pi-youtube',
  Email: 'pi pi-envelope',
  BrowserHistory: 'pi pi-history',
  RssFeed: 'pi pi-rss',
};

export const CATEGORY_ORDER: readonly string[] = [
  'AI/ML',
  '.NET/C#',
  'Angular/Frontend',
  'Azure/Cloud',
  'Security',
  'Docker/Infra',
  'General Tech',
  'Uncategorized',
];

export const CATEGORY_COLORS: Record<string, string> = {
  'AI/ML': '#8b5cf6',
  '.NET/C#': '#512bd4',
  'Angular/Frontend': '#dd0031',
  'Azure/Cloud': '#0078d4',
  'Security': '#ef4444',
  'Docker/Infra': '#2496ed',
  'General Tech': '#6b7280',
  'Uncategorized': '#9ca3af',
};

export const CATEGORY_ICONS: Record<string, string> = {
  'AI/ML': 'pi pi-microchip-ai',
  '.NET/C#': 'pi pi-microsoft',
  'Angular/Frontend': 'pi pi-code',
  'Azure/Cloud': 'pi pi-cloud',
  'Security': 'pi pi-shield',
  'Docker/Infra': 'pi pi-server',
  'General Tech': 'pi pi-desktop',
  'Uncategorized': 'pi pi-th-large',
};
