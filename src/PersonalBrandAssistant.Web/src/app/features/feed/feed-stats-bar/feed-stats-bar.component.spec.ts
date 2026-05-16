import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedStatsBarComponent } from './feed-stats-bar.component';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';
import { createMockFeedStore, mockFeedSummary } from '../testing/feed-test-utils';

describe('FeedStatsBarComponent', () => {
  let fixture: ComponentFixture<FeedStatsBarComponent>;
  let mockStore: ReturnType<typeof createMockFeedStore>;

  beforeEach(() => {
    mockStore = createMockFeedStore();
    mockStore.summary.set(mockFeedSummary());

    TestBed.configureTestingModule({
      imports: [FeedStatsBarComponent],
      providers: [{ provide: FeedStore, useValue: mockStore }],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(FeedStatsBarComponent);
    fixture.detectChanges();
  });

  it('should render 4 stat cards when summary exists', () => {
    const cards = fixture.nativeElement.querySelectorAll('.stat-card');
    expect(cards.length).toBe(4);
  });

  it('should display unread count', () => {
    mockStore.summary.set(mockFeedSummary({ unreadCount: 23 }));
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="stat-unread"]');
    expect(card.textContent).toContain('23');
  });

  it('should display pending approvals count', () => {
    mockStore.summary.set(mockFeedSummary({ pendingApprovals: 5 }));
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="stat-approvals"]');
    expect(card.textContent).toContain('5');
  });

  it('should display trending count', () => {
    mockStore.summary.set(mockFeedSummary({ trendingCount: 12 }));
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="stat-trending"]');
    expect(card.textContent).toContain('12');
  });

  it('should display engagement delta', () => {
    mockStore.summary.set(mockFeedSummary({ engagementDelta: 15.5 }));
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-testid="stat-engagement"]');
    expect(card.textContent).toContain('15.5');
  });

  it('should call setFilter on stat card click', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="stat-approvals"]');
    card.click();

    expect(mockStore.setFilter).toHaveBeenCalledWith(FeedItemType.ApprovalRequest);
  });

  it('should show skeleton when summary is null', () => {
    mockStore.summary.set(null);
    fixture.detectChanges();

    const skeletons = fixture.nativeElement.querySelectorAll('.stat-skeleton');
    expect(skeletons.length).toBe(4);
    expect(fixture.nativeElement.querySelector('[data-testid="stat-unread"]')).toBeFalsy();
  });
});
