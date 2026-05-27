import { Component } from '@angular/core';

@Component({
  selector: 'app-general-settings',
  standalone: true,
  template: `
    <div class="general">
      <p class="placeholder">General settings coming soon.</p>
    </div>
  `,
  styles: [`
    .general { padding: 8px 0; }
    .placeholder { color: #8a8a96; font-size: 14px; }
  `],
})
export class GeneralSettingsComponent {}
