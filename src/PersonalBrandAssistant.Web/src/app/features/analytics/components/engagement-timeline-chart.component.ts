import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UIChart } from 'primeng/chart';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { DailyEngagement } from '../models/dashboard.model';
import { PLATFORM_COLORS, PLATFORM_LABELS } from '../../../shared/utils/platform-icons';

@Component({
  selector: 'app-engagement-timeline-chart',
  standalone: true,
  imports: [CommonModule, UIChart, Card, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-card header="Engagement Over Time">
      @if (timeline().length > 0) {
        <div style="position: relative; height: 280px;">
          <p-chart type="line" [data]="chartData()" [options]="chartOptions" height="280px" />
        </div>
      } @else {
        <p-skeleton height="280px" borderRadius="8px" />
      }
    </p-card>
  `,
})
export class EngagementTimelineChartComponent {
  readonly timeline = input<readonly DailyEngagement[]>([]);

  readonly chartData = computed(() => {
    const data = this.timeline();
    if (data.length === 0) return { labels: [], datasets: [] };

    const labels = data.map(d =>
      new Date(d.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
    );

    const platformNames = new Set<string>();
    for (const day of data) {
      for (const p of day.platforms) {
        platformNames.add(p.platform);
      }
    }

    const totalDataset = {
      label: 'Total',
      data: data.map(d => d.total),
      borderColor: '#8b5cf6',
      backgroundColor: 'rgba(139, 92, 246, 0.08)',
      borderWidth: 2.5,
      fill: true,
      tension: 0.35,
      pointRadius: 0,
      pointHitRadius: 8,
    };

    const platformDatasets = [...platformNames].map(name => ({
      label: (PLATFORM_LABELS as Record<string, string>)[name] ?? name,
      data: data.map(d => d.platforms.find(p => p.platform === name)?.total ?? 0),
      borderColor: (PLATFORM_COLORS as Record<string, string>)[name] ?? '#999',
      borderWidth: 1.5,
      fill: false,
      tension: 0.35,
      pointRadius: 0,
      pointHitRadius: 8,
    }));

    return { labels, datasets: [totalDataset, ...platformDatasets] };
  });

  readonly chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index' as const, intersect: false },
    plugins: {
      legend: {
        position: 'top' as const,
        labels: { usePointStyle: true, pointStyle: 'circle', boxWidth: 6, padding: 16, font: { size: 11, weight: '600' } },
      },
      tooltip: {
        backgroundColor: '#1a1a24', borderColor: '#3a3a48', borderWidth: 1,
        titleFont: { weight: '700' }, bodyFont: { size: 12 }, padding: 12, cornerRadius: 8,
      },
    },
    scales: {
      x: { grid: { display: false }, ticks: { maxTicksLimit: 8, font: { size: 10 } } },
      y: { grid: { color: 'rgba(255,255,255,0.04)' }, ticks: { font: { size: 10 } } },
    },
  };
}
