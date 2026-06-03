import { ChangeDetectionStrategy, Component, Input, computed, signal } from '@angular/core';
import { ContentStatus } from '../models/content.model';
import { STATUS_META } from '../content-list/content-display.utils';

@Component({
  selector: 'app-status-tag',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="status-tag" [style.color]="meta().color">
      <span class="dot" [style.background]="meta().color"></span>
      {{ meta().label }}
    </span>
  `,
  styles: [`
    .status-tag {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 500;
    }
    .dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      flex-shrink: 0;
    }
  `],
})
export class StatusTagComponent {
  private readonly _status = signal<ContentStatus>(ContentStatus.Draft);
  @Input({ required: true }) set status(value: ContentStatus) {
    this._status.set(value);
  }
  readonly meta = computed(() => STATUS_META[this._status()]);
}
