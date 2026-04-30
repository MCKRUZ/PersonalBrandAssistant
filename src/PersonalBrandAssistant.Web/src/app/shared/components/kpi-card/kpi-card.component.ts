import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-kpi-card',
  standalone: true,
  template: `
    <div class="kpi-card" [class.flagged]="flagged()">
      <span class="kpi-value font-display">{{ formattedValue() }}</span>
      @if (trend(); as t) {
        <span class="kpi-trend" [class]="'trend-' + t">
          {{ t === 'up' ? '▲' : t === 'down' ? '▼' : '—' }}
        </span>
      }
      <span class="kpi-label">{{ label() }}</span>
      @if (sub(); as s) {
        <span class="kpi-sub">{{ s }}</span>
      }
    </div>
  `,
  styles: `
    .kpi-card {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 16px;
      background: var(--p-surface-50);
      border-radius: 8px;
      border: 1px solid var(--p-surface-300);
    }
    .kpi-card.flagged {
      border-color: #c87156;
    }
    .kpi-value {
      font-size: 28px;
      color: var(--p-surface-900);
    }
    .kpi-trend {
      font-size: 12px;
      margin-left: 4px;
    }
    .trend-up { color: #4ade80; }
    .trend-down { color: #f87171; }
    .trend-flat { color: var(--p-surface-500); }
    .kpi-label {
      font-size: 13px;
      color: var(--p-surface-600);
    }
    .kpi-sub {
      font-size: 11px;
      color: var(--p-surface-500);
    }
  `,
})
export class KpiCardComponent {
  value = input.required<number | string | undefined>();
  label = input.required<string>();
  trend = input<'up' | 'down' | 'flat'>();
  sub = input<string>();
  flagged = input(false);

  formattedValue = computed(() => {
    const v = this.value();
    if (v == null) return '--';
    if (typeof v === 'string') return v;
    if (v >= 1_000_000) return (v / 1_000_000).toFixed(1).replace(/\.0$/, '') + 'M';
    if (v >= 1_000) return (v / 1_000).toFixed(1).replace(/\.0$/, '') + 'K';
    return v.toString();
  });
}
