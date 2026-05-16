import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { FeedPageComponent } from './feed-page.component';
import { FeedStore } from '../store/feed.store';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import type { FeedItem } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('FeedPageComponent', () => {
  let component: FeedPageComponent;
  let fixture: ComponentFixture<FeedPageComponent>;
  let store: InstanceType<typeof FeedStore>;
  let feedService: jasmine.SpyObj<FeedService>;
  let feedItemSubject: Subject<FeedItem>;

  const mockSummary: FeedSummary = {
    unreadCount: 5, pendingApprovals: 2, trendingCount: 3, engagementDelta: 1.5,
  };

  const mockItem: FeedItem = {
    id: 'feed-1', type: FeedItemType.TrendAlert, title: 'Test',
    summary: 'Test', data: null, actionType: null, actionTargetId: null,
    priority: FeedItemPriority.Normal, isRead: false, isActedOn: false,
    createdAt: '2026-01-01T00:00:00Z', expiresAt: null,
  };

  const emptyPage: PagedResult<FeedItem> = {
    items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0,
  };

  beforeEach(async () => {
    feedItemSubject = new Subject<FeedItem>();

    feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of(emptyPage));
    feedService.getSummary.and.returnValue(of(mockSummary));
    feedService.getTrending.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [FeedPageComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        {
          provide: FeedHubService,
          useValue: {
            feedItemReceived$: feedItemSubject.asObservable(),
            summaryUpdated$: of(),
          },
        },
      ],
    }).compileComponents();

    store = TestBed.inject(FeedStore);
    fixture = TestBed.createComponent(FeedPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders FeedStatsBar component', () => {
    const statsBar = fixture.nativeElement.querySelector('app-feed-stats-bar');
    expect(statsBar).toBeTruthy();
  });

  it('renders filter tabs placeholder', () => {
    const placeholder = fixture.nativeElement.querySelector('[data-testid="filter-tabs-slot"]');
    expect(placeholder).toBeTruthy();
  });

  it('renders card list placeholder', () => {
    const placeholder = fixture.nativeElement.querySelector('[data-testid="card-list-slot"]');
    expect(placeholder).toBeTruthy();
  });

  it('renders sidebar placeholder', () => {
    const placeholder = fixture.nativeElement.querySelector('[data-testid="sidebar-slot"]');
    expect(placeholder).toBeTruthy();
  });

  it('hides batch toolbar when no items selected', () => {
    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar-slot"]');
    expect(toolbar).toBeFalsy();
  });

  it('renders batch toolbar when items are selected', () => {
    store.toggleSelect('feed-1');
    fixture.detectChanges();

    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar-slot"]');
    expect(toolbar).toBeTruthy();
  });

  it('hides new items banner when newItemCount is 0', () => {
    const banner = fixture.nativeElement.querySelector('[data-testid="new-items-banner-slot"]');
    expect(banner).toBeFalsy();
  });

  it('renders new items banner when newItemCount > 0', () => {
    feedItemSubject.next(mockItem);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="new-items-banner-slot"]');
    expect(banner).toBeTruthy();
  });

  it('hides paginator when totalCount <= pageSize', () => {
    const paginator = fixture.nativeElement.querySelector('[data-testid="paginator"]');
    expect(paginator).toBeFalsy();
  });

  it('renders paginator when totalCount > pageSize', () => {
    const largePage: PagedResult<FeedItem> = {
      items: [mockItem], totalCount: 50, page: 1, pageSize: 20, totalPages: 3,
    };
    feedService.list.and.returnValue(of(largePage));
    store.loadItems();
    fixture.detectChanges();

    const paginator = fixture.nativeElement.querySelector('[data-testid="paginator"]');
    expect(paginator).toBeTruthy();
  });

  it('uses CSS Grid two-column layout', () => {
    const grid = fixture.nativeElement.querySelector('.feed-grid');
    expect(grid).toBeTruthy();
  });
});
