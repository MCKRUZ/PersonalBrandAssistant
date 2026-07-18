import { AnalyticsPeriod } from '../models/analytics.model';

// Single source of truth for the period toggle used by every analytics view.
export const PERIOD_OPTIONS: ReadonlyArray<{ label: string; value: AnalyticsPeriod }> = [
  { label: '7d', value: '7d' },
  { label: '30d', value: '30d' },
  { label: '90d', value: '90d' },
];

const PERIOD_LABELS: Record<AnalyticsPeriod, string> = { '7d': '7 days', '30d': '30 days', '90d': '90 days' };

export function periodDisplayLabel(period: AnalyticsPeriod): string {
  return PERIOD_LABELS[period];
}

// Shared accent ramp (obsidian theme) for trend lines / sparklines across analytics charts.
export const CHART_PALETTE = ['#c87156', '#8a7df0', '#60a5fa', '#4ade80', '#fbbf24', '#f0935f'];
