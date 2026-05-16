import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeedCardComponent } from './feed-card.component';
import { FeedItem, FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import { Component, input, viewChild } from '@angular/core';

function createFeedItem(overrides: Partial<FeedItem> = {}): FeedItem {
  return {
    id: 'item-1',
    type: FeedItemType.AgentDraft,
    title: 'Test Draft Title',
    summary: 'Test summary text for the feed card',
    data: null,
    actionType: null,
    actionTargetId: null,
    priority: FeedItemPriority.Normal,
    isRead: false,
    isActedOn: false,
    createdAt: new Date(Date.now() - 7_200_000).toISOString(),
    expiresAt: null,
    ...overrides,
  };
}

@Component({
  standalone: true,
  imports: [FeedCardComponent],
  template: `<app-feed-card [item]="item()" [selectedIds]="selectedIds()" />`,
})
class TestHostComponent {
  readonly item = input(createFeedItem());
  readonly selectedIds = input<string[]>([]);
  readonly card = viewChild(FeedCardComponent);
}

describe('FeedCardComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
  });

  function setup(item?: Partial<FeedItem>, selectedIds: string[] = []) {
    fixture.componentRef.setInput('item', createFeedItem(item));
    fixture.componentRef.setInput('selectedIds', selectedIds);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('[data-testid="feed-card"]') as HTMLElement;
  }

  describe('type-specific border colors', () => {
    it('should render blue left border for AgentDraft type', () => {
      const card = setup({ type: FeedItemType.AgentDraft });
      expect(card.style.borderLeftColor).toBe('rgb(59, 130, 246)');
    });

    it('should render orange left border for TrendAlert type', () => {
      const card = setup({ type: FeedItemType.TrendAlert });
      expect(card.style.borderLeftColor).toBe('rgb(249, 115, 22)');
    });

    it('should render purple left border for IdeaSuggestion type', () => {
      const card = setup({ type: FeedItemType.IdeaSuggestion });
      expect(card.style.borderLeftColor).toBe('rgb(168, 85, 247)');
    });

    it('should render green left border for AnalyticsHighlight type', () => {
      const card = setup({ type: FeedItemType.AnalyticsHighlight });
      expect(card.style.borderLeftColor).toBe('rgb(34, 197, 94)');
    });
  });

  describe('icons', () => {
    const iconTests: [FeedItemType, string][] = [
      [FeedItemType.AgentDraft, 'pi-bolt'],
      [FeedItemType.TrendAlert, 'pi-chart-line'],
      [FeedItemType.IdeaSuggestion, 'pi-lightbulb'],
      [FeedItemType.AnalyticsHighlight, 'pi-chart-bar'],
      [FeedItemType.ApprovalRequest, 'pi-check-circle'],
      [FeedItemType.SystemNotification, 'pi-bell'],
    ];

    iconTests.forEach(([type, expectedClass]) => {
      it(`should render ${expectedClass} icon for ${type}`, () => {
        setup({ type });
        const icon = fixture.nativeElement.querySelector('[data-testid="type-icon"]') as HTMLElement;
        expect(icon.classList.contains(expectedClass)).toBeTrue();
      });
    });
  });

  describe('priority badges', () => {
    it('should show priority badge for High priority', () => {
      setup({ priority: FeedItemPriority.High });
      const badge = fixture.nativeElement.querySelector('[data-testid="priority-badge"]');
      expect(badge).toBeTruthy();
      expect(badge.textContent.trim()).toBe('High');
    });

    it('should show priority badge for Urgent priority', () => {
      setup({ priority: FeedItemPriority.Urgent });
      const badge = fixture.nativeElement.querySelector('[data-testid="priority-badge"]');
      expect(badge).toBeTruthy();
      expect(badge.textContent.trim()).toBe('Urgent');
    });

    it('should not show priority badge for Normal priority', () => {
      setup({ priority: FeedItemPriority.Normal });
      const badge = fixture.nativeElement.querySelector('[data-testid="priority-badge"]');
      expect(badge).toBeNull();
    });

    it('should not show priority badge for Low priority', () => {
      setup({ priority: FeedItemPriority.Low });
      const badge = fixture.nativeElement.querySelector('[data-testid="priority-badge"]');
      expect(badge).toBeNull();
    });

    it('should have pulse animation class for Urgent badge', () => {
      setup({ priority: FeedItemPriority.Urgent });
      const badge = fixture.nativeElement.querySelector('[data-testid="priority-badge"]');
      expect(badge.classList.contains('pulse')).toBeTrue();
    });
  });

  describe('action buttons per type', () => {
    it('should show Approve button for AgentDraft type', () => {
      setup({ type: FeedItemType.AgentDraft });
      const btn = fixture.nativeElement.querySelector('[data-testid="primary-action"]');
      expect(btn.textContent.trim()).toBe('Approve');
    });

    it('should show View button for TrendAlert type', () => {
      setup({ type: FeedItemType.TrendAlert });
      const btn = fixture.nativeElement.querySelector('[data-testid="primary-action"]');
      expect(btn.textContent.trim()).toBe('View');
    });

    it('should show Create Content button for IdeaSuggestion type', () => {
      setup({ type: FeedItemType.IdeaSuggestion });
      const btn = fixture.nativeElement.querySelector('[data-testid="primary-action"]');
      expect(btn.textContent.trim()).toBe('Create Content');
    });

    it('should not show primary action for SystemNotification', () => {
      setup({ type: FeedItemType.SystemNotification });
      const btn = fixture.nativeElement.querySelector('[data-testid="primary-action"]');
      expect(btn).toBeNull();
    });

    it('should show Dismiss button for all types', () => {
      const types = Object.values(FeedItemType);
      for (const type of types) {
        setup({ type });
        const btn = fixture.nativeElement.querySelector('[data-testid="action-dismiss"]');
        expect(btn).toBeTruthy(`Dismiss button missing for ${type}`);
      }
    });

    it('should render Edit and Schedule secondary buttons for AgentDraft', () => {
      setup({ type: FeedItemType.AgentDraft });
      const edit = fixture.nativeElement.querySelector('[data-testid="action-edit"]');
      const schedule = fixture.nativeElement.querySelector('[data-testid="action-schedule"]');
      expect(edit).toBeTruthy();
      expect(schedule).toBeTruthy();
    });

    it('should render Edit secondary button for ApprovalRequest', () => {
      setup({ type: FeedItemType.ApprovalRequest });
      const edit = fixture.nativeElement.querySelector('[data-testid="action-edit"]');
      expect(edit).toBeTruthy();
    });

    it('should not render secondary buttons for TrendAlert', () => {
      setup({ type: FeedItemType.TrendAlert });
      const edit = fixture.nativeElement.querySelector('[data-testid="action-edit"]');
      expect(edit).toBeNull();
    });

    it('should emit action event when secondary button clicked', () => {
      setup({ id: 'item-1', type: FeedItemType.AgentDraft });
      let emitted: { id: string; action: string } | undefined;
      fixture.componentInstance.card()!.action.subscribe(e => emitted = e);

      const edit = fixture.nativeElement.querySelector('[data-testid="action-edit"]') as HTMLButtonElement;
      edit.click();

      expect(emitted).toEqual({ id: 'item-1', action: 'edit' });
    });
  });

  describe('read state', () => {
    it('should have is-read class when item is read', () => {
      const card = setup({ isRead: true });
      expect(card.classList.contains('is-read')).toBeTrue();
    });

    it('should not have is-read class when item is unread', () => {
      const card = setup({ isRead: false });
      expect(card.classList.contains('is-read')).toBeFalse();
    });
  });

  describe('selection', () => {
    it('should check checkbox when item id is in selectedIds', () => {
      setup({ id: 'item-1' }, ['item-1']);
      const checkbox = fixture.nativeElement.querySelector('[data-testid="card-checkbox"]') as HTMLInputElement;
      expect(checkbox.checked).toBeTrue();
    });

    it('should uncheck checkbox when item id is not in selectedIds', () => {
      setup({ id: 'item-1' }, []);
      const checkbox = fixture.nativeElement.querySelector('[data-testid="card-checkbox"]') as HTMLInputElement;
      expect(checkbox.checked).toBeFalse();
    });
  });

  describe('event emission', () => {
    it('should emit action event when primary action clicked', () => {
      setup({ id: 'item-1', type: FeedItemType.AgentDraft });
      let emitted: { id: string; action: string } | undefined;
      fixture.componentInstance.card()!.action.subscribe(e => emitted = e);

      const btn = fixture.nativeElement.querySelector('[data-testid="primary-action"]') as HTMLButtonElement;
      btn.click();

      expect(emitted).toEqual({ id: 'item-1', action: 'approve' });
    });

    it('should emit action event when dismiss clicked', () => {
      setup({ id: 'item-1' });
      let emitted: { id: string; action: string } | undefined;
      fixture.componentInstance.card()!.action.subscribe(e => emitted = e);

      const btn = fixture.nativeElement.querySelector('[data-testid="action-dismiss"]') as HTMLButtonElement;
      btn.click();

      expect(emitted).toEqual({ id: 'item-1', action: 'dismiss' });
    });

    it('should emit select event when checkbox toggled', () => {
      setup({ id: 'item-1' });
      let emitted: string | undefined;
      fixture.componentInstance.card()!.select.subscribe(e => emitted = e);

      const checkbox = fixture.nativeElement.querySelector('[data-testid="card-checkbox"]') as HTMLInputElement;
      checkbox.dispatchEvent(new Event('change'));

      expect(emitted).toBe('item-1');
    });
  });
});
