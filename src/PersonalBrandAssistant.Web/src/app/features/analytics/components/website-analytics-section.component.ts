import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { Skeleton } from 'primeng/skeleton';
import { WebsiteAnalyticsResponse } from '../models/dashboard.model';

function formatDuration(seconds: number): string {
  const total = Math.round(seconds);
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  return `${mins}m ${secs}s`;
}

@Component({
  selector: 'app-website-analytics-section',
  standalone: true,
  imports: [CommonModule, TableModule, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="website-section">
      <div class="section-header">
        <i class="pi pi-globe section-icon"></i>
        <span>Website Analytics</span>
      </div>

      @if (data(); as d) {
        <div class="overview-grid">
          @for (m of overviewMetrics(); track m.label) {
            <div class="metric-card">
              <div class="metric-label">{{ m.label }}</div>
              <div class="metric-value">{{ m.value }}</div>
            </div>
          }
        </div>

        @if (mutableTopPages().length > 0) {
          <div class="table-section">
            <h4 class="table-title">Top Pages</h4>
            <p-table [value]="mutableTopPages()" [rows]="10" class="top-pages-table" styleClass="p-datatable-sm" aria-label="Top pages">
              <ng-template #header>
                <tr>
                  <th>Page Path</th>
                  <th>Views</th>
                  <th>Users</th>
                </tr>
              </ng-template>
              <ng-template #body let-page>
                <tr>
                  <td class="page-path">{{ page.pagePath }}</td>
                  <td>{{ page.views | number }}</td>
                  <td>{{ page.users | number }}</td>
                </tr>
              </ng-template>
            </p-table>
          </div>
        } @else {
          <p class="empty-text">No data for this period</p>
        }

        <div class="two-col-grid">
          <div>
            <h4 class="table-title">Traffic Sources</h4>
            @if (mutableTrafficSources().length > 0) {
              <p-table [value]="mutableTrafficSources()" styleClass="p-datatable-sm" aria-label="Traffic sources">
                <ng-template #header>
                  <tr>
                    <th>Channel</th>
                    <th>Sessions</th>
                    <th>Users</th>
                  </tr>
                </ng-template>
                <ng-template #body let-source>
                  <tr>
                    <td>{{ source.channel }}</td>
                    <td>{{ source.sessions | number }}</td>
                    <td>{{ source.users | number }}</td>
                  </tr>
                </ng-template>
              </p-table>
            } @else {
              <p class="empty-text">No data for this period</p>
            }
          </div>
          <div>
            <h4 class="table-title">Search Queries</h4>
            @if (mutableSearchQueries().length > 0) {
              <p-table [value]="mutableSearchQueries()" styleClass="p-datatable-sm" aria-label="Search queries">
                <ng-template #header>
                  <tr>
                    <th>Query</th>
                    <th>Clicks</th>
                    <th>Impressions</th>
                    <th>CTR</th>
                    <th>Position</th>
                  </tr>
                </ng-template>
                <ng-template #body let-q>
                  <tr>
                    <td>{{ q.query }}</td>
                    <td>{{ q.clicks | number }}</td>
                    <td>{{ q.impressions | number }}</td>
                    <td>{{ (q.ctr * 100).toFixed(1) }}%</td>
                    <td>{{ q.position.toFixed(1) }}</td>
                  </tr>
                </ng-template>
              </p-table>
            } @else {
              <p class="empty-text">No data for this period</p>
            }
          </div>
        </div>
      } @else {
        <div class="overview-grid">
          @for (i of skeletonCards; track i) {
            <p-skeleton width="100%" height="70px" borderRadius="10px" />
          }
        </div>
        <p-skeleton width="100%" height="200px" borderRadius="10px" styleClass="mt-2" />
      }
    </div>
  `,
  styles: `
    .website-section {
      background: var(--p-surface-900, #111118);
      border: 1px solid var(--p-surface-700, #25252f);
      border-radius: 12px;
      padding: 1.25rem;
    }
    .section-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 1rem;
      font-weight: 700;
      margin-bottom: 1rem;
    }
    .section-icon {
      color: #8b5cf6;
      font-size: 1.1rem;
    }
    .overview-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
      gap: 0.75rem;
      margin-bottom: 1.25rem;
    }
    .metric-card {
      background: var(--p-surface-800, #1a1a24);
      border-radius: 10px;
      padding: 0.75rem 0.9rem;
    }
    .metric-label {
      font-size: 0.68rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--p-text-muted-color, #71717a);
      margin-bottom: 0.3rem;
    }
    .metric-value {
      font-size: 1.25rem;
      font-weight: 800;
      letter-spacing: -0.02em;
    }

    .table-section {
      margin-bottom: 1rem;
    }
    .table-title {
      font-size: 0.8rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--p-text-muted-color, #71717a);
      margin: 0 0 0.5rem 0;
    }
    .page-path {
      font-family: monospace;
      font-size: 0.82rem;
      max-width: 300px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .two-col-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1rem;
    }
    @media (max-width: 768px) {
      .two-col-grid { grid-template-columns: 1fr; }
    }

    .empty-text {
      font-size: 0.8rem;
      color: var(--p-text-muted-color, #71717a);
      font-style: italic;
    }
  `,
})
export class WebsiteAnalyticsSectionComponent {
  readonly data = input<WebsiteAnalyticsResponse | null>(null);
  readonly skeletonCards = [1, 2, 3, 4, 5, 6];

  readonly overviewMetrics = computed(() => {
    const d = this.data();
    if (!d) return [];
    const o = d.overview;
    return [
      { label: 'Active Users', value: o.activeUsers.toLocaleString('en-US') },
      { label: 'Sessions', value: o.sessions.toLocaleString('en-US') },
      { label: 'Page Views', value: o.pageViews.toLocaleString('en-US') },
      { label: 'Avg Duration', value: formatDuration(o.avgSessionDuration) },
      { label: 'Bounce Rate', value: o.bounceRate.toFixed(1) + '%' },
      { label: 'New Users', value: o.newUsers.toLocaleString('en-US') },
    ];
  });

  readonly mutableTopPages = computed(() => {
    const d = this.data();
    return d ? [...d.topPages] : [];
  });

  readonly mutableTrafficSources = computed(() => {
    const d = this.data();
    return d ? [...d.trafficSources] : [];
  });

  readonly mutableSearchQueries = computed(() => {
    const d = this.data();
    return d ? [...d.searchQueries] : [];
  });
}
