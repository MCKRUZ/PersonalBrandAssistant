import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FeedBatchToolbarComponent } from './feed-batch-toolbar.component';
import { FeedStore } from '../store/feed.store';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import type { FeedItem } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('FeedBatchToolbarComponent', () => {
  let fixture: ComponentFixture<FeedBatchToolbarComponent>;
  let store: InstanceType<typeof FeedStore>;

  const mockItem = (id: string): FeedItem => ({
    id, type: FeedItemType.TrendAlert, title: 'Test',
    summary: 'Test', data: null, actionType: null, actionTargetId: null,
    priority: FeedItemPriority.Normal, isRead: false, isActedOn: false,
    createdAt: '2026-01-01T00:00:00Z', expiresAt: null,
  });

  const mockSummary: FeedSummary = {
    unreadCount: 5, pendingApprovals: 2, trendingCount: 3, engagementDelta: 1.5,
  };

  beforeEach(() => {
    const feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchMarkReadByIds', 'batchDismiss', 'batchAct',
    ]);
    const mockPage: PagedResult<FeedItem> = {
      items: [mockItem('item-1'), mockItem('item-2'), mockItem('item-3')],
      totalCount: 3, page: 1, pageSize: 20, totalPages: 1,
    };
    feedService.list.and.returnValue(of(mockPage));
    feedService.getSummary.and.returnValue(of(mockSummary));
    feedService.getTrending.and.returnValue(of([]));
    feedService.batchAct.and.returnValue(of({ failures: [] }));
    feedService.batchMarkRead.and.returnValue(of(undefined));
    feedService.batchMarkReadByIds.and.returnValue(of({ count: 1 }));

    TestBed.configureTestingModule({
      imports: [FeedBatchToolbarComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
      ],
    });

    store = TestBed.inject(FeedStore);
    fixture = TestBed.createComponent(FeedBatchToolbarComponent);
    fixture.detectChanges();
  });

  it('hides toolbar when store.hasSelection is false', () => {
    const toolbar = fixture.nativeElement.querySelector('.batch-toolbar');
    expect(toolbar).toBeFalsy();
  });

  it('shows toolbar when store.hasSelection is true', () => {
    store.toggleSelect('item-1');
    fixture.detectChanges();

    const toolbar = fixture.nativeElement.querySelector('.batch-toolbar');
    expect(toolbar).toBeTruthy();
  });

  it('displays selected count', () => {
    store.toggleSelect('item-1');
    store.toggleSelect('item-2');
    fixture.detectChanges();

    const count = fixture.nativeElement.querySelector('[data-testid="selected-count"]');
    expect(count.textContent).toContain('2');
  });

  it('Approve button calls store.batchAct with selected IDs and approve', () => {
    store.toggleSelect('item-1');
    store.toggleSelect('item-2');
    fixture.detectChanges();

    spyOn(store, 'batchAct');
    const btn = fixture.nativeElement.querySelector('[data-testid="btn-approve"]');
    btn.click();
    expect(store.batchAct).toHaveBeenCalledWith(['item-1', 'item-2'], 'approve');
  });

  it('Dismiss button calls store.batchAct with selected IDs and dismiss', () => {
    store.toggleSelect('item-1');
    fixture.detectChanges();

    spyOn(store, 'batchAct');
    const btn = fixture.nativeElement.querySelector('[data-testid="btn-dismiss"]');
    btn.click();
    expect(store.batchAct).toHaveBeenCalledWith(['item-1'], 'dismiss');
  });

  it('Mark Read button calls store.batchMarkReadByIds with selected IDs', () => {
    store.toggleSelect('item-1');
    fixture.detectChanges();

    spyOn(store, 'batchMarkReadByIds');
    const btn = fixture.nativeElement.querySelector('[data-testid="btn-mark-read"]');
    btn.click();
    expect(store.batchMarkReadByIds).toHaveBeenCalledWith(['item-1']);
  });

  it('Clear button calls store.clearSelection', () => {
    store.toggleSelect('item-1');
    fixture.detectChanges();

    spyOn(store, 'clearSelection');
    const btn = fixture.nativeElement.querySelector('[data-testid="btn-clear"]');
    btn.click();
    expect(store.clearSelection).toHaveBeenCalled();
  });
});
