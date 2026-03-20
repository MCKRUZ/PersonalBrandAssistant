import { Component, input, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UIChart } from 'primeng/chart';
import { Card } from 'primeng/card';
import { TopPerformingContent } from '../../../shared/models';
import { PLATFORM_COLORS } from '../../../shared/utils/platform-icons';

@Component({
  selector: 'app-engagement-chart',
  standalone: true,
  imports: [CommonModule, UIChart, Card],
  template: `
    <div class="grid">
      <div class="col-12 md:col-8">
        <p-card header="Top 10 by Engagement">
          <p-chart type="bar" [data]="barData()" [options]="barOptions" height="300px" />
        </p-card>
      </div>
      <div class="col-12 md:col-4">
        <p-card header="Engagement by Platform">
          <p-chart type="doughnut" [data]="doughnutData()" [options]="doughnutOptions" height="300px" />
        </p-card>
      </div>
    </div>
  `,
})
export class EngagementChartComponent {
  items = input<readonly TopPerformingContent[]>([]);

  readonly barOptions = {
    indexAxis: 'y' as const,
    plugins: { legend: { display: false } },
    scales: { x: { beginAtZero: true } },
  };

  readonly doughnutOptions = {
    plugins: { legend: { position: 'bottom' as const } },
  };

  barData = computed(() => {
    const top10 = this.items().slice(0, 10);
    return {
      labels: top10.map(i => (i.title ?? 'Untitled').substring(0, 30)),
      datasets: [{
        data: top10.map(i => i.totalEngagement),
        backgroundColor: '#3b82f6',
      }],
    };
  });

  doughnutData = computed(() => {
    const platformTotals = new Map<string, number>();
    for (const item of this.items()) {
      for (const p of item.platforms) {
        platformTotals.set(p, (platformTotals.get(p) ?? 0) + item.totalEngagement);
      }
    }
    const labels = [...platformTotals.keys()];
    return {
      labels,
      datasets: [{
        data: labels.map(l => platformTotals.get(l) ?? 0),
        backgroundColor: labels.map(l => (PLATFORM_COLORS as Record<string, string>)[l] ?? '#999'),
      }],
    };
  });
}
