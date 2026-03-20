import { ActorType, ContentStatus, ContentType, PlatformType } from './enums';

export interface TransitionRequest {
  readonly targetStatus: ContentStatus;
  readonly reason?: string;
}

export interface WorkflowTransitionLog {
  readonly id: string;
  readonly contentId: string;
  readonly fromStatus: ContentStatus;
  readonly toStatus: ContentStatus;
  readonly reason?: string;
  readonly actorType: ActorType;
  readonly actorId?: string;
  readonly timestamp: string;
}

export interface BrandVoiceScore {
  readonly contentId: string;
  readonly overallScore: number;
  readonly toneScore: number;
  readonly vocabularyScore: number;
  readonly personaScore: number;
  readonly issues: readonly string[];
  readonly scoredAt: string;
}

export interface RepurposingSuggestion {
  readonly platform: PlatformType;
  readonly suggestedType: ContentType;
  readonly rationale: string;
  readonly confidenceScore: number;
}
