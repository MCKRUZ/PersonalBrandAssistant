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

export interface Content {
  id: string;
  title: string;
  contentType: ContentType;
  status: ContentStatus;
  primaryPlatform: Platform;
  voiceScore: number | null;
  tags: string[];
  createdAt: string;
  updatedAt: string;
  scheduledAt: string | null;
  publishedAt: string | null;
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
}

export interface UpdateContentRequest {
  title?: string;
  body?: string;
  tags?: string[];
  contentType?: ContentType;
  primaryPlatform?: Platform;
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

export interface ContentFilterState {
  status?: ContentStatus;
  platform?: Platform;
  contentType?: ContentType;
  dateFrom?: string;
  dateTo?: string;
  search?: string;
}
