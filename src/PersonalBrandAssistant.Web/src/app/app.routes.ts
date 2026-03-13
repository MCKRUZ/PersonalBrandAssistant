import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
  },
  {
    path: 'content',
    loadChildren: () =>
      import('./features/content/content.routes').then(
        (m) => m.CONTENT_ROUTES
      ),
  },
  {
    path: 'calendar',
    loadChildren: () =>
      import('./features/calendar/calendar.routes').then(
        (m) => m.CALENDAR_ROUTES
      ),
  },
  {
    path: 'analytics',
    loadChildren: () =>
      import('./features/analytics/analytics.routes').then(
        (m) => m.ANALYTICS_ROUTES
      ),
  },
  {
    path: 'platforms',
    loadChildren: () =>
      import('./features/platforms/platforms.routes').then(
        (m) => m.PLATFORMS_ROUTES
      ),
  },
  {
    path: 'settings',
    loadChildren: () =>
      import('./features/settings/settings.routes').then(
        (m) => m.SETTINGS_ROUTES
      ),
  },
  { path: '**', redirectTo: 'dashboard' },
];
