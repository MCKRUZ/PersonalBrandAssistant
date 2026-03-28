import { Routes } from '@angular/router';

export const NEWS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./news-hub.component').then((m) => m.NewsHubComponent),
  },
];
