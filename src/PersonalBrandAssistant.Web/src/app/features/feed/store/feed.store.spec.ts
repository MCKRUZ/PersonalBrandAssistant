import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { FeedStore } from './feed.store';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import type { FeedItem } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { TrendingTopic } from '../models/trending-topic.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('FeedStore', () => {
  let store: InstanceType<typeof FeedStore>;
  let feedService: jasmine.SpyObj<FeedService>;
  let feedItemSubject: Subject<FeedItem>;
  let summarySubject: Subject<FeedSummary>;

  const emptyPage: PagedResult<FeedItem> = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 0,
  };

  const mockSummary: FeedSummary = {
    unreadCount: 5,
    pendingApprovals: 2,
    trendingCount: 3,
    engagementDelta: 1.5,
  };

  const mockTrending: TrendingTopic[] = [
    { topic: 'AI', count: 10, latestAt: '2026-01-01T00:00:00Z' },
  ];

  const mockItem: FeedItem = {
    id: 'feed-1',
    type: FeedItemType.TrendAlert,
    title: 'Test Feed Item',
    summary: 'Test summary',
    data: null,
    actionType: null,
    actionTargetId: null,
    priority: FeedItemPriority.Normal,
    isRead: false,
    isActedOn: false,
    createdAt: '2026-01-01T00:00:00Z',
    expiresAt: null,
  };

  const mockItem2: FeedItem = {
    ...mockItem,
    id: 'feed-2',
    title: 'Another Feed Item',
  };

  beforeEach(() => {
    feedItemSubject = new Subject<FeedItem>();
    summarySubject = new Subject<FeedSummary>();

    feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem',
      'batchMarkRead', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of(emptyPage));
    feedService.getSummary.and.returnValue(of(mockSummary));
    feedService.getTrending.and.returnValue(of(mockTrending));
    feedService.markRead.and.returnValue(of(void 0));
    feedService.actOnItem.and.returnValue(of({ success: true, navigationTarget: null, targetId: null }));
    feedService.batchMarkRead.and.returnValue(of({ count: 0 }));
    feedService.batchDismiss.and.returnValue(of({ count: 0 }));
    feedService.batchAct.and.returnValue(of({ successCount: 0, failures: [] }));

    TestBed.configureTestingModule({
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        {
          provide: FeedHubService,
          useValue: {
            feedItemReceived$: feedItemSubject.asObservable(),
            summaryUpdated$: summarySubject.asObservable(),
            connect: jasmine.createSpy('connect').and.returnValue(Promise.resolve()),
            disconnect: jasmine.createSpy('disconnect').and.returnValue(Promise.resolve()),
          },
        },
      ],
    });
    store = TestBed.inject(FeedStore);
  });

  // --- Initial State ---
  it('has correct initial state', () => {
    expect(store.items()).toEqual([]);
    expect(store.totalCount()).toBe(0);
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(20);
    expect(store.activeFilter()).toBeNull();
    expect(store.loading()).toBeFalse();
    expect(store.error()).toBeNull();
    expect(store.selectedIds()).toEqual([]);
    expect(store.newItemCount()).toBe(0);
  });

  // --- onInit ---
  it('onInit calls loadItems, loadSummary, loadTrending', () => {
    expect(feedService.list).toHaveBeenCalled();
    expect(feedService.getSummary).toHaveBeenCalled();
    expect(feedService.getTrending).toHaveBeenCalled();
  });

  it('onInit subscribes to feedHubService observables', () => {
    expect(store.summary()).toEqual(mockSummary);
    expect(store.trendingTopics()).toEqual(mockTrending);
  });

  // --- Data Loading ---
  it('loadItems patches items and totalCount from service', () => {
    const page: PagedResult<FeedItem> = {
      items: [mockItem],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    feedService.list.and.returnValue(of(page));

    store.loadItems();

    expect(store.items()).toEqual([mockItem]);
    expect(store.totalCount()).toBe(1);
    expect(store.loading()).toBeFalse();
  });

  it('loadItems handles errors', () => {
    feedService.list.and.returnValue(throwError(() => new Error('Network error')));

    store.loadItems();

    expect(store.loading()).toBeFalse();
    expect(store.error()).toBe('Network error');
  });

  it('loadSummary patches summary from service', () => {
    const newSummary: FeedSummary = { ...mockSummary, unreadCount: 10 };
    feedService.getSummary.and.returnValue(of(newSummary));

    store.loadSummary();

    expect(store.summary()).toEqual(newSummary);
  });

  it('loadTrending patches trendingTopics from service', () => {
    const topics: TrendingTopic[] = [
      { topic: 'ML', count: 5, latestAt: '2026-02-01T00:00:00Z' },
    ];
    feedService.getTrending.and.returnValue(of(topics));

    store.loadTrending();

    expect(store.trendingTopics()).toEqual(topics);
  });

  // --- Filtering/Pagination ---
  it('setFilter updates activeFilter, resets page to 1, triggers reload', () => {
    store.setPage(3);
    feedService.list.calls.reset();

    store.setFilter(FeedItemType.TrendAlert);

    expect(store.activeFilter()).toBe(FeedItemType.TrendAlert);
    expect(store.page()).toBe(1);
    expect(feedService.list).toHaveBeenCalled();
  });

  it('setFilter with null clears filter', () => {
    store.setFilter(FeedItemType.TrendAlert);
    feedService.list.calls.reset();

    store.setFilter(null);

    expect(store.activeFilter()).toBeNull();
    expect(feedService.list).toHaveBeenCalled();
  });

  it('setPage updates page and triggers reload', () => {
    feedService.list.calls.reset();

    store.setPage(3);

    expect(store.page()).toBe(3);
    expect(feedService.list).toHaveBeenCalled();
  });

  // --- Selection ---
  it('toggleSelect adds id when not present', () => {
    store.toggleSelect('feed-1');

    expect(store.selectedIds()).toEqual(['feed-1']);
  });

  it('toggleSelect removes id when present', () => {
    store.toggleSelect('feed-1');
    store.toggleSelect('feed-1');

    expect(store.selectedIds()).toEqual([]);
  });

  it('selectAll sets selectedIds to all item IDs', () => {
    const page: PagedResult<FeedItem> = {
      items: [mockItem, mockItem2],
      totalCount: 2,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    feedService.list.and.returnValue(of(page));
    store.loadItems();

    store.selectAll();

    expect(store.selectedIds()).toEqual(['feed-1', 'feed-2']);
  });

  it('clearSelection sets selectedIds to empty array', () => {
    store.toggleSelect('feed-1');

    store.clearSelection();

    expect(store.selectedIds()).toEqual([]);
  });

  // --- Computed ---
  it('hasSelection returns true when selectedIds non-empty', () => {
    expect(store.hasSelection()).toBeFalse();

    store.toggleSelect('feed-1');

    expect(store.hasSelection()).toBeTrue();
  });

  it('selectedCount returns correct count', () => {
    expect(store.selectedCount()).toBe(0);

    store.toggleSelect('feed-1');
    store.toggleSelect('feed-2');

    expect(store.selectedCount()).toBe(2);
  });

  it('isAllSelected returns true when all items selected', () => {
    const page: PagedResult<FeedItem> = {
      items: [mockItem, mockItem2],
      totalCount: 2,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    feedService.list.and.returnValue(of(page));
    store.loadItems();

    expect(store.isAllSelected()).toBeFalse();

    store.selectAll();

    expect(store.isAllSelected()).toBeTrue();
  });

  // --- Actions ---
  it('markRead error sets error state', () => {
    feedService.markRead.and.returnValue(throwError(() => new Error('Not found')));

    store.markRead('feed-1');

    expect(store.error()).toBe('Not found');
  });

  it('markRead calls service and updates local item state', () => {
    const page: PagedResult<FeedItem> = {
      items: [mockItem],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    feedService.list.and.returnValue(of(page));
    store.loadItems();

    store.markRead('feed-1');

    expect(feedService.markRead).toHaveBeenCalledWith('feed-1');
    expect(store.items()[0].isRead).toBeTrue();
  });

  it('actOnItem calls service and marks item as acted on', () => {
    const page: PagedResult<FeedItem> = {
      items: [mockItem],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    feedService.list.and.returnValue(of(page));
    store.loadItems();

    store.actOnItem('feed-1', 'approve');

    expect(feedService.actOnItem).toHaveBeenCalledWith('feed-1', 'approve');
    expect(store.items()[0].isActedOn).toBeTrue();
  });

  it('batchMarkRead calls service, reloads items and summary', () => {
    feedService.batchMarkRead.and.returnValue(of({ count: 3 }));
    feedService.list.calls.reset();
    feedService.getSummary.calls.reset();

    store.batchMarkRead(FeedItemType.TrendAlert);

    expect(feedService.batchMarkRead).toHaveBeenCalledWith(FeedItemType.TrendAlert, true);
    expect(feedService.list).toHaveBeenCalled();
    expect(feedService.getSummary).toHaveBeenCalled();
  });

  it('batchDismiss calls service and reloads', () => {
    feedService.batchDismiss.and.returnValue(of({ count: 2 }));
    feedService.list.calls.reset();
    feedService.getSummary.calls.reset();

    store.batchDismiss(FeedItemType.SystemNotification);

    expect(feedService.batchDismiss).toHaveBeenCalledWith(FeedItemType.SystemNotification);
    expect(feedService.list).toHaveBeenCalled();
    expect(feedService.getSummary).toHaveBeenCalled();
  });

  it('batchMarkRead with isRead=false passes false to service', () => {
    feedService.batchMarkRead.and.returnValue(of({ count: 2 }));

    store.batchMarkRead(FeedItemType.TrendAlert, false);

    expect(feedService.batchMarkRead).toHaveBeenCalledWith(FeedItemType.TrendAlert, false);
  });

  it('batchAct calls service, reloads, clears selection', () => {
    store.toggleSelect('feed-1');
    feedService.batchAct.and.returnValue(of({ successCount: 1, failures: [] }));
    feedService.list.calls.reset();
    feedService.getSummary.calls.reset();

    store.batchAct(['feed-1'], 'approve');

    expect(feedService.batchAct).toHaveBeenCalledWith(['feed-1'], 'approve');
    expect(feedService.list).toHaveBeenCalled();
    expect(feedService.getSummary).toHaveBeenCalled();
    expect(store.selectedIds()).toEqual([]);
  });

  it('batchAct captures partial failures in state', () => {
    const failures = [{ id: 'feed-2', reason: 'Already acted on' }];
    feedService.batchAct.and.returnValue(of({ successCount: 1, failures }));

    store.batchAct(['feed-1', 'feed-2'], 'approve');

    expect(store.lastBatchFailures()).toEqual(failures);
  });

  // --- SignalR ---
  it('incrementNewItemCount increases counter by 1', () => {
    feedItemSubject.next(mockItem);

    expect(store.newItemCount()).toBe(1);

    feedItemSubject.next(mockItem2);

    expect(store.newItemCount()).toBe(2);
  });

  it('loadNewItems resets page, reloads, resets newItemCount to 0', () => {
    feedItemSubject.next(mockItem);
    feedItemSubject.next(mockItem2);
    expect(store.newItemCount()).toBe(2);

    feedService.list.calls.reset();
    store.loadNewItems();

    expect(store.page()).toBe(1);
    expect(store.newItemCount()).toBe(0);
    expect(feedService.list).toHaveBeenCalled();
  });

  it('updateSummary patches summary directly from SignalR', () => {
    const newSummary: FeedSummary = { ...mockSummary, unreadCount: 99 };
    summarySubject.next(newSummary);

    expect(store.summary()).toEqual(newSummary);
  });
});
