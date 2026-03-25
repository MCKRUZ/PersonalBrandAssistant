import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { DateRangePickerComponent } from './components/date-range-picker.component';
import { EngagementChartComponent } from './components/engagement-chart.component';
import { TopContentTableComponent } from './components/top-content-table.component';
import { AnalyticsStore } from './store/analytics.store';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [
    CommonModule, PageHeaderComponent, LoadingSpinnerComponent, EmptyStateComponent,
    DateRangePickerComponent, EngagementChartComponent, TopContentTableComponent,
  ],
  template: `
    <app-page-header title="Analytics" />

    <div class="mb-3">
      <app-date-range-picker (rangeChanged)="onRangeChanged($event)" />
    </div>

    @if (store.loading()) {
      <app-loading-spinner message="Loading analytics..." />
    } @else if (store.topContent().length === 0) {
      <app-empty-state message="No analytics data yet. Publish content to see performance." icon="pi pi-chart-line" />
    } @else {
      <app-engagement-chart [items]="store.topContent()" />
      <div class="mt-3">
        <app-top-content-table [items]="store.topContent()" (viewDetail)="viewDetail($event)" />
      </div>
    }
  `,
})
export class AnalyticsDashboardComponent implements OnInit {
  private readonly router = inject(Router);
  readonly store = inject(AnalyticsStore);

  ngOnInit() {
    this.store.loadDashboard();
  }

  onRangeChanged(range: { from: string; to: string }) {
    this.store.setPeriod(range);
  }

  viewDetail(contentId: string) {
    this.router.navigate(['/analytics', contentId]);
  }
}
