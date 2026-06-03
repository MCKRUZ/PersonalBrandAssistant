import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ContentStatus } from '../../models/content.model';
import { STATUS_META } from '../../content-list/content-display.utils';

/** Linear pipeline order for the dots. Archived is off this path (terminal). */
const LINEAR_STAGES: ContentStatus[] = [
  ContentStatus.Idea,
  ContentStatus.Draft,
  ContentStatus.Review,
  ContentStatus.Approved,
  ContentStatus.Scheduled,
  ContentStatus.Published,
];

@Component({
  selector: 'app-stage-tracker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isArchived()) {
      <div class="stage-tracker archived" data-testid="stage-tracker">
        @for (stage of stages; track stage; let i = $index) {
          <span class="dot muted" data-testid="stage-dot"></span>
          @if (i < stages.length - 1) { <span class="connector muted"></span> }
        }
        <span class="archived-label" data-testid="archived-label">Archived</span>
      </div>
    } @else {
      <div class="stage-tracker" data-testid="stage-tracker">
        @for (stage of stages; track stage; let i = $index) {
          <span
            class="dot"
            data-testid="stage-dot"
            [class.completed]="i < activeIndex()"
            [class.active]="i === activeIndex()"
            [class.empty]="i > activeIndex()"
            [style.background]="i === activeIndex() ? activeColor() : null"
            [style.border-color]="i === activeIndex() ? activeColor() : null"></span>
          @if (i < stages.length - 1) {
            <span class="connector" [class.filled]="i < activeIndex()"></span>
          }
        }
      </div>
    }
  `,
  styles: [`
    .stage-tracker { display: flex; align-items: center; gap: 0; }
    .dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      flex-shrink: 0;
      transition: width 160ms, height 160ms, background 160ms;
    }
    .dot.completed { background: var(--text-muted); }
    .dot.empty { background: transparent; border: 1px solid var(--surface-border); }
    .dot.active { width: 12px; height: 12px; }
    .dot.muted { background: var(--surface-border); }
    .connector {
      width: 18px;
      height: 1px;
      background: var(--surface-border);
      flex-shrink: 0;
    }
    .connector.filled { background: var(--text-muted); }
    .connector.muted { background: var(--surface-border); }
    .archived-label {
      margin-left: 10px;
      font-size: 11px;
      font-family: var(--font-mono);
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
  `],
})
export class StageTrackerComponent {
  readonly status = input.required<ContentStatus | null>();

  readonly stages = LINEAR_STAGES;

  readonly isArchived = computed(() => this.status() === ContentStatus.Archived);

  readonly activeIndex = computed(() => {
    const s = this.status();
    if (s === null) return -1;
    return LINEAR_STAGES.indexOf(s);
  });

  readonly activeColor = computed(() => {
    const s = this.status();
    return s ? STATUS_META[s].color : 'var(--text-muted)';
  });
}
