import { Component } from '@angular/core';

@Component({
  selector: 'app-settings',
  standalone: true,
  template: `
    <div class="page">
      <h1>Settings</h1>
      <p class="subtitle">Configuration</p>
    </div>
  `,
  styles: [`
    .page { padding: 8px 0; }
    h1 { font-size: 24px; font-weight: 600; margin: 0 0 4px; color: #f0f6fc; }
    .subtitle { color: #8b949e; margin: 0; font-size: 14px; }
  `]
})
export class SettingsComponent {}
