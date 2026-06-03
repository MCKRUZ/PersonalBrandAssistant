import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import {
  CdkDrag,
  CdkDragDrop,
  CdkDropList,
  CdkDropListGroup,
} from '@angular/cdk/drag-drop';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import { Content, ContentStatus } from '../../models/content.model';
import { LEGAL_TRANSITIONS, STATUS_META } from '../content-display.utils';
import { ContentCardComponent } from '../content-card/content-card.component';
import { ScheduleDialogComponent } from '../schedule-dialog/schedule-dialog.component';

const STATUS_ORDER: ContentStatus[] = [
  ContentStatus.Idea,
  ContentStatus.Draft,
  ContentStatus.Review,
  ContentStatus.Approved,
  ContentStatus.Scheduled,
  ContentStatus.Published,
  ContentStatus.Archived,
];

/**
 * CDK kanban. One drop-list per status, columns derived from `store.byStatus()`. The enter-predicate
 * rejects illegal target columns so a card never snaps where it can't go. Drops dispatch through the
 * store (never mutate the event arrays — they derive from a computed). Dropping into `Scheduled`
 * opens the schedule dialog and calls ContentService.schedule on confirm.
 */
@Component({
  selector: 'app-content-board',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CdkDropListGroup,
    CdkDropList,
    CdkDrag,
    ContentCardComponent,
    ScheduleDialogComponent,
  ],
  template: `
    <div class="board" cdkDropListGroup data-testid="content-board">
      @for (col of columns(); track col.status) {
        <div class="column" [attr.data-status]="col.status">
          <div class="col-head">
            <span class="dot" [style.background]="col.color"></span>
            <span class="name">{{ col.label }}</span>
            <span class="count">{{ col.cards.length }}</span>
          </div>
          <div
            class="col-body"
            cdkDropList
            [id]="col.status"
            [cdkDropListData]="col.cards"
            [cdkDropListEnterPredicate]="canDropInto(col.status)"
            (cdkDropListDropped)="onDrop($event)"
            [class.col-over]="overColumn() === col.status"
            (cdkDropListEntered)="onEntered(col.status, $event)"
            (cdkDropListExited)="onExited()"
            [attr.data-testid]="'col-' + col.status">
            @for (card of col.cards; track card.id) {
              <app-content-card
                cdkDrag
                [cdkDragData]="card"
                [content]="card"
                variant="board"
                (click)="openCard.emit(card.id)" />
            }
            @if (col.cards.length === 0) {
              <div class="drop-here" data-testid="drop-here">Drop here</div>
            }
          </div>
        </div>
      }
    </div>

    <app-schedule-dialog
      [visible]="scheduleVisible()"
      (confirmed)="onScheduleConfirm($event)"
      (cancelled)="onScheduleCancel()" />
  `,
  styles: [`
    .board {
      display: flex;
      gap: 16px;
      align-items: flex-start;
      overflow-x: auto;
      padding: 4px 28px 28px;
      height: 100%;
      min-height: 0;
    }
    .column {
      flex: 0 0 286px;
      width: 286px;
      max-height: 100%;
      display: flex;
      flex-direction: column;
      background: var(--surface-inset);
      border: 1px solid var(--surface-border);
      border-radius: var(--r);
      transition: border-color 0.14s, box-shadow 0.14s;
    }
    .col-head {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 14px 14px 11px;
    }
    .col-head .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .col-head .name {
      font-size: 13px;
      font-weight: 600;
      color: var(--text-primary);
    }
    .col-head .count {
      margin-left: auto;
      font-family: var(--font-mono);
      font-size: 11px;
      color: var(--text-muted);
    }
    .col-body {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding: 0 11px 13px;
      overflow-y: auto;
      min-height: 60px;
      border-radius: var(--r);
      transition: background 0.14s;
    }
    .col-over {
      background: var(--accent-soft);
    }
    .column:has(.col-over) {
      border-color: var(--brand-primary);
    }
    .drop-here {
      display: grid;
      place-items: center;
      min-height: 72px;
      border: 1px dashed var(--surface-border);
      border-radius: var(--r-inner);
      color: var(--text-muted);
      font-size: 12px;
    }
    .cdk-drag-preview app-content-card {
      box-shadow: 0 10px 30px -8px rgba(0, 0, 0, 0.7);
    }
    .cdk-drag-placeholder {
      opacity: 0.4;
    }
    .cdk-drag-animating {
      transition: transform 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
    }
  `],
})
export class ContentBoardComponent {
  readonly store = inject(ContentStore);
  private readonly contentService = inject(ContentService);

  /** Card clicked -> orchestrator opens the detail drawer. */
  readonly openCard = output<string>();

  readonly overColumn = signal<ContentStatus | null>(null);
  readonly scheduleVisible = signal(false);
  private pendingScheduleId: string | null = null;

  readonly columns = computed(() => {
    const grouped = this.store.byStatus();
    return STATUS_ORDER.map((status) => ({
      status,
      label: STATUS_META[status].label,
      color: STATUS_META[status].color,
      cards: grouped[status],
    }));
  });

  /** Enter-predicate factory: a drag may enter `target` only if the move is a legal transition. */
  canDropInto(target: ContentStatus): (drag: CdkDrag<Content>) => boolean {
    return (drag: CdkDrag<Content>) => {
      const card = drag.data;
      if (!card) return false;
      if (card.status === target) return true; // staying put is always allowed
      return LEGAL_TRANSITIONS[card.status]?.includes(target) ?? false;
    };
  }

  onEntered(status: ContentStatus, _event: unknown): void {
    this.overColumn.set(status);
  }

  onExited(): void {
    this.overColumn.set(null);
  }

  onDrop(event: CdkDragDrop<Content[]>): void {
    this.overColumn.set(null);

    // Same column => no reordering semantics; status conveys position. No-op.
    if (event.previousContainer === event.container) return;

    const card = event.item.data as Content;
    const target = event.container.id as ContentStatus;
    if (!card) return;

    if (target === ContentStatus.Scheduled) {
      this.pendingScheduleId = card.id;
      this.scheduleVisible.set(true);
      return;
    }

    // Never mutate event arrays (they derive from byStatus computed) — dispatch through the store.
    this.store.transition(card.id, target);
  }

  onScheduleConfirm(scheduledAt: string): void {
    const id = this.pendingScheduleId;
    this.scheduleVisible.set(false);
    this.pendingScheduleId = null;
    if (!id) return;
    this.contentService.schedule(id, { scheduledAt }).subscribe({
      next: () => this.store.loadAll(),
    });
  }

  onScheduleCancel(): void {
    this.scheduleVisible.set(false);
    this.pendingScheduleId = null;
  }
}
