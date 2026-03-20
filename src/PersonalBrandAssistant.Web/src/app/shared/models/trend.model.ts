import { ContentType, PlatformType, TrendSuggestionStatus, TrendSourceType } from './enums';

export interface TrendSuggestionItem {
  readonly source: TrendSourceType;
  readonly title: string;
  readonly description?: string;
  readonly url?: string;
  readonly sourceName?: string;
  readonly thumbnailUrl?: string;
  readonly sourceCategory?: string;
  readonly score: number;
  readonly trendItemId: string;
  readonly summary?: string;
}

export interface TrendSuggestion {
  readonly id: string;
  readonly topic: string;
  readonly rationale: string;
  readonly relevanceScore: number;
  readonly suggestedContentType: ContentType;
  readonly suggestedPlatforms: readonly PlatformType[];
  readonly status: TrendSuggestionStatus;
  readonly relatedTrends: readonly TrendSuggestionItem[];
  readonly createdAt: string;
  readonly updatedAt: string;
}
