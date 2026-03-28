import { Routes } from '@angular/router';
import { PlatformsListComponent } from './platforms-list.component';

export const PLATFORMS_ROUTES: Routes = [
  { path: '', component: PlatformsListComponent },
  {
    path: ':type/callback',
    loadComponent: () =>
      import('./components/oauth-callback.component').then(
        (m) => m.OAuthCallbackComponent
      ),
  },
];
