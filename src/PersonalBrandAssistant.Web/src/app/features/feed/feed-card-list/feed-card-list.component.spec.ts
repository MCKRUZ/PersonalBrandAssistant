import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeedCardListComponent } from './feed-card-list.component';
import { FeedItem, FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import { Component, input, viewChild } from '@angular/core';

function createFeedItem(overrides: Partial<FeedItem> = {}): FeedItem {
  return {
    id: 'item-1',
    type: FeedItemType.AgentDraft,
    title: 'Test Title',
    summary: 'Test summary',
    data: null,
    actionType: null,
    actionTargetId: null,
    priority: FeedItemPriority.Normal,
    isRead: false,
    isActedOn: false,
    createdAt: new Date().toISOString(),
    expiresAt: null,
    ...overrides,
  };
}

@Component({
  standalone: true,
  imports: [FeedCardListComponent],
  template: `<app-feed-card-list [items]="items()" [loading]="loading()" [selectedIds]="selectedIds()" />`,
})
class TestHostComponent {
  readonly items = input<FeedItem[]>([]);
  readonly loading = input(false);
  readonly selectedIds = input<string[]>([]);
  readonly list = viewChild(FeedCardListComponent);
}

describe('FeedCardListComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
  });

  function setup(items: FeedItem[] = [], loading = false, selectedIds: string[] = []) {
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('loading', loading);
    fixture.componentRef.setInput('selectedIds', selectedIds);
    fixture.detectChanges();
  }

  it('should render FeedCard for each item in items input', () => {
    const items = [
      createFeedItem({ id: '1' }),
      createFeedItem({ id: '2' }),
      createFeedItem({ id: '3' }),
    ];
    setup(items);

    const cards = fixture.nativeElement.querySelectorAll('app-feed-card');
    expect(cards.length).toBe(3);
  });

  it('should show 5 skeleton cards when loading is true', () => {
    setup([], true);

    const skeletons = fixture.nativeElement.querySelectorAll('[data-testid="skeleton-card"]');
    expect(skeletons.length).toBe(5);
    expect(fixture.nativeElement.querySelector('[data-testid="skeleton-list"]')).toBeTruthy();
  });

  it('should show empty state when loading is false and items is empty', () => {
    setup([], false);

    const emptyState = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain("You're all caught up!");
  });

  it('should not show skeleton or empty state when items are present', () => {
    setup([createFeedItem()]);

    expect(fixture.nativeElement.querySelector('[data-testid="skeleton-list"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="empty-state"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="card-list"]')).toBeTruthy();
  });

  it('should emit action event when card action triggered', () => {
    setup([createFeedItem({ id: 'item-1' })]);
    let emitted: { id: string; action: string } | undefined;
    fixture.componentInstance.list()!.action.subscribe(e => emitted = e);

    const btn = fixture.nativeElement.querySelector('[data-testid="primary-action"]') as HTMLButtonElement;
    btn.click();

    expect(emitted).toEqual({ id: 'item-1', action: 'approve' });
  });

  it('should emit select event when card checkbox toggled', () => {
    setup([createFeedItem({ id: 'item-1' })]);
    let emitted: string | undefined;
    fixture.componentInstance.list()!.select.subscribe(e => emitted = e);

    const checkbox = fixture.nativeElement.querySelector('[data-testid="card-checkbox"]') as HTMLInputElement;
    checkbox.dispatchEvent(new Event('change'));

    expect(emitted).toBe('item-1');
  });
});
