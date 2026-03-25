import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DashboardSummary } from '../models/dashboard.model';

type KpiFormat = 'number' | 'abbreviated' | 'percent' | 'currency';

interface KpiCard {
  readonly label: string;
  readonly value: string;
  readonly changeText: string;
  readonly trend: 'up' | 'down' | 'neutral';
}

function formatKpiValue(value: number, format: KpiFormat): string {
  switch (format) {
    case 'abbreviated':
      if (value >= 1_000_000) return (value / 1_000_000).toFixed(1) + 'M';
      if (value >= 1_000) return Math.round(value / 1_000) + 'K';
      return value.toLocaleString('en-US');
    case 'percent':
      return value.toFixed(2) + '%';
    case 'currency':
      return '$' + value.toFixed(2);
    default:
      return value.toLocaleString('en-US');
  }
}

function computeChange(current: number, previous: number): { text: string; trend: 'up' | 'down' | 'neutral' } {
  if (previous === 0) return { text: 'N/A', trend: 'neutral' };
  const pct = Math.round(((current - previous) / previous) * 10000) / 100;
  if (pct > 0) return { text: '+' + pct.toFixed(1) + '%', trend: 'up' };
  if (pct < 0) return { text: pct.toFixed(1) + '%', trend: 'down' };
  return { text: '0%', trend: 'neutral' };
}

const KPI_DEFS: readonly { label: string; current: keyof DashboardSummary; previous: keyof DashboardSummary; format: KpiFormat; invertTrend?: boolean }[] = [
  { label: 'Total Engagement', current: 'totalEngagement', previous: 'previousEngagement', format: 'number' },
  { label: 'Total Impressions', current: 'totalImpressions', previous: 'previousImpressions', format: 'abbreviated' },
  { label: 'Engagement Rate', current: 'engagementRate', previous: 'previousEngagementRate', format: 'percent' },
  { label: 'Content Published', current: 'contentPublished', previous: 'previousContentPublished', format: 'number' },
  { label: 'Cost / Engagement', current: 'costPerEngagement', previous: 'previousCostPerEngagement', format: 'currency', invertTrend: true },
  { label: 'Website Users', current: 'websiteUsers', previous: 'previousWebsiteUsers', format: 'number' },
];

@Component({
  selector: 'app-dashboard-kpi-cards',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="kpi-grid">
      @for (card of kpiCards(); track card.label) {
        <div class="kpi-card">
          <div class="kpi-label">{{ card.label }}</div>
          <div class="kpi-value">{{ card.value }}</div>
          <span class="kpi-trend" [class.up]="card.trend === 'up'" [class.down]="card.trend === 'down'">
            @if (card.trend === 'up') { &#9650; }
            @if (card.trend === 'down') { &#9660; }
            {{ card.changeText }}
          </span>
        </div>
      }
    </div>
  `,
  styles: `
    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 1rem;
    }
    .kpi-card {
      background: var(--p-surface-900, #111118);
      border: 1px solid var(--p-surface-700, #25252f);
      border-radius: 12px;
      padding: 1.1rem 1.25rem;
      transition: border-color 0.2s ease, transform 0.2s ease;
    }
    .kpi-card:hover {
      border-color: var(--p-surface-600, #3a3a48);
      transform: translateY(-1px);
    }
    .kpi-label {
      font-size: 0.72rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--p-text-muted-color, #71717a);
      margin-bottom: 0.5rem;
    }
    .kpi-value {
      font-size: 1.65rem;
      font-weight: 800;
      letter-spacing: -0.03em;
      line-height: 1.1;
      margin-bottom: 0.4rem;
    }
    .kpi-trend {
      font-size: 0.75rem;
      font-weight: 600;
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.15rem 0.5rem;
      border-radius: 6px;
    }
    .kpi-trend.up { color: #22c55e; background: rgba(34, 197, 94, 0.12); }
    .kpi-trend.down { color: #ef4444; background: rgba(239, 68, 68, 0.12); }
  `,
})
export class DashboardKpiCardsComponent {
  readonly summary = input<DashboardSummary | null>(null);

  readonly kpiCards = computed<readonly KpiCard[]>(() => {
    const s = this.summary();
    if (!s) return [];
    return KPI_DEFS.map(def => {
      const current = s[def.current] as number;
      const previous = s[def.previous] as number;
      const change = computeChange(current, previous);
      const trend: 'up' | 'down' | 'neutral' = def.invertTrend && change.trend !== 'neutral'
        ? (change.trend === 'up' ? 'down' : 'up')
        : change.trend;
      return {
        label: def.label,
        value: formatKpiValue(current, def.format),
        changeText: change.text,
        trend,
      };
    });
  });
}
