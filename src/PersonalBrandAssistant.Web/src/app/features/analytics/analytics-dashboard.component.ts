import { ChangeDetectionStrategy, Component, computed, inject, OnInit, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';
import { Tooltip } from 'primeng/tooltip';
import { Message } from 'primeng/message';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TopContentTableComponent } from './components/top-content-table.component';
import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';
import { DateRangeSelectorComponent } from './components/date-range-selector.component';
import { EngagementTimelineChartComponent } from './components/engagement-timeline-chart.component';
import { PlatformBreakdownChartComponent } from './components/platform-breakdown-chart.component';
import { PlatformHealthCardsComponent } from './components/platform-health-cards.component';
import { WebsiteAnalyticsSectionComponent } from './components/website-analytics-section.component';
import { SubstackSectionComponent } from './components/substack-section.component';
import { ImportPostDialogComponent } from './components/import-post-dialog.component';
import { AnalyticsStore } from './store/analytics.store';
import { DashboardPeriod } from './models/dashboard.model';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [
    CommonModule, ButtonModule, Skeleton, Tooltip, Message,
    PageHeaderComponent, EmptyStateComponent,
    TopContentTableComponent, DashboardKpiCardsComponent, DateRangeSelectorComponent,
    EngagementTimelineChartComponent, PlatformBreakdownChartComponent,
    PlatformHealthCardsComponent, WebsiteAnalyticsSectionComponent, SubstackSectionComponent,
    ImportPostDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dashboard-header">
      <app-page-header title="Brand Analytics" />
      <div class="header-controls">
        <app-date-range-selector
          [activePeriod]="store.period()"
          (periodChanged)="onPeriodChanged($event)"
        />
        <p-button
          label="Import Post"
          icon="pi pi-download"
          [outlined]="true"
          size="small"
          (onClick)="importDialog().open()"
          pTooltip="Import an existing social media post"
        />
        <p-button
          icon="pi pi-refresh"
          [text]="true"
          [loading]="store.loading()"
          (onClick)="onRefresh()"
          pTooltip="Refresh data"
          ariaLabel="Refresh dashboard data"
        />
        @if (store.lastRefreshedAt(); as ts) {
          <span class="staleness-text" [class.stale]="store.isStale()">
            Updated {{ getRelativeTime(ts) }}
          </span>
        }
      </div>
    </div>

    @if (store.loading() && !store.summary()) {
      <div class="kpi-skeleton-grid">
        @for (i of skeletonCards; track i) {
          <p-skeleton width="100%" height="100px" borderRadius="12px" />
        }
      </div>
      <p-skeleton width="100%" height="300px" borderRadius="12px" styleClass="mt-3" />
    } @else if (!store.summary() && store.topContent().length === 0) {
      <app-empty-state message="No analytics data yet. Publish content to see performance." icon="pi pi-chart-line" />
    } @else {
      <app-dashboard-kpi-cards [summary]="store.summary()" />

      @if (hasErrors()) {
        <p-message severity="warn" styleClass="mt-2 w-full">
          Some sections failed to load: {{ errorSections() }}. Try refreshing.
        </p-message>
      }

      <div class="charts-row mt-3">
        <app-engagement-timeline-chart [timeline]="store.timeline()" [loading]="store.loading()" />
        <app-platform-breakdown-chart [timeline]="store.timeline()" [loading]="store.loading()" />
      </div>

      <div class="mt-3">
        <app-platform-health-cards [platforms]="store.platformSummaries()" />
      </div>

      <div class="mt-3">
        <app-top-content-table [items]="store.topContent()" (viewDetail)="viewDetail($event)" />
      </div>

      <div class="bottom-row mt-3">
        <app-website-analytics-section [data]="store.websiteData()" />
        <app-substack-section [posts]="store.substackPosts()" />
      </div>
    }

    <app-import-post-dialog (imported)="onImported()" />
  `,
  styles: `
    .dashboard-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 1rem;
      margin-bottom: 1.5rem;
    }
    .header-controls {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-wrap: wrap;
    }
    .staleness-text {
      font-size: 0.8rem;
      color: var(--p-text-muted-color, #71717a);
    }
    .staleness-text.stale {
      color: #f59e0b;
    }
    .kpi-skeleton-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 1rem;
    }
    .charts-row {
      display: grid;
      grid-template-columns: 2fr 1fr;
      gap: 1rem;
    }
    .bottom-row {
      display: grid;
      grid-template-columns: 3fr 1fr;
      gap: 1rem;
    }
    @media (max-width: 1024px) {
      .bottom-row { grid-template-columns: 1fr; }
    }
  `,
})
export class AnalyticsDashboardComponent implements OnInit {
  private readonly router = inject(Router);
  readonly store = inject(AnalyticsStore);
  readonly importDialog = viewChild.required(ImportPostDialogComponent);
  readonly skeletonCards = [1, 2, 3, 4, 5, 6];

  readonly hasErrors = computed(() => {
    const e = this.store.errors();
    return !!(e.summary || e.timeline || e.platforms || e.website || e.substack || e.topContent);
  });

  readonly errorSections = computed(() => {
    const e = this.store.errors();
    const sections: string[] = [];
    if (e.summary) sections.push('Summary');
    if (e.timeline) sections.push('Timeline');
    if (e.platforms) sections.push('Platforms');
    if (e.website) sections.push('Website');
    if (e.substack) sections.push('Substack');
    if (e.topContent) sections.push('Top Content');
    return sections.join(', ');
  });

  ngOnInit() {
    this.store.loadDashboard();
  }

  onPeriodChanged(period: DashboardPeriod) {
    this.store.setPeriod(period);
  }

  onRefresh() {
    this.store.refreshDashboard();
  }

  viewDetail(contentId: string) {
    this.router.navigate(['/analytics', contentId]);
  }

  onImported() {
    this.store.refreshDashboard();
  }

  getRelativeTime(iso: string): string {
    const diffMs = Date.now() - new Date(iso).getTime();
    const mins = Math.floor(diffMs / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hours = Math.floor(mins / 60);
    return `${hours}h ago`;
  }
}
