import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UIChart } from 'primeng/chart';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { DailyEngagement } from '../models/dashboard.model';
import { PLATFORM_LABELS } from '../../../shared/utils/platform-icons';

@Component({
  selector: 'app-platform-breakdown-chart',
  standalone: true,
  imports: [CommonModule, UIChart, Card, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-card header="Platform Breakdown">
      @if (timeline().length > 0) {
        <div style="position: relative; height: 280px;">
          <p-chart type="bar" [data]="chartData()" [options]="chartOptions" height="280px" />
        </div>
      } @else {
        <p-skeleton height="280px" borderRadius="8px" />
      }
    </p-card>
  `,
})
export class PlatformBreakdownChartComponent {
  readonly timeline = input<readonly DailyEngagement[]>([]);

  readonly chartData = computed(() => {
    const data = this.timeline();
    if (data.length === 0) return { labels: [], datasets: [] };

    const agg = new Map<string, { likes: number; comments: number; shares: number }>();
    for (const day of data) {
      for (const p of day.platforms) {
        const entry = agg.get(p.platform) ?? { likes: 0, comments: 0, shares: 0 };
        entry.likes += p.likes;
        entry.comments += p.comments;
        entry.shares += p.shares;
        agg.set(p.platform, entry);
      }
    }

    const platforms = [...agg.keys()].sort((a, b) => {
      const totalA = agg.get(a)!;
      const totalB = agg.get(b)!;
      return (totalB.likes + totalB.comments + totalB.shares) - (totalA.likes + totalA.comments + totalA.shares);
    });

    const labels = platforms.map(p => (PLATFORM_LABELS as Record<string, string>)[p] ?? p);

    return {
      labels,
      datasets: [
        { label: 'Likes', data: platforms.map(p => agg.get(p)!.likes), backgroundColor: 'rgba(139, 92, 246, 0.7)', borderRadius: 3 },
        { label: 'Comments', data: platforms.map(p => agg.get(p)!.comments), backgroundColor: 'rgba(96, 165, 250, 0.7)', borderRadius: 3 },
        { label: 'Shares', data: platforms.map(p => agg.get(p)!.shares), backgroundColor: 'rgba(52, 211, 153, 0.55)', borderRadius: 3 },
      ],
    };
  });

  readonly chartOptions = {
    indexAxis: 'y' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'top' as const,
        labels: { usePointStyle: true, pointStyle: 'circle', boxWidth: 6, padding: 16, font: { size: 11, weight: '600' } },
      },
      tooltip: {
        backgroundColor: '#1a1a24', borderColor: '#3a3a48', borderWidth: 1, padding: 10, cornerRadius: 8,
      },
    },
    scales: {
      x: { stacked: true, grid: { color: 'rgba(255,255,255,0.04)' }, ticks: { font: { size: 10 } } },
      y: { stacked: true, grid: { display: false }, ticks: { font: { size: 11, weight: '600' } } },
    },
  };
}
