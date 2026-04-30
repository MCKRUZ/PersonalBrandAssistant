import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-axis-bar',
  standalone: true,
  template: `
    <div class="axis-bar">
      <div class="axis-header">
        <span class="axis-label">{{ label() }}</span>
        <span class="axis-value">{{ clampedValue() }}</span>
      </div>
      <div class="axis-track"
           role="progressbar"
           [attr.aria-valuenow]="clampedValue()"
           aria-valuemin="0"
           aria-valuemax="100"
           [attr.aria-label]="label()">
        <div
          class="axis-fill"
          [style.width.%]="clampedValue()"
          [style.background]="color()"
        ></div>
      </div>
    </div>
  `,
  styles: `
    .axis-bar { display: flex; flex-direction: column; gap: 4px; }
    .axis-header {
      display: flex;
      justify-content: space-between;
      font-size: 12px;
    }
    .axis-label { color: var(--p-surface-700); }
    .axis-value { color: var(--p-surface-600); font-family: 'JetBrains Mono', monospace; }
    .axis-track {
      height: 6px;
      background: var(--p-surface-300);
      border-radius: 3px;
      overflow: hidden;
    }
    .axis-fill {
      height: 100%;
      border-radius: 3px;
      transition: width 400ms ease;
    }
  `,
})
export class AxisBarComponent {
  label = input.required<string>();
  value = input.required<number>();

  clampedValue = computed(() => Math.max(0, Math.min(100, Math.round(this.value()))));

  color = computed(() => {
    const v = this.clampedValue();
    if (v >= 80) return '#4ade80';
    if (v >= 60) return '#fbbf24';
    return '#f87171';
  });
}
