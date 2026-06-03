import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ContentStore } from '../../stores/content.store';
import { ContentStatus } from '../../models/content.model';
import { STATUS_META } from '../content-display.utils';

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
 * Status-filter pills. First pill is "All {total}"; the rest mirror `store.counts()`. Clicking a
 * pill toggles `store.activeStatus`; re-clicking the active pill clears it. Zero-count pills dim;
 * the selected pill takes its status color.
 */
@Component({
  selector: 'app-pipeline-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="pipeline-bar" data-testid="pipeline-bar">
      <button
        type="button"
        class="pill all"
        [class.on]="store.activeStatus() === null"
        (click)="store.setActiveStatus(null)"
        data-testid="pill-all">
        All
        <span class="count">{{ total() }}</span>
      </button>

      @for (s of statuses; track s.status) {
        <button
          type="button"
          class="pill"
          [class.on]="store.activeStatus() === s.status"
          [class.empty]="store.counts()[s.status] === 0"
          [style.--pill-color]="s.color"
          (click)="store.setActiveStatus(s.status)"
          [attr.data-status]="s.status"
          [attr.data-testid]="'pill-' + s.status">
          <span class="dot" [style.background]="s.color"></span>
          {{ s.label }}
          <span class="count">{{ store.counts()[s.status] }}</span>
        </button>
      }
    </div>
  `,
  styles: [`
    .pipeline-bar {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      padding: 18px 28px 14px;
    }
    .pill {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      padding: 7px 13px;
      border-radius: var(--r-pill);
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      color: var(--text-secondary);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.14s, color 0.14s, border-color 0.14s, opacity 0.14s;
    }
    .pill:hover {
      background: var(--surface-hover);
      color: var(--text-primary);
    }
    .pill.empty {
      opacity: 0.5;
    }
    .pill.on {
      background: var(--surface-elevated);
      color: var(--text-primary);
      border-color: var(--pill-color, var(--surface-disabled));
    }
    .pill.all.on {
      border-color: var(--brand-primary);
      color: var(--text-primary);
    }
    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .count {
      font-family: var(--font-mono);
      font-size: 11px;
      color: var(--text-muted);
    }
    .pill.on .count {
      color: var(--text-secondary);
    }
  `],
})
export class PipelineBarComponent {
  readonly store = inject(ContentStore);

  readonly statuses = STATUS_ORDER.map((status) => ({
    status,
    label: STATUS_META[status].label,
    color: STATUS_META[status].color,
  }));

  readonly total = computed(() => this.store.allContents().length);
}
