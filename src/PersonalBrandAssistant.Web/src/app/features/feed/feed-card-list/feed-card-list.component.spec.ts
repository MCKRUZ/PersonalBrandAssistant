import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedCardListComponent } from './feed-card-list.component';
import { mockFeedItem } from '../testing/feed-test-utils';

describe('FeedCardListComponent', () => {
  let fixture: ComponentFixture<FeedCardListComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FeedCardListComponent],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(FeedCardListComponent);
    fixture.detectChanges();
  });

  it('should create component', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render feed cards for each item', () => {
    const items = [
      mockFeedItem({ id: '1' }),
      mockFeedItem({ id: '2' }),
      mockFeedItem({ id: '3' }),
    ];
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('app-feed-card');
    expect(cards.length).toBe(3);
  });

  it('should show skeleton when loading', () => {
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    const skeletonList = fixture.nativeElement.querySelector('[data-testid="skeleton-list"]');
    expect(skeletonList).toBeTruthy();

    const skeletonCards = fixture.nativeElement.querySelectorAll('[data-testid="skeleton-card"]');
    expect(skeletonCards.length).toBe(5);
  });

  it('should show empty state when not loading and no items', () => {
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('items', []);
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(emptyState).toBeTruthy();
  });

  it('should render cards when selectedIds provided', () => {
    const items = [mockFeedItem({ id: 'x1' }), mockFeedItem({ id: 'x2' })];
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('selectedIds', ['x1']);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('app-feed-card');
    expect(cards.length).toBe(2);
  });
});
