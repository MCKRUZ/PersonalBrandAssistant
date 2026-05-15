export enum FeedItemType {
  AgentDraft = 'AgentDraft',
  TrendAlert = 'TrendAlert',
  AnalyticsHighlight = 'AnalyticsHighlight',
  IdeaSuggestion = 'IdeaSuggestion',
  ApprovalRequest = 'ApprovalRequest',
  SystemNotification = 'SystemNotification',
}

export enum FeedItemPriority {
  Low = 'Low',
  Normal = 'Normal',
  High = 'High',
  Urgent = 'Urgent',
}

export interface FeedItem {
  id: string;
  type: FeedItemType;
  title: string;
  summary: string;
  data: string | null;
  actionType: string | null;
  actionTargetId: string | null;
  priority: FeedItemPriority;
  isRead: boolean;
  isActedOn: boolean;
  createdAt: string;
  expiresAt: string | null;
}

export interface FeedActionResult {
  success: boolean;
  navigationTarget: string | null;
  targetId: string | null;
}

export interface FeedListParams {
  type?: FeedItemType;
  priority?: FeedItemPriority;
  isRead?: boolean;
  includeExpired?: boolean;
  sortBy?: string;
  sortDirection?: string;
  page?: number;
  pageSize?: number;
}
