import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { SelectButton } from 'primeng/selectbutton';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { EngagementTimelineChartComponent } from '../../features/analytics/components/engagement-timeline-chart.component';
import { TopContentTableComponent } from '../../features/analytics/components/top-content-table.component';
import { PlatformHealthCardsComponent } from '../../features/analytics/components/platform-health-cards.component';
import { DashboardKpiCardsComponent } from '../../features/analytics/components/dashboard-kpi-cards.component';
import { BestTimesHeatmapComponent } from './components/best-times-heatmap.component';
import { AnalyticsStore } from './analytics.store';
import { AnalyticsApiService } from './analytics-api.service';
import { DashboardPeriod } from '../../features/analytics/models/dashboard.model';

@Component({
  selector: 'app-analytics-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule, CardModule, ButtonModule, SelectButton,
    PageHeaderComponent, LoadingSpinnerComponent, EmptyStateComponent,
    EngagementTimelineChartComponent, TopContentTableComponent,
    PlatformHealthCardsComponent, DashboardKpiCardsComponent,
    BestTimesHeatmapComponent,
  ],
  providers: [AnalyticsStore],
  template: `
    <div class="analytics-page">
      <div class="header-row">
        <app-page-header title="Analytics" />
        <div class="header-actions">
          <p-selectButton
            [options]="periodOptions"
            [ngModel]="store.period()"
            (ngModelChange)="onPeriodChange($event)"
            optionLabel="label"
            optionValue="value"
          />
          <p-button icon="pi pi-refresh" [text]="true" (onClick)="store.refreshDashboard()" [loading]="store.loading()" pTooltip="Refresh data" />
        </div>
      </div>

      @if (store.loading() && !store.hasData()) {
        <app-loading-spinner message="Loading analytics..." />
      } @else if (!store.hasData()) {
        <app-empty-state message="No analytics data yet. Publish content to see insights." icon="pi pi-chart-bar" />
      } @else {
        <app-dashboard-kpi-cards [summary]="$any(store.summary())" />

        <div class="charts-row">
          <app-engagement-timeline-chart [timeline]="$any(store.timeline())" class="chart-main" />
        </div>

        <app-platform-health-cards [platforms]="$any(store.platformSummaries())" />

        <app-top-content-table [items]="$any(store.topContent())" />

        <div class="bottom-row">
          <app-best-times-heatmap [heatmap]="store.heatmap()" />
        </div>
      }
    </div>
  `,
  styles: `
    .analytics-page { display: flex; flex-direction: column; gap: 1.5rem; }
    .header-row { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 1rem; }
    .header-actions { display: flex; align-items: center; gap: 0.75rem; }
    .charts-row { display: grid; grid-template-columns: 1fr; gap: 1.5rem; }
    .bottom-row { display: grid; grid-template-columns: 1fr; gap: 1.5rem; }
  `,
})
export class AnalyticsComponent {
  readonly store = inject(AnalyticsStore);

  readonly periodOptions = [
    { label: '7d', value: '7d' },
    { label: '14d', value: '14d' },
    { label: '30d', value: '30d' },
  ];

  onPeriodChange(period: DashboardPeriod) {
    this.store.setPeriod(period);
  }
}
