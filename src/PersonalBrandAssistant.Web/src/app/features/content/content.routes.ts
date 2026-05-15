import { Routes } from '@angular/router';

export const CONTENT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./content-list/content-list.component').then(
        (m) => m.ContentListComponent
      ),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./content-editor/content-editor.component').then(
        (m) => m.ContentEditorComponent
      ),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./content-editor/content-editor.component').then(
        (m) => m.ContentEditorComponent
      ),
  },
];
