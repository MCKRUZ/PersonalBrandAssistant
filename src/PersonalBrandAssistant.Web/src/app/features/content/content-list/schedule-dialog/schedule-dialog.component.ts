import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';

/**
 * Minimal date/time picker that produces a `scheduledAt` ISO string. Reused by the board's
 * "drop into Scheduled" flow and the drawer's "Move to Scheduled" action. Emits the ISO date
 * on confirm; the caller then calls ContentService.schedule(id, { scheduledAt }).
 */
@Component({
  selector: 'app-schedule-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ButtonModule, DatePickerModule],
  template: `
    @if (visible()) {
      <div class="scrim" (click)="cancelled.emit()" data-testid="schedule-scrim"></div>
      <div class="dialog" role="dialog" aria-modal="true" data-testid="schedule-dialog">
        <h2 class="title">Schedule publish</h2>
        <p class="sub">Pick when this should go out.</p>
        <p-datepicker
          [(ngModel)]="value"
          [showTime]="true"
          hourFormat="12"
          [showClear]="true"
          dateFormat="M dd, yy"
          placeholder="Select date & time"
          [style]="{ width: '100%' }"
          appendTo="body"
          data-testid="schedule-picker" />
        <div class="actions">
          <p-button
            label="Cancel"
            severity="secondary"
            [text]="true"
            (onClick)="cancelled.emit()"
            data-testid="schedule-cancel" />
          <p-button
            label="Schedule"
            [disabled]="!value()"
            (onClick)="onConfirm()"
            data-testid="schedule-confirm" />
        </div>
      </div>
    }
  `,
  styles: [`
    .scrim {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.55);
      z-index: 60;
    }
    .dialog {
      position: fixed;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      width: 360px;
      max-width: 92vw;
      z-index: 61;
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: var(--r-modal);
      padding: 22px;
      box-shadow: 0 24px 60px -20px rgba(0, 0, 0, 0.7);
    }
    .title {
      font-family: var(--font-display);
      font-size: 22px;
      font-weight: 400;
      color: var(--text-primary);
      margin: 0 0 4px;
    }
    .sub {
      font-size: 13px;
      color: var(--text-secondary);
      margin: 0 0 16px;
    }
    .actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      margin-top: 18px;
    }
  `],
})
export class ScheduleDialogComponent {
  readonly visible = input(false);
  readonly confirmed = output<string>();
  readonly cancelled = output<void>();

  readonly value = signal<Date | null>(null);

  onConfirm(): void {
    const date = this.value();
    if (!date) return;
    this.confirmed.emit(date.toISOString());
    this.value.set(null);
  }
}
