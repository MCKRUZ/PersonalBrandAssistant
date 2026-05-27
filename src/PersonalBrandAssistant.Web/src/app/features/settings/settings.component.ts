import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="page">
      <h1>Settings</h1>
      <nav class="tabs">
        <a routerLink="general" routerLinkActive="active">General</a>
        <a routerLink="platforms" routerLinkActive="active">Platforms</a>
      </nav>
      <div class="tab-content">
        <router-outlet />
      </div>
    </div>
  `,
  styles: [`
    .page { padding: 8px 0; }
    h1 { font-size: 24px; font-weight: 600; margin: 0 0 16px; color: #f0f0f5; }
    .tabs {
      display: flex; gap: 0; border-bottom: 1px solid #2c2c36; margin-bottom: 24px;
    }
    .tabs a {
      padding: 10px 20px; color: #8a8a96; text-decoration: none; font-size: 14px;
      border-bottom: 2px solid transparent; transition: color 0.15s, border-color 0.15s;
    }
    .tabs a:hover { color: #f0f0f5; }
    .tabs a.active { color: #c87156; border-bottom-color: #c87156; }
    .tab-content { }
  `],
})
export class SettingsComponent {}
