import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent),
    data: { title: 'Dashboard', sidecarContext: 'dashboard' },
  },
  {
    path: 'content',
    loadChildren: () => import('./features/content/content.routes').then(m => m.CONTENT_ROUTES),
    data: { title: 'Content', sidecarContext: 'content-list' },
  },
  {
    path: 'blog',
    loadComponent: () => import('./features/blog/blog.component').then(m => m.BlogComponent),
    data: { title: 'Blog', sidecarContext: 'blog-editor' },
  },
  {
    path: 'blog-pipeline',
    redirectTo: 'blog',
    pathMatch: 'full',
  },
  {
    path: 'calendar',
    loadChildren: () => import('./features/calendar/calendar.routes').then(m => m.CALENDAR_ROUTES),
    data: { title: 'Calendar', sidecarContext: 'calendar' },
  },
  {
    path: 'approval-queue',
    loadComponent: () => import('./pages/approval-queue/approval-queue.component').then(m => m.ApprovalQueueComponent),
    data: { title: 'Approval Queue', sidecarContext: 'approval-queue' },
  },
  {
    path: 'social',
    loadChildren: () => import('./features/social/social.routes').then(m => m.SOCIAL_ROUTES),
    data: { title: 'Social', sidecarContext: 'social' },
  },
  {
    path: 'platforms',
    loadChildren: () => import('./features/platforms/platforms.routes').then(m => m.PLATFORMS_ROUTES),
    data: { title: 'Platforms', sidecarContext: 'platforms' },
  },
  {
    path: 'analytics',
    loadChildren: () => import('./features/analytics/analytics.routes').then(m => m.ANALYTICS_ROUTES),
    data: { title: 'Analytics', sidecarContext: 'analytics' },
  },
  {
    path: 'news',
    loadChildren: () => import('./features/news/news.routes').then(m => m.NEWS_ROUTES),
    data: { title: 'News', sidecarContext: 'news' },
  },
  {
    path: 'automation',
    loadChildren: () => import('./features/automation/automation.routes').then(m => m.AUTOMATION_ROUTES),
    data: { title: 'Automation', sidecarContext: 'automation' },
  },
  {
    path: 'settings',
    loadChildren: () => import('./features/settings/settings.routes').then(m => m.SETTINGS_ROUTES),
    data: { title: 'Settings', sidecarContext: 'settings' },
  },
  { path: '**', redirectTo: 'dashboard' },
];
