import { Routes } from '@angular/router';
import { SettingsComponent } from './settings.component';

export const SETTINGS_ROUTES: Routes = [
  {
    path: '',
    component: SettingsComponent,
    children: [
      { path: '', redirectTo: 'general', pathMatch: 'full' },
      {
        path: 'general',
        loadComponent: () =>
          import('./general/general-settings.component').then(
            (m) => m.GeneralSettingsComponent
          ),
      },
      {
        path: 'platforms',
        loadComponent: () =>
          import('./platforms/platform-connections.component').then(
            (m) => m.PlatformConnectionsComponent
          ),
      },
    ],
  },
];
