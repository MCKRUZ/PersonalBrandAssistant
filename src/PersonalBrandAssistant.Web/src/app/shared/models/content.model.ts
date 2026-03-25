import { ContentStatus, ContentType, PlatformType, AutonomyLevel } from './enums';

export interface ContentMetadata {
  readonly tags: readonly string[];
  readonly seoKeywords: readonly string[];
  readonly platformSpecificData: Readonly<Record<string, string>>;
  readonly aiGenerationContext?: string;
  readonly tokensUsed?: number;
  readonly estimatedCost?: number;
}

export interface Content {
  readonly id: string;
  readonly contentType: ContentType;
  readonly title?: string;
  readonly body: string;
  readonly status: ContentStatus;
  readonly targetPlatforms: readonly PlatformType[];
  readonly scheduledAt?: string;
  readonly publishedAt?: string;
  readonly parentContentId?: string;
  readonly treeDepth: number;
  readonly repurposeSourcePlatform?: PlatformType;
  readonly capturedAutonomyLevel: AutonomyLevel;
  readonly retryCount: number;
  readonly nextRetryAt?: string;
  readonly publishingStartedAt?: string;
  readonly version: number;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly metadata: ContentMetadata;
}

export interface CreateContentRequest {
  readonly contentType: ContentType;
  readonly body: string;
  readonly title?: string;
  readonly targetPlatforms?: readonly PlatformType[];
  readonly metadata?: Partial<ContentMetadata>;
}

export interface UpdateContentRequest {
  readonly id: string;
  readonly title?: string;
  readonly body?: string;
  readonly targetPlatforms?: readonly PlatformType[];
  readonly metadata?: Partial<ContentMetadata>;
  readonly version: number;
}

export interface ContentCreationRequest {
  readonly type: ContentType;
  readonly topic: string;
  readonly outline?: string;
  readonly targetPlatforms?: readonly PlatformType[];
  readonly parentContentId?: string;
  readonly parameters?: Readonly<Record<string, string>>;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly cursor?: string;
  readonly hasMore: boolean;
}

export interface PlatformFormatOption {
  readonly platform: PlatformType;
  readonly format: ContentType;
  readonly suggestedAngle: string;
  readonly rationale: string;
  readonly confidenceScore: number;
}

export interface ContentIdeaRecommendation {
  readonly storyTitle: string;
  readonly storySummary: string;
  readonly sourceUrl?: string;
  readonly angles: readonly string[];
  readonly recommendations: readonly PlatformFormatOption[];
}
