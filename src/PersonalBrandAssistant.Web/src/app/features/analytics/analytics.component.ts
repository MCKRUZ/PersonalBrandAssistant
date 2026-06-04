import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { ChartModule } from 'primeng/chart';
import { SelectButtonModule } from 'primeng/selectbutton';
import { FormsModule } from '@angular/forms';
import { AnalyticsService } from './services/analytics.service';
import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from './models/analytics.model';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, TableModule, ChartModule, SelectButtonModule],
  template: `
    <div class="p-4">
      <div class="flex align-items-center justify-content-between mb-3">
        <h2 class="m-0">Website Analytics</h2>
        <p-selectButton
          [options]="periodOptions"
          [ngModel]="period()"
          (ngModelChange)="changePeriod($event)"
          optionLabel="label" optionValue="value" />
      </div>

      @if (health(); as h) {
        @if (!h.ga4 || !h.searchConsole) {
          <div class="p-3 mb-3 border-round" style="background:#fff3cd;color:#664d03;">
            Some analytics sources are unavailable
            @if (!h.ga4) { <span> · Google Analytics</span> }
            @if (!h.searchConsole) { <span> · Search Console</span> }
          </div>
        }
      }

      @if (loading()) {
        <p>Loading…</p>
      } @else if (data()) {
        @if (data(); as d) {
        <div class="grid mb-4">
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Users</div><div class="text-2xl font-bold">{{ d.overview.activeUsers }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Sessions</div><div class="text-2xl font-bold">{{ d.overview.sessions }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Page Views</div><div class="text-2xl font-bold">{{ d.overview.pageViews }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">New Users</div><div class="text-2xl font-bold">{{ d.overview.newUsers }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Bounce</div><div class="text-2xl font-bold">{{ (d.overview.bounceRate * 100) | number:'1.0-1' }}%</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Avg Sess (s)</div><div class="text-2xl font-bold">{{ d.overview.avgSessionDuration | number:'1.0-0' }}</div></div></div>
        </div>

        <div class="grid">
          <div class="col-12 md:col-5">
            <h3>Traffic Sources</h3>
            <p-chart type="doughnut" [data]="trafficChart()" />
          </div>
          <div class="col-12 md:col-7">
            <h3>Top Pages</h3>
            <p-table [value]="d.topPages" [paginator]="d.topPages.length > 10" [rows]="10">
              <ng-template pTemplate="header"><tr><th>Page</th><th>Views</th><th>Users</th></tr></ng-template>
              <ng-template pTemplate="body" let-row><tr><td>{{ row.pagePath }}</td><td>{{ row.views }}</td><td>{{ row.uniqueUsers }}</td></tr></ng-template>
            </p-table>
          </div>
        </div>

        <h3>Top Search Queries</h3>
        <p-table [value]="d.searchQueries" [paginator]="d.searchQueries.length > 10" [rows]="10">
          <ng-template pTemplate="header"><tr><th>Query</th><th>Clicks</th><th>Impressions</th><th>CTR</th><th>Position</th></tr></ng-template>
          <ng-template pTemplate="body" let-row>
            <tr><td>{{ row.query }}</td><td>{{ row.clicks }}</td><td>{{ row.impressions }}</td><td>{{ (row.ctr * 100) | number:'1.0-1' }}%</td><td>{{ row.position | number:'1.0-1' }}</td></tr>
          </ng-template>
        </p-table>
        }
      } @else {
        <p>No analytics data available.</p>
      }
    </div>
  `,
})
export class AnalyticsComponent implements OnInit {
  readonly periodOptions = [
    { label: '7d', value: '7d' as AnalyticsPeriod },
    { label: '30d', value: '30d' as AnalyticsPeriod },
    { label: '90d', value: '90d' as AnalyticsPeriod },
  ];

  readonly period = signal<AnalyticsPeriod>('30d');
  readonly loading = signal(false);
  readonly data = signal<WebsiteAnalytics | null>(null);
  readonly health = signal<AnalyticsHealth | null>(null);
  readonly trafficChart = signal<unknown>({});

  constructor(private readonly api: AnalyticsService) {}

  ngOnInit(): void {
    this.load();
    this.api.getHealth().subscribe({
      next: h => this.health.set(h),
      error: () => this.health.set({ ga4: false, searchConsole: false }),
    });
  }

  changePeriod(p: AnalyticsPeriod): void {
    this.period.set(p);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.getWebsite(this.period()).subscribe({
      next: d => {
        this.data.set(d);
        this.trafficChart.set({
          labels: d.trafficSources.map(s => s.channel),
          datasets: [{ data: d.trafficSources.map(s => s.sessions) }],
        });
        this.loading.set(false);
      },
      error: () => {
        this.data.set(null);
        this.loading.set(false);
      },
    });
  }
}
