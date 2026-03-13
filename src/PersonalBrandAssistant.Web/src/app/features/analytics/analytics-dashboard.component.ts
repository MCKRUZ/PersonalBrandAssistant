import { Component } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [PageHeaderComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Analytics" />
    <app-empty-state message="Analytics coming soon" icon="pi pi-chart-line" />
  `,
})
export class AnalyticsDashboardComponent {}
