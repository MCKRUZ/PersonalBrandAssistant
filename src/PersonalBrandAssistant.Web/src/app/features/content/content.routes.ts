import { Routes } from '@angular/router';
import { ContentListComponent } from './content-list.component';

export const CONTENT_ROUTES: Routes = [
  { path: '', component: ContentListComponent },
  {
    path: 'new',
    loadComponent: () =>
      import('../../pages/content-editor/content-editor.component').then(m => m.ContentEditorComponent),
    data: { sidecarContext: 'content-editor' },
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
      import('../../pages/content-editor/content-editor.component').then(m => m.ContentEditorComponent),
    data: { sidecarContext: 'content-editor' },
  },
];
