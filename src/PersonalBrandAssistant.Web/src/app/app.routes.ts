import { Routes } from '@angular/router';
import { LayoutComponent } from './shell/layout/layout.component';

export const routes: Routes = [
  {
    path: '',
    component: LayoutComponent,
    children: [
      { path: '', redirectTo: 'feed', pathMatch: 'full' },
      { path: 'feed', loadComponent: () => import('./features/feed/feed.component').then(m => m.FeedComponent) },
      { path: 'discover', loadComponent: () => import('./features/discover/discover.component').then(m => m.DiscoverComponent) },
      { path: 'ideas', loadChildren: () => import('./features/ideas/ideas.routes').then(m => m.IDEAS_ROUTES) },
      { path: 'content', loadComponent: () => import('./features/content/content.component').then(m => m.ContentComponent) },
      { path: 'calendar', loadComponent: () => import('./features/calendar/calendar.component').then(m => m.CalendarComponent) },
      { path: 'analytics', loadComponent: () => import('./features/analytics/analytics.component').then(m => m.AnalyticsComponent) },
      { path: 'listening', loadComponent: () => import('./features/listening/listening.component').then(m => m.ListeningComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) },
    ]
  }
];
