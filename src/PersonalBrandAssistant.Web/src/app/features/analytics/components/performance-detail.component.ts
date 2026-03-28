import { Component, inject, OnInit } from '@angular/core';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Card } from 'primeng/card';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { PageHeaderComponent, PageAction } from '../../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { AnalyticsStore } from '../store/analytics.store';

@Component({
  selector: 'app-performance-detail',
  standalone: true,
  imports: [
    CommonModule, Card, TableModule, ButtonModule, Tag, DatePipe, DecimalPipe,
    PageHeaderComponent, LoadingSpinnerComponent, PlatformChipComponent,
  ],
  template: `
    @if (store.loading()) {
      <app-loading-spinner message="Loading report..." />
    } @else {
      @if (store.selectedReport(); as report) {
        <app-page-header [title]="report.title || 'Content Performance'" [actions]="actions" />

        <div class="grid">
          <div class="col-12 md:col-4">
            <p-card header="Overview">
              <div class="flex flex-column gap-2">
                <div><strong>Type:</strong> <p-tag [value]="report.contentType" severity="info" /></div>
                <div><strong>Published:</strong> {{ report.publishedAt | date:'medium' }}</div>
                <div><strong>Total Engagement:</strong> <span class="text-2xl font-bold">{{ report.totalEngagement | number }}</span></div>
              </div>
            </p-card>
          </div>
          <div class="col-12 md:col-8">
            <p-card header="Engagement by Platform">
              <p-table [value]="$any(report.engagementByPlatform)" styleClass="p-datatable-sm">
                <ng-template #header>
                  <tr>
                    <th>Platform</th>
                    <th>Views</th>
                    <th>Likes</th>
                    <th>Shares</th>
                    <th>Comments</th>
                    <th>Clicks</th>
                  </tr>
                </ng-template>
                <ng-template #body let-snap>
                  <tr>
                    <td><app-platform-chip [platform]="snap.platform" /></td>
                    <td>{{ snap.views | number }}</td>
                    <td>{{ snap.likes | number }}</td>
                    <td>{{ snap.shares | number }}</td>
                    <td>{{ snap.comments | number }}</td>
                    <td>{{ snap.clicks | number }}</td>
                  </tr>
                </ng-template>
              </p-table>
            </p-card>
          </div>
        </div>
      }
    }
  `,
})
export class PerformanceDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(AnalyticsStore);

  readonly actions: PageAction[] = [
    { label: 'Back', icon: 'pi pi-arrow-left', command: () => this.router.navigate(['/analytics']) },
  ];

  ngOnInit() {
    const contentId = this.route.snapshot.params['contentId'];
    this.store.loadContentReport(contentId);
  }
}
