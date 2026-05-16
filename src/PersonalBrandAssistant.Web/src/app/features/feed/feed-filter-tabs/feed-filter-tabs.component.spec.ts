import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ActivatedRoute, Router } from '@angular/router';
import { FeedFilterTabsComponent } from './feed-filter-tabs.component';
import { FeedStore } from '../store/feed.store';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { FeedItem } from '../models/feed-item.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('FeedFilterTabsComponent', () => {
  let fixture: ComponentFixture<FeedFilterTabsComponent>;
  let store: InstanceType<typeof FeedStore>;
  let router: jasmine.SpyObj<Router>;

  const emptyPage: PagedResult<FeedItem> = {
    items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0,
  };

  const mockSummary: FeedSummary = {
    unreadCount: 12, pendingApprovals: 3, trendingCount: 7, engagementDelta: 4.2,
  };

  function setup(queryParams: Record<string, string> = {}): void {
    const feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchMarkReadByIds', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of(emptyPage));
    feedService.getSummary.and.returnValue(of(mockSummary));
    feedService.getTrending.and.returnValue(of([]));

    router = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [FeedFilterTabsComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { queryParams: of(queryParams) } },
      ],
    });

    store = TestBed.inject(FeedStore);
    fixture = TestBed.createComponent(FeedFilterTabsComponent);
    fixture.detectChanges();
  }

  beforeEach(() => setup());

  it('renders All, Drafts, Trends, Ideas, Analytics, Approvals tabs', () => {
    const tabs = fixture.nativeElement.querySelectorAll('.tab');
    expect(tabs.length).toBe(6);
    expect(tabs[0].textContent).toContain('All');
    expect(tabs[1].textContent).toContain('Drafts');
    expect(tabs[2].textContent).toContain('Trends');
    expect(tabs[3].textContent).toContain('Ideas');
    expect(tabs[4].textContent).toContain('Analytics');
    expect(tabs[5].textContent).toContain('Approvals');
  });

  it('highlights active tab based on store.activeFilter', () => {
    store.setFilter(FeedItemType.TrendAlert);
    fixture.detectChanges();

    const activeTab = fixture.nativeElement.querySelector('.tab.active');
    expect(activeTab).toBeTruthy();
    expect(activeTab.textContent).toContain('Trends');
  });

  it('click on tab calls store.setFilter with correct type', () => {
    spyOn(store, 'setFilter');
    const trendsTab = fixture.nativeElement.querySelector('[data-testid="tab-trends"]');
    trendsTab.click();
    expect(store.setFilter).toHaveBeenCalledWith(FeedItemType.TrendAlert);
  });

  it('"All" tab passes null to setFilter', () => {
    store.setFilter(FeedItemType.TrendAlert);
    fixture.detectChanges();

    spyOn(store, 'setFilter');
    const allTab = fixture.nativeElement.querySelector('[data-testid="tab-all"]');
    allTab.click();
    expect(store.setFilter).toHaveBeenCalledWith(null);
  });

  it('shows unread count badge on All tab', () => {
    const badge = fixture.nativeElement.querySelector('[data-testid="tab-all"] .badge');
    expect(badge).toBeTruthy();
    expect(badge.textContent.trim()).toBe('12');
  });

  it('shows trending count badge on Trends tab', () => {
    const badge = fixture.nativeElement.querySelector('[data-testid="tab-trends"] .badge');
    expect(badge).toBeTruthy();
    expect(badge.textContent.trim()).toBe('7');
  });

  it('shows pending approvals badge on Approvals tab', () => {
    const badge = fixture.nativeElement.querySelector('[data-testid="tab-approvals"] .badge');
    expect(badge).toBeTruthy();
    expect(badge.textContent.trim()).toBe('3');
  });

  it('does not show badge on tabs without counts', () => {
    const draftsBadge = fixture.nativeElement.querySelector('[data-testid="tab-drafts"] .badge');
    const ideasBadge = fixture.nativeElement.querySelector('[data-testid="tab-ideas"] .badge');
    const analyticsBadge = fixture.nativeElement.querySelector('[data-testid="tab-analytics"] .badge');
    expect(draftsBadge).toBeFalsy();
    expect(ideasBadge).toBeFalsy();
    expect(analyticsBadge).toBeFalsy();
  });

  it('updates URL query params on tab change', () => {
    const trendsTab = fixture.nativeElement.querySelector('[data-testid="tab-trends"]');
    trendsTab.click();
    expect(router.navigate).toHaveBeenCalledWith([], {
      queryParams: { type: FeedItemType.TrendAlert },
      queryParamsHandling: 'merge',
    });
  });

  it('removes type query param when All tab selected', () => {
    const allTab = fixture.nativeElement.querySelector('[data-testid="tab-all"]');
    allTab.click();
    expect(router.navigate).toHaveBeenCalledWith([], {
      queryParams: { type: null },
      queryParamsHandling: 'merge',
    });
  });
});

describe('FeedFilterTabsComponent (URL init)', () => {
  it('reads initial filter from URL query params', () => {
    const feedService = jasmine.createSpyObj('FeedService', [
      'list', 'getSummary', 'getTrending',
      'markRead', 'actOnItem', 'batchMarkRead', 'batchMarkReadByIds', 'batchDismiss', 'batchAct',
    ]);
    feedService.list.and.returnValue(of({
      items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0,
    }));
    feedService.getSummary.and.returnValue(of({
      unreadCount: 0, pendingApprovals: 0, trendingCount: 0, engagementDelta: 0,
    }));
    feedService.getTrending.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [FeedFilterTabsComponent],
      providers: [
        FeedStore,
        { provide: FeedService, useValue: feedService },
        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
        { provide: ActivatedRoute, useValue: { queryParams: of({ type: 'TrendAlert' }) } },
      ],
    });

    const store = TestBed.inject(FeedStore);
    TestBed.createComponent(FeedFilterTabsComponent).detectChanges();

    expect(store.activeFilter()).toBe(FeedItemType.TrendAlert);
  });
});
