import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { FeedStore } from './feed.store';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType } from '../models/feed-item.model';
import type { FeedItem } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { TrendingTopic } from '../models/trending-topic.model';
import type { PagedResult } from '../../../models/pagination.model';
import { mockFeedItem, mockFeedSummary, mockTrendingTopic } from '../testing/feed-test-utils';

describe('FeedStore', () => {
  let store: InstanceType<typeof FeedStore>;
  let feedServiceSpy: jasmine.SpyObj<FeedService>;
  let feedItemSubject: Subject<FeedItem>;
  let summarySubject: Subject<FeedSummary>;

  const mockItems: FeedItem[] = [
    mockFeedItem({ id: 'item-1', title: 'Item 1' }),
    mockFeedItem({ id: 'item-2', title: 'Item 2', type: FeedItemType.TrendAlert }),
  ];

  const mockPagedResult: PagedResult<FeedItem> = {
    items: mockItems,
    totalCount: 2,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  };

  const mockSummary = mockFeedSummary();
  const mockTrending: TrendingTopic[] = [
    mockTrendingTopic({ topic: 'Angular' }),
    mockTrendingTopic({ topic: 'AI' }),
  ];

  beforeEach(() => {
    feedItemSubject = new Subject<FeedItem>();
    summarySubject = new Subject<FeedSummary>();

    feedServiceSpy = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending', 'markRead', 'actOnItem',
      'batchMarkRead', 'batchMarkReadByIds', 'batchDismiss', 'batchAct',
    ]);
    feedServiceSpy.list.and.returnValue(of(mockPagedResult));
    feedServiceSpy.getSummary.and.returnValue(of(mockSummary));
    feedServiceSpy.getTrending.and.returnValue(of(mockTrending));
    feedServiceSpy.markRead.and.returnValue(of(void 0));
    feedServiceSpy.actOnItem.and.returnValue(of({ success: true, navigationTarget: null, targetId: null }));
    feedServiceSpy.batchMarkRead.and.returnValue(of({ count: 1 }));
    feedServiceSpy.batchMarkReadByIds.and.returnValue(of({ count: 1 }));
    feedServiceSpy.batchDismiss.and.returnValue(of({ count: 1 }));
    feedServiceSpy.batchAct.and.returnValue(of({ successCount: 1, failures: [] }));

    const feedHubServiceSpy = jasmine.createSpyObj('FeedHubService', ['connect'], {
      feedItemReceived$: feedItemSubject.asObservable(),
      summaryUpdated$: summarySubject.asObservable(),
    });

    TestBed.configureTestingModule({
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedServiceSpy },
        { provide: FeedHubService, useValue: feedHubServiceSpy },
      ],
    });

    store = TestBed.inject(FeedStore);
  });

  describe('onInit', () => {
    it('should have initial items from onInit load', () => {
      expect(store.items()).toEqual(mockItems);
      expect(store.totalCount()).toBe(2);
    });

    it('should load summary on init', () => {
      expect(store.summary()).toEqual(mockSummary);
    });

    it('should load trending on init', () => {
      expect(store.trendingTopics()).toEqual(mockTrending);
    });

    it('should set loading false after items load', () => {
      expect(store.loading()).toBe(false);
    });
  });

  describe('setFilter', () => {
    it('should update activeFilter, reset page, and reload', () => {
      store.setFilter(FeedItemType.TrendAlert);

      expect(store.activeFilter()).toBe(FeedItemType.TrendAlert);
      expect(store.page()).toBe(1);
      expect(feedServiceSpy.list).toHaveBeenCalledTimes(2);
    });
  });

  describe('setPage', () => {
    it('should update page and reload', () => {
      store.setPage(3);

      expect(store.page()).toBe(3);
      expect(feedServiceSpy.list).toHaveBeenCalledTimes(2);
    });
  });

  describe('selection', () => {
    it('should add id when not selected via toggleSelect', () => {
      store.toggleSelect('item-1');

      expect(store.selectedIds()).toContain('item-1');
    });

    it('should remove id when already selected via toggleSelect', () => {
      store.toggleSelect('item-1');
      store.toggleSelect('item-1');

      expect(store.selectedIds()).toEqual([]);
    });

    it('should set selectedIds to all item ids via selectAll', () => {
      store.selectAll();

      expect(store.selectedIds()).toEqual(['item-1', 'item-2']);
    });

    it('should empty selectedIds via clearSelection', () => {
      store.toggleSelect('item-1');
      store.clearSelection();

      expect(store.selectedIds()).toEqual([]);
    });
  });

  describe('computed', () => {
    it('should return true for hasSelection when items selected', () => {
      store.toggleSelect('item-1');

      expect(store.hasSelection()).toBe(true);
    });

    it('should return correct count for selectedCount', () => {
      store.toggleSelect('item-1');
      store.toggleSelect('item-2');

      expect(store.selectedCount()).toBe(2);
    });
  });

  describe('markRead', () => {
    it('should call service and update item isRead', () => {
      store.markRead('item-1');

      expect(feedServiceSpy.markRead).toHaveBeenCalledWith('item-1');
      expect(store.items().find(i => i.id === 'item-1')!.isRead).toBe(true);
    });
  });

  describe('actOnItem', () => {
    it('should call service and mark acted on', () => {
      store.actOnItem('item-1', 'approve');

      expect(feedServiceSpy.actOnItem).toHaveBeenCalledWith('item-1', 'approve');
      expect(store.items().find(i => i.id === 'item-1')!.isActedOn).toBe(true);
    });
  });

  describe('batchMarkRead', () => {
    it('should call service and reload', () => {
      const listCallsBefore = feedServiceSpy.list.calls.count();

      store.batchMarkRead();

      expect(feedServiceSpy.batchMarkRead).toHaveBeenCalled();
      expect(feedServiceSpy.list.calls.count()).toBeGreaterThan(listCallsBefore);
    });
  });

  describe('newItemCount', () => {
    it('should increase by 1 via incrementNewItemCount', () => {
      store.incrementNewItemCount();
      store.incrementNewItemCount();

      expect(store.newItemCount()).toBe(2);
    });

    it('should reset page and newItemCount via loadNewItems', () => {
      store.incrementNewItemCount();
      store.incrementNewItemCount();
      store.loadNewItems();

      expect(store.page()).toBe(1);
      expect(store.newItemCount()).toBe(0);
    });
  });

  describe('SignalR integration', () => {
    it('should increment new item count when feedItemReceived$ emits', () => {
      feedItemSubject.next(mockFeedItem());

      expect(store.newItemCount()).toBe(1);
    });

    it('should update summary when summaryUpdated$ emits', () => {
      const updated = mockFeedSummary({ unreadCount: 99 });
      summarySubject.next(updated);

      expect(store.summary()).toEqual(updated);
    });
  });

  describe('error handling', () => {
    it('should set error state when loadItems fails', () => {
      feedServiceSpy.list.and.returnValue(throwError(() => new Error('Network error')));

      store.setFilter(FeedItemType.AgentDraft);

      expect(store.error()).toBe('Network error');
      expect(store.loading()).toBe(false);
    });

    it('should set error state when markRead fails', () => {
      feedServiceSpy.markRead.and.returnValue(throwError(() => new Error('Mark failed')));

      store.markRead('item-1');

      expect(store.error()).toBe('Mark failed');
    });

    it('should set error state when actOnItem fails', () => {
      feedServiceSpy.actOnItem.and.returnValue(throwError(() => new Error('Act failed')));

      store.actOnItem('item-1', 'approve');

      expect(store.error()).toBe('Act failed');
    });
  });

  describe('batchDismiss', () => {
    it('should call service and reload', () => {
      const listCallsBefore = feedServiceSpy.list.calls.count();

      store.batchDismiss(FeedItemType.SystemNotification);

      expect(feedServiceSpy.batchDismiss).toHaveBeenCalledWith(FeedItemType.SystemNotification);
      expect(feedServiceSpy.list.calls.count()).toBeGreaterThan(listCallsBefore);
    });
  });

  describe('batchMarkReadByIds', () => {
    it('should call service, mark items read, and clear selection', () => {
      store.toggleSelect('item-1');

      store.batchMarkReadByIds(['item-1']);

      expect(feedServiceSpy.batchMarkReadByIds).toHaveBeenCalledWith(['item-1'], true);
      expect(store.items().find(i => i.id === 'item-1')!.isRead).toBe(true);
      expect(store.selectedIds()).toEqual([]);
    });
  });

  describe('batchAct', () => {
    it('should call service, clear selection, and store failures', () => {
      const failures = [{ id: 'item-2', reason: 'expired' }];
      feedServiceSpy.batchAct.and.returnValue(of({ successCount: 1, failures }));

      store.toggleSelect('item-1');
      store.toggleSelect('item-2');
      store.batchAct(['item-1', 'item-2'], 'approve');

      expect(feedServiceSpy.batchAct).toHaveBeenCalledWith(['item-1', 'item-2'], 'approve');
      expect(store.selectedIds()).toEqual([]);
      expect(store.lastBatchFailures()).toEqual(failures);
    });
  });

  describe('isAllSelected', () => {
    it('should return true when all items are selected', () => {
      store.selectAll();

      expect(store.isAllSelected()).toBe(true);
    });

    it('should return false when only some items are selected', () => {
      store.toggleSelect('item-1');

      expect(store.isAllSelected()).toBe(false);
    });
  });
});
