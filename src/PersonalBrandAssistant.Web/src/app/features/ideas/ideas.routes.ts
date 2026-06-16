import { Routes } from '@angular/router';

export const IDEAS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./ideas.component').then((m) => m.IdeasComponent),
  },
  {
    path: 'sources',
    loadComponent: () =>
      import('./pages/idea-sources/idea-sources.component').then(
        (m) => m.IdeaSourcesPageComponent
      ),
  },
  {
    path: 'brand-profile',
    loadComponent: () =>
      import('./pages/brand-profile/brand-profile.component').then(
        (m) => m.BrandProfileComponent
      ),
  },
];
