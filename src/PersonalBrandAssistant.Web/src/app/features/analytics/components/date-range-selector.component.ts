import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePicker } from 'primeng/datepicker';
import { DashboardPeriod } from '../models/dashboard.model';

const PRESETS = ['1d', '7d', '14d', '30d', '90d'] as const;

@Component({
  selector: 'app-date-range-selector',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, DatePicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex align-items-center gap-2 flex-wrap">
      @for (preset of presets; track preset) {
        <p-button
          [label]="preset.toUpperCase()"
          [outlined]="!isActive(preset)"
          [severity]="isActive(preset) ? 'primary' : 'secondary'"
          size="small"
          (onClick)="selectPreset(preset)"
        />
      }
      <p-datepicker
        [(ngModel)]="customRange"
        selectionMode="range"
        [showIcon]="true"
        placeholder="Custom range"
        styleClass="w-14rem"
        (onSelect)="onCustomSelect()"
      />
    </div>
  `,
})
export class DateRangeSelectorComponent {
  readonly activePeriod = input<DashboardPeriod>('30d');
  readonly periodChanged = output<DashboardPeriod>();

  readonly presets = PRESETS;
  customRange: Date[] = [];

  isActive(preset: string): boolean {
    return this.activePeriod() === preset;
  }

  selectPreset(preset: string) {
    this.customRange = [];
    this.periodChanged.emit(preset as DashboardPeriod);
  }

  onCustomSelect() {
    if (this.customRange.length >= 2 && this.customRange[1]) {
      this.periodChanged.emit({
        from: this.customRange[0].toISOString(),
        to: this.customRange[1].toISOString(),
      });
    }
  }
}
