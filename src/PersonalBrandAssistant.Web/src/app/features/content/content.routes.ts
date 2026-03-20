import { Routes } from '@angular/router';
import { ContentListComponent } from './content-list.component';

export const CONTENT_ROUTES: Routes = [
  { path: '', component: ContentListComponent },
  {
    path: 'new',
    loadComponent: () =>
      import('./components/content-form.component').then(m => m.ContentFormComponent),
  },
  {
    path: 'trends',
    loadComponent: () =>
      import('./trends-list.component').then(m => m.TrendsListComponent),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./components/content-detail.component').then(m => m.ContentDetailComponent),
  },
  {
    path: ':id/edit',
    loadComponent: () =>
      import('./components/content-form.component').then(m => m.ContentFormComponent),
  },
];
