import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NEVER, Observable, of } from 'rxjs';
import { FeedStatsBarComponent } from './feed-stats-bar.component';
import { FeedStore } from '../store/feed.store';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { FeedItem } from '../models/feed-item.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('FeedStatsBarComponent', () => {
  let component: FeedStatsBarComponent;
  let fixture: ComponentFixture<FeedStatsBarComponent>;
  let store: InstanceType<typeof FeedStore>;

  const emptyPage: PagedResult<FeedItem> = {
    items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0,
  };

  const mockSummary: FeedSummary = {
    unreadCount: 12,
    pendingApprovals: 3,
    trendingCount: 7,
    engagementDelta: 4.2,
  };

  function setup(summary$: Observable<FeedSummary> = of(mockSummary)): void {
    const feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of(emptyPage));
    feedService.getSummary.and.returnValue(summary$);
    feedService.getTrending.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [FeedStatsBarComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        {
          provide: FeedHubService,
          useValue: {
            feedItemReceived$: of(),
            summaryUpdated$: of(),
          },
        },
      ],
    });

    store = TestBed.inject(FeedStore);
    fixture = TestBed.createComponent(FeedStatsBarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => setup());

  it('renders 4 stat cards', () => {
    const cards = fixture.nativeElement.querySelectorAll('.stat-card');
    expect(cards.length).toBe(4);
  });

  it('displays unread count from summary', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-unread"]');
    expect(card.querySelector('.stat-value').textContent.trim()).toBe('12');
  });

  it('displays pending approvals count from summary', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-approvals"]');
    expect(card.querySelector('.stat-value').textContent.trim()).toBe('3');
  });

  it('displays trending count from summary', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-trending"]');
    expect(card.querySelector('.stat-value').textContent.trim()).toBe('7');
  });

  it('displays engagement delta with trend indicator', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-engagement"]');
    const value = card.querySelector('.stat-value').textContent.trim();
    expect(value).toContain('4.2');
  });

  it('shows positive trend arrow for positive delta', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-engagement"]');
    const arrow = card.querySelector('.trend-up');
    expect(arrow).toBeTruthy();
  });

  it('click on approvals card triggers setFilter with ApprovalRequest', () => {
    spyOn(store, 'setFilter');
    const card = fixture.nativeElement.querySelector('[data-testid="stat-approvals"]');

    card.click();

    expect(store.setFilter).toHaveBeenCalledWith(FeedItemType.ApprovalRequest);
  });

  it('click on unread card calls setFilter with null', () => {
    spyOn(store, 'setFilter');
    const card = fixture.nativeElement.querySelector('[data-testid="stat-unread"]');

    card.click();

    expect(store.setFilter).toHaveBeenCalledWith(null);
  });

  it('click on trending card triggers setFilter with TrendAlert', () => {
    spyOn(store, 'setFilter');
    const card = fixture.nativeElement.querySelector('[data-testid="stat-trending"]');

    card.click();

    expect(store.setFilter).toHaveBeenCalledWith(FeedItemType.TrendAlert);
  });

  it('click on engagement card triggers setFilter with AnalyticsHighlight', () => {
    spyOn(store, 'setFilter');
    const card = fixture.nativeElement.querySelector('[data-testid="stat-engagement"]');

    card.click();

    expect(store.setFilter).toHaveBeenCalledWith(FeedItemType.AnalyticsHighlight);
  });
});

describe('FeedStatsBarComponent (negative delta)', () => {
  let fixture: ComponentFixture<FeedStatsBarComponent>;

  beforeEach(() => {
    const negativeSummary: FeedSummary = {
      unreadCount: 5, pendingApprovals: 1, trendingCount: 2, engagementDelta: -3.8,
    };
    const feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 }));
    feedService.getSummary.and.returnValue(of(negativeSummary));
    feedService.getTrending.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [FeedStatsBarComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
      ],
    });
    fixture = TestBed.createComponent(FeedStatsBarComponent);
    fixture.detectChanges();
  });

  it('shows negative trend arrow for negative delta', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-engagement"]');
    expect(card.querySelector('.trend-down')).toBeTruthy();
    expect(card.querySelector('.trend-up')).toBeFalsy();
  });

  it('applies negative CSS class for negative delta', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-engagement"]');
    const value = card.querySelector('.stat-value');
    expect(value.classList.contains('negative')).toBeTrue();
  });
});

describe('FeedStatsBarComponent (skeleton)', () => {
  let fixture: ComponentFixture<FeedStatsBarComponent>;

  beforeEach(() => {
    const feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 }));
    feedService.getSummary.and.returnValue(NEVER);
    feedService.getTrending.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [FeedStatsBarComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
      ],
    });
    fixture = TestBed.createComponent(FeedStatsBarComponent);
    fixture.detectChanges();
  });

  it('shows skeleton state when summary is null', () => {
    const skeletons = fixture.nativeElement.querySelectorAll('.stat-skeleton');
    expect(skeletons.length).toBe(4);
  });
});
