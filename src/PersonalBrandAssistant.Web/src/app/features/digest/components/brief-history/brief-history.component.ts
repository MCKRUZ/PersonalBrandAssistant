import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DigestSummary } from '../../models/digest.model';

@Component({
  selector: 'app-brief-history',
  standalone: true,
  imports: [DatePipe],
  template: `
    <nav class="history">
      <h2 class="history-title">Daily Briefs</h2>
      @for (d of digests(); track d.id) {
        <button type="button" class="history-entry" [class.active]="d.id === selectedId()"
                data-testid="history-entry" (click)="select.emit(d.id)">
          <span class="entry-date">{{ d.date | date: 'MMM d' }}</span>
          <span class="entry-title">{{ d.title }}</span>
          <span class="entry-count">{{ d.itemCount }} items</span>
        </button>
      }
    </nav>
  `,
  styles: [`
    .history { display: flex; flex-direction: column; padding: 16px 8px; }
    .history-title { font-size: 13px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); padding: 0 8px 8px; margin: 0; }
    .history-entry { text-align: left; background: none; border: none; cursor: pointer;
      display: flex; flex-direction: column; gap: 2px; padding: 10px 12px; border-radius: var(--r-control);
      color: var(--text-secondary); border-left: 2px solid transparent; }
    .history-entry:hover { background: var(--surface-hover); }
    .history-entry.active { background: var(--accent-soft); border-left-color: var(--brand-primary); }
    .entry-date { font-size: 11px; color: var(--text-muted); }
    .entry-title { font-size: 14px; font-weight: 600; color: var(--text-primary); }
    .entry-count { font-size: 11px; color: var(--text-secondary); }
  `],
})
export class BriefHistoryComponent {
  readonly digests = input.required<DigestSummary[]>();
  readonly selectedId = input.required<string | null>();
  readonly select = output<string>();
}
