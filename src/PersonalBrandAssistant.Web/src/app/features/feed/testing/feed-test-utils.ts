import { signal, WritableSignal } from '@angular/core';
import { FeedItem, FeedItemType, FeedItemPriority, FeedActionResult } from '../models/feed-item.model';
import { FeedSummary } from '../models/feed-summary.model';
import { TrendingTopic } from '../models/trending-topic.model';

export function mockFeedItem(overrides: Partial<FeedItem> = {}): FeedItem {
  return {
    id: crypto.randomUUID(),
    type: FeedItemType.AgentDraft,
    title: 'Test Item',
    summary: 'Test summary',
    data: null,
    actionType: 'approve',
    actionTargetId: null,
    priority: FeedItemPriority.Normal,
    isRead: false,
    isActedOn: false,
    createdAt: new Date().toISOString(),
    expiresAt: null,
    ...overrides,
  };
}

export function mockFeedSummary(overrides: Partial<FeedSummary> = {}): FeedSummary {
  return {
    unreadCount: 10,
    pendingApprovals: 3,
    trendingCount: 5,
    engagementDelta: 12.5,
    ...overrides,
  };
}

export function mockTrendingTopic(overrides: Partial<TrendingTopic> = {}): TrendingTopic {
  return {
    topic: 'Angular',
    count: 5,
    latestAt: new Date().toISOString(),
    ...overrides,
  };
}

export function createMockFeedStore() {
  return {
    items: signal([]) as WritableSignal<FeedItem[]>,
    loading: signal(false) as WritableSignal<boolean>,
    totalCount: signal(0) as WritableSignal<number>,
    page: signal(1) as WritableSignal<number>,
    pageSize: signal(20) as WritableSignal<number>,
    activeFilter: signal(null) as WritableSignal<FeedItemType | null>,
    error: signal(null) as WritableSignal<string | null>,
    summary: signal(null) as WritableSignal<FeedSummary | null>,
    summaryLoading: signal(false) as WritableSignal<boolean>,
    trendingTopics: signal([]) as WritableSignal<TrendingTopic[]>,
    selectedIds: signal([]) as WritableSignal<string[]>,
    newItemCount: signal(0) as WritableSignal<number>,
    lastBatchFailures: signal([]) as WritableSignal<{ id: string; reason: string }[]>,
    hasSelection: signal(false) as WritableSignal<boolean>,
    selectedCount: signal(0) as WritableSignal<number>,
    isAllSelected: signal(false) as WritableSignal<boolean>,
    loadItems: jasmine.createSpy('loadItems'),
    loadSummary: jasmine.createSpy('loadSummary'),
    loadTrending: jasmine.createSpy('loadTrending'),
    setFilter: jasmine.createSpy('setFilter'),
    setPage: jasmine.createSpy('setPage'),
    toggleSelect: jasmine.createSpy('toggleSelect'),
    selectAll: jasmine.createSpy('selectAll'),
    clearSelection: jasmine.createSpy('clearSelection'),
    markRead: jasmine.createSpy('markRead'),
    actOnItem: jasmine.createSpy('actOnItem'),
    batchMarkRead: jasmine.createSpy('batchMarkRead'),
    batchMarkReadByIds: jasmine.createSpy('batchMarkReadByIds'),
    batchDismiss: jasmine.createSpy('batchDismiss'),
    batchAct: jasmine.createSpy('batchAct'),
    incrementNewItemCount: jasmine.createSpy('incrementNewItemCount'),
    loadNewItems: jasmine.createSpy('loadNewItems'),
    updateSummary: jasmine.createSpy('updateSummary'),
  };
}
