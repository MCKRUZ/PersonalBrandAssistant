import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Platform } from '../../models/content.model';

export type ResultState = 'publishing' | 'published' | 'failed' | 'ready' | 'scheduled';

export interface ResultRow {
  platform: Platform;
  code: string;
  label: string;
  mode: 'auto' | 'manual' | 'scheduled';
  state: ResultState;
  /** Published URL (auto) once available. */
  url?: string;
  /** Platform-formatted text to copy (manual). */
  copyText?: string;
  /** External compose URL (manual "Open"). */
  openUrl?: string;
  /** ISO datetime (scheduled). */
  scheduledAt?: string;
}

/**
 * Post-confirm result view. Presentational: the parent (modal) supplies rows and updates their
 * `state` as publish status resolves. Auto rows show Publishing -> Published; manual rows offer
 * Copy + Open; scheduled rows show the scheduled time.
 */
@Component({
  selector: 'app-publish-result',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="result-list">
      @for (row of rows(); track row.platform) {
        <div class="result-row" [attr.data-platform]="row.platform">
          <span class="code">{{ row.code }}</span>
          <span class="label">{{ row.label }}</span>

          <span class="state">
            @switch (row.state) {
              @case ('publishing') { <span class="spinner"></span> Publishing… }
              @case ('published') {
                <span class="ok">✓ Published</span>
                @if (row.url) { — <a [href]="row.url" target="_blank" rel="noopener">View ↗</a> }
              }
              @case ('failed') {
                <span class="fail">Failed</span>
                <button type="button" class="link" (click)="retry.emit(row.platform)">Retry</button>
              }
              @case ('ready') {
                Ready to post
                <button type="button" class="link" (click)="copy(row)">⧉ Copy text</button>
                @if (row.openUrl) {
                  <a [href]="row.openUrl" target="_blank" rel="noopener">Open {{ row.label }} ↗</a>
                }
              }
              @case ('scheduled') { ◴ Scheduled for {{ row.scheduledAt }} }
            }
          </span>
        </div>
      }
    </div>
    <p class="note">Manual destinations have no publish API — copy the text and post it yourself.</p>
  `,
  styles: [`
    .result-list { display: flex; flex-direction: column; gap: 8px; }
    .result-row {
      display: flex; align-items: center; gap: 10px;
      padding: 12px; border: 1px solid var(--surface-border); border-radius: var(--r-inner);
      background: var(--surface-inset); color: var(--text-primary); font-size: 13.5px;
    }
    .code {
      display: inline-grid; place-items: center; width: 26px; height: 26px;
      border: 1px solid var(--surface-border); border-radius: var(--r-control);
      font-family: var(--font-mono); font-size: 11px;
    }
    .label { font-weight: 500; }
    .state { margin-left: auto; display: flex; align-items: center; gap: 8px; }
    .ok { color: var(--status-published); }
    .fail { color: var(--voice-low); }
    .link {
      background: none; border: none; color: var(--brand-primary); cursor: pointer;
      font: inherit; padding: 0;
    }
    a { color: var(--brand-primary); }
    .spinner {
      width: 12px; height: 12px; border-radius: 50%;
      border: 2px solid var(--surface-border); border-top-color: var(--brand-primary);
      animation: spin 0.7s linear infinite; display: inline-block;
    }
    .note { margin-top: 12px; font-size: 12px; color: var(--text-muted); }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class PublishResultComponent {
  readonly rows = input.required<ResultRow[]>();
  readonly retry = output<Platform>();

  copy(row: ResultRow): void {
    if (row.copyText && typeof navigator !== 'undefined' && navigator.clipboard) {
      void navigator.clipboard.writeText(row.copyText);
    }
  }
}
