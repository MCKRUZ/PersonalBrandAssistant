import { Component, input } from '@angular/core';
import { Card } from 'primeng/card';

@Component({
  selector: 'app-kpi-card',
  standalone: true,
  imports: [Card],
  template: `
    <p-card styleClass="h-full">
      <div class="flex align-items-center gap-3">
        <div class="flex align-items-center justify-content-center border-round" [style]="{ 'background-color': 'var(--p-primary-100)', width: '3rem', height: '3rem' }">
          <i [class]="icon()" style="font-size: 1.5rem; color: var(--p-primary-500)"></i>
        </div>
        <div>
          <div class="text-color-secondary text-sm">{{ label() }}</div>
          <div class="text-2xl font-bold">{{ value() }}</div>
        </div>
      </div>
    </p-card>
  `,
})
export class KpiCardComponent {
  label = input.required<string>();
  value = input.required<string | number>();
  icon = input.required<string>();
}
