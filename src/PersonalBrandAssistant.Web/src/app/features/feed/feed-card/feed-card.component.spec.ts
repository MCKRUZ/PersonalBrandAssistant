import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ComponentRef, NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedCardComponent } from './feed-card.component';
import { FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import { mockFeedItem } from '../testing/feed-test-utils';

describe('FeedCardComponent', () => {
  let fixture: ComponentFixture<FeedCardComponent>;
  let componentRef: ComponentRef<FeedCardComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FeedCardComponent],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(FeedCardComponent);
    componentRef = fixture.componentRef;
    componentRef.setInput('item', mockFeedItem());
    fixture.detectChanges();
  });

  function setItem(overrides: Parameters<typeof mockFeedItem>[0]) {
    componentRef.setInput('item', mockFeedItem(overrides));
    fixture.detectChanges();
  }

  function query(testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  it('should render blue border for AgentDraft', () => {
    setItem({ type: FeedItemType.AgentDraft });
    const card = query('feed-card')!;
    expect(card.style.borderLeftColor).toBe('rgb(59, 130, 246)');
  });

  it('should render orange border for TrendAlert', () => {
    setItem({ type: FeedItemType.TrendAlert });
    const card = query('feed-card')!;
    expect(card.style.borderLeftColor).toBe('rgb(249, 115, 22)');
  });

  it('should render purple border for IdeaSuggestion', () => {
    setItem({ type: FeedItemType.IdeaSuggestion });
    const card = query('feed-card')!;
    expect(card.style.borderLeftColor).toBe('rgb(168, 85, 247)');
  });

  it('should render green border for AnalyticsHighlight', () => {
    setItem({ type: FeedItemType.AnalyticsHighlight });
    const card = query('feed-card')!;
    expect(card.style.borderLeftColor).toBe('rgb(34, 197, 94)');
  });

  it('should render correct icon class per type', () => {
    setItem({ type: FeedItemType.AgentDraft });
    const icon = query('type-icon')!;
    expect(icon.classList.contains('pi-bolt')).toBeTrue();
  });

  it('should show priority badge for High', () => {
    setItem({ priority: FeedItemPriority.High });
    expect(query('priority-badge')).toBeTruthy();
  });

  it('should hide priority badge for Normal', () => {
    setItem({ priority: FeedItemPriority.Normal });
    expect(query('priority-badge')).toBeNull();
  });

  it('should add pulse class for Urgent', () => {
    setItem({ priority: FeedItemPriority.Urgent });
    const badge = query('priority-badge')!;
    expect(badge.classList.contains('pulse')).toBeTrue();
  });

  it('should show Approve button for AgentDraft', () => {
    setItem({ type: FeedItemType.AgentDraft });
    const btn = query('primary-action')!;
    expect(btn.textContent!.trim()).toBe('Approve');
  });

  it('should show View for TrendAlert', () => {
    setItem({ type: FeedItemType.TrendAlert });
    const btn = query('primary-action')!;
    expect(btn.textContent!.trim()).toBe('View');
  });

  it('should show Create Content for IdeaSuggestion', () => {
    setItem({ type: FeedItemType.IdeaSuggestion });
    const btn = query('primary-action')!;
    expect(btn.textContent!.trim()).toBe('Create Content');
  });

  it('should show Dismiss button for all types', () => {
    for (const type of Object.values(FeedItemType)) {
      setItem({ type });
      expect(query('action-dismiss')).toBeTruthy(`Dismiss missing for ${type}`);
    }
  });

  it('should add is-read class when item is read', () => {
    setItem({ isRead: true });
    const card = query('feed-card')!;
    expect(card.classList.contains('is-read')).toBeTrue();
  });

  it('should emit action on primary button click', () => {
    const itemId = 'emit-test-id';
    setItem({ id: itemId, type: FeedItemType.AgentDraft });

    let emitted: { id: string; action: string } | undefined;
    fixture.componentInstance.action.subscribe((v) => (emitted = v));

    query('primary-action')!.click();

    expect(emitted).toEqual({ id: itemId, action: 'approve' });
  });
});
