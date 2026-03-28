import { PlatformType } from '../../../shared/models';

export type EngagementTaskType = 'Comment' | 'Like' | 'Share' | 'Follow';

export type InboxItemType = 'Mention' | 'DirectMessage' | 'Comment' | 'Reply';

export type SocialPlatformType = PlatformType | 'Reddit';

export type SchedulingMode = 'Fixed' | 'HumanLike';

export interface EngagementTask {
  readonly id: string;
  readonly platform: SocialPlatformType;
  readonly taskType: EngagementTaskType;
  readonly targetCriteria: string;
  readonly cronExpression: string;
  readonly isEnabled: boolean;
  readonly autoRespond: boolean;
  readonly lastExecutedAt?: string;
  readonly nextExecutionAt?: string;
  readonly maxActionsPerExecution: number;
  readonly schedulingMode: SchedulingMode;
  readonly createdAt: string;
}

export interface CreateEngagementTaskRequest {
  readonly platform: string;
  readonly taskType: string;
  readonly targetCriteria: string;
  readonly cronExpression: string;
  readonly isEnabled: boolean;
  readonly autoRespond: boolean;
  readonly maxActionsPerExecution: number;
  readonly schedulingMode?: string;
}

export interface UpdateEngagementTaskRequest {
  readonly targetCriteria?: string;
  readonly cronExpression?: string;
  readonly isEnabled?: boolean;
  readonly autoRespond?: boolean;
  readonly maxActionsPerExecution?: number;
  readonly schedulingMode?: string;
}

export interface EngagementAction {
  readonly id: string;
  readonly actionType: EngagementTaskType;
  readonly targetUrl: string;
  readonly generatedContent?: string;
  readonly platformPostId?: string;
  readonly succeeded: boolean;
  readonly errorMessage?: string;
  readonly performedAt: string;
}

export interface EngagementExecution {
  readonly id: string;
  readonly executedAt: string;
  readonly actionsAttempted: number;
  readonly actionsSucceeded: number;
  readonly errorMessage?: string;
  readonly actions: readonly EngagementAction[];
}

export interface SocialInboxItem {
  readonly id: string;
  readonly platform: SocialPlatformType;
  readonly itemType: InboxItemType;
  readonly authorName: string;
  readonly authorProfileUrl: string;
  readonly content: string;
  readonly sourceUrl: string;
  readonly isRead: boolean;
  readonly draftReply?: string;
  readonly replySent: boolean;
  readonly receivedAt: string;
}

export interface DiscoveredOpportunity {
  readonly postId: string;
  readonly postUrl: string;
  readonly title: string;
  readonly contentPreview: string;
  readonly community: string;
  readonly platform: string;
  readonly discoveredAt: string;
  readonly impactScore?: string;
  readonly category?: string;
}

export interface SocialStats {
  readonly activeTasks: number;
  readonly totalExecutions: number;
  readonly successRate: number;
  readonly totalActions: number;
}

export interface SafetyStatus {
  readonly autonomyLevel: string;
  readonly autoRespondTaskCount: number;
  readonly enabledTaskCount: number;
}

export interface EngageSingleRequest {
  readonly platform: string;
  readonly postId: string;
  readonly postUrl: string;
  readonly title: string;
  readonly content: string;
  readonly community: string;
}
