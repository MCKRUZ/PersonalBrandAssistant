import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SelectButtonModule } from 'primeng/selectbutton';
import { AnalyticsPeriod } from '../models/analytics.model';
import { PERIOD_OPTIONS } from './analytics-shared';

// Shared 7d/30d/90d toggle. Emits on change; the host owns the period signal + reload.
@Component({
  selector: 'app-period-selector',
  standalone: true,
  imports: [FormsModule, SelectButtonModule],
  template: `
    <p-selectButton
      class="period-select"
      [options]="periodOptions"
      [ngModel]="period()"
      (ngModelChange)="periodChange.emit($event)"
      optionLabel="label" optionValue="value" />
  `,
})
export class PeriodSelectorComponent {
  readonly period = input.required<AnalyticsPeriod>();
  readonly periodChange = output<AnalyticsPeriod>();
  readonly periodOptions = [...PERIOD_OPTIONS];
}
