export enum IdeaStatus {
  New = 'New',
  Saved = 'Saved',
  Used = 'Used',
  Dismissed = 'Dismissed',
}

export enum IdeaSourceType {
  RSS = 'RSS',
  API = 'API',
  HackerNews = 'HackerNews',
  GitHub = 'GitHub',
  Manual = 'Manual',
  AIGenerated = 'AIGenerated',
}

export interface Idea {
  id: string;
  title: string;
  description: string | null;
  url: string | null;
  sourceName: string;
  category: string | null;
  summary: string | null;
  thumbnailUrl: string | null;
  status: IdeaStatus;
  tags: string[];
  detectedAt: string;
  hasSavedDetails: boolean;
  score: number | null;
  scoreReason: string | null;
  isDuplicate: boolean;
}

export interface IdeaDetail extends Idea {
  description: string | null;
  url: string | null;
  aiConnections: IdeaConnection[] | null;
  savedDetails: SavedIdeaDetail | null;
  sourceInfo: IdeaSourceInfo | null;
}

export interface SavedIdeaDetail {
  notes: string | null;
  tags: string[];
  suggestedPlatforms: string[];
  suggestedAngle: string | null;
  savedAt: string;
}

export interface IdeaConnection {
  theme: string;
  relatedIdeaIds: string[];
  suggestedAngle: string;
  confidence: number;
}

export interface IdeaSourceInfo {
  name: string;
  type: IdeaSourceType;
  feedUrl: string | null;
}

export interface IdeaSource {
  id: string;
  name: string;
  type: IdeaSourceType;
  feedUrl: string | null;
  apiUrl: string | null;
  category: string;
  pollIntervalMinutes: number;
  isEnabled: boolean;
  lastPolledAt: string | null;
  lastSuccessAt: string | null;
  lastError: string | null;
  consecutiveFailures: number;
  ideaCount: number;
  isHealthy: boolean;
}

export interface CreateIdeaRequest {
  title: string;
  description?: string;
  url?: string;
  category?: string;
  tags?: string[];
}

export interface SaveIdeaRequest {
  notes?: string;
  tags?: string[];
}

export interface IdeaSourceRequest {
  name: string;
  type: IdeaSourceType;
  feedUrl?: string;
  apiUrl?: string;
  category: string;
  pollIntervalMinutes: number;
  isEnabled?: boolean;
}

export interface CreateContentRequest {
  contentType: string;
  primaryPlatform: string;
}

export interface IdeaFilterState {
  status: IdeaStatus | null;
  sourceId: string | null;
  category: string | null;
  tags: string[];
  dateFrom: string | null;
  dateTo: string | null;
  searchText: string | null;
  minScore: number | null;
}

export interface IdeaSortState {
  field: string;
  direction: 'asc' | 'desc';
}
