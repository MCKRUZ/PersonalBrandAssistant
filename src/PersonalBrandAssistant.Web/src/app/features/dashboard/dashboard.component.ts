import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { KpiCardComponent } from './components/kpi-card.component';
import { RecentContentTableComponent } from './components/recent-content-table.component';
import { TrendSuggestionsPanelComponent } from './components/trend-suggestions-panel.component';
import { UpcomingSlotsPanelComponent } from './components/upcoming-slots-panel.component';
import { DashboardStore } from './store/dashboard.store';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule, PageHeaderComponent, LoadingSpinnerComponent,
    KpiCardComponent, RecentContentTableComponent,
    TrendSuggestionsPanelComponent, UpcomingSlotsPanelComponent,
  ],
  template: `
    <app-page-header title="Dashboard" />

    @if (store.loading()) {
      <app-loading-spinner message="Loading dashboard..." />
    } @else {
      <div class="grid mb-3">
        <div class="col-12 md:col-3">
          <app-kpi-card label="Total Content" [value]="store.kpis().totalContent" icon="pi pi-file" />
        </div>
        <div class="col-12 md:col-3">
          <app-kpi-card label="Pending Review" [value]="store.kpis().pendingReview" icon="pi pi-clock" />
        </div>
        <div class="col-12 md:col-3">
          <app-kpi-card label="Published This Week" [value]="store.kpis().publishedThisWeek" icon="pi pi-check-circle" />
        </div>
        <div class="col-12 md:col-3">
          <app-kpi-card label="Notifications" [value]="store.notifications().length" icon="pi pi-bell" />
        </div>
      </div>

      <div class="grid">
        <div class="col-12 md:col-8">
          <app-recent-content-table [items]="store.recentContent()" />
        </div>
        <div class="col-12 md:col-4">
          <app-trend-suggestions-panel [items]="store.trendSuggestions()" />
        </div>
      </div>

      <div class="grid mt-3">
        <div class="col-12 md:col-6">
          <app-upcoming-slots-panel [items]="store.upcomingSlots()" />
        </div>
      </div>
    }
  `,
})
export class DashboardComponent implements OnInit {
  readonly store = inject(DashboardStore);

  ngOnInit() {
    this.store.load(undefined);
  }
}
