import { Routes } from '@angular/router';
import { AnalyticsDashboardComponent } from './analytics-dashboard.component';

export const ANALYTICS_ROUTES: Routes = [
  { path: '', component: AnalyticsDashboardComponent },
  {
    path: ':contentId',
    loadComponent: () =>
      import('./components/performance-detail.component').then(m => m.PerformanceDetailComponent),
  },
];
