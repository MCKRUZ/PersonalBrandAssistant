import { Routes } from '@angular/router';
import { PlatformsListComponent } from './platforms-list.component';
import { OAuthCallbackComponent } from './components/oauth-callback.component';

export const PLATFORMS_ROUTES: Routes = [
  {
    path: '',
    component: PlatformsListComponent,
    data: { title: 'Platforms', sidecarContext: 'platforms' },
  },
  { path: ':type/callback', component: OAuthCallbackComponent },
];
