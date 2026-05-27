export enum ContentStatus {
  Idea = 'Idea',
  Draft = 'Draft',
  Review = 'Review',
  Approved = 'Approved',
  Scheduled = 'Scheduled',
  Published = 'Published',
  Archived = 'Archived',
}

export enum ContentType {
  BlogPost = 'BlogPost',
  LinkedInPost = 'LinkedInPost',
  Tweet = 'Tweet',
  ThreadedTweet = 'ThreadedTweet',
  SubstackNewsletter = 'SubstackNewsletter',
  RedditPost = 'RedditPost',
  YouTubeVideo = 'YouTubeVideo',
  YouTubeShort = 'YouTubeShort',
}

export enum Platform {
  Blog = 'Blog',
  Medium = 'Medium',
  Substack = 'Substack',
  LinkedIn = 'LinkedIn',
  Twitter = 'Twitter',
  Reddit = 'Reddit',
  YouTube = 'YouTube',
}

export enum PublishStatus {
  Pending = 'Pending',
  Formatting = 'Formatting',
  Published = 'Published',
  Failed = 'Failed',
}

export const PUBLISHABLE_PLATFORMS: readonly Platform[] = [
  Platform.Blog,
  Platform.Medium,
  Platform.Substack,
  Platform.LinkedIn,
  Platform.Twitter,
];

export const PLATFORM_CHAR_LIMITS: Partial<Record<Platform, number>> = {
  [Platform.Twitter]: 280,
  [Platform.LinkedIn]: 3000,
};

export interface Content {
  id: string;
  title: string;
  contentType: ContentType;
  status: ContentStatus;
  primaryPlatform: Platform;
  targetPlatforms: Platform[];
  voiceScore: number | null;
  tags: string[];
  createdAt: string;
  updatedAt: string;
  scheduledAt: string | null;
  publishedAt: string | null;
  platformPublishes: PlatformPublishSummary[];
}

export interface ContentDetail extends Content {
  body: string;
  viralityPrediction: number | null;
  sourceIdeaId: string | null;
  parentContentId: string | null;
  platformPublishes: PlatformPublish[];
  children: ChildContent[];
}

export interface PlatformPublish {
  id: string;
  platform: Platform;
  publishStatus: PublishStatus;
  publishedUrl: string | null;
  publishedAt: string | null;
  retryCount: number;
  nextRetryAt: string | null;
}

export interface PlatformPublishSummary {
  platform: Platform;
  publishStatus: PublishStatus;
  publishedUrl: string | null;
}

export interface ChildContent {
  id: string;
  title: string;
  contentType: ContentType;
  primaryPlatform: Platform;
  status: ContentStatus;
  updatedAt: string;
}

export interface VoiceCheckResult {
  score: number;
  feedback: string;
}

export interface CreateContentRequest {
  title: string;
  contentType: ContentType;
  primaryPlatform: Platform;
  sourceIdeaId?: string;
  tags: string[];
  targetPlatforms?: Platform[];
}

export interface UpdateContentRequest {
  title?: string;
  body?: string;
  tags?: string[];
  contentType?: ContentType;
  primaryPlatform?: Platform;
  targetPlatforms?: Platform[];
  lastUpdatedAt: string;
}

export interface DraftContentRequest {
  action: string;
  instructions?: string;
  toneName?: string;
}

export interface ScheduleContentRequest {
  scheduledAt: string;
}

export interface CrossPostRequest {
  targetPlatform: Platform;
}

export interface PublishRequest {
  targetPlatforms?: Platform[];
}

export interface PublishStatusResponse {
  contentId: string;
  primaryPlatform: Platform;
  platformStatuses: PlatformPublish[];
}

export interface PlatformConnectionStatus {
  platform: Platform;
  isConnected: boolean;
  isExpiring: boolean;
  expiresAt: string | null;
  capabilities: PlatformCapabilities;
}

export interface PlatformCapabilities {
  maxCharacters: number;
  supportsMarkdown: boolean;
  supportsHtml: boolean;
  supportsImages: boolean;
  supportsScheduling: boolean;
  supportsThreads: boolean;
}

export interface ContentFilterState {
  status?: ContentStatus;
  platform?: Platform;
  contentType?: ContentType;
  dateFrom?: string;
  dateTo?: string;
  search?: string;
}
