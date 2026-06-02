import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from '../sidebar/sidebar.component';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent],
  template: `
    <app-sidebar />
    <main class="layout-content">
      <router-outlet />
    </main>
  `,
  styles: [`
    :host {
      display: flex;
      height: 100vh;
      overflow: hidden;
    }
    .layout-content {
      flex: 1;
      overflow-y: auto;
      background: var(--surface-base);
    }
    @media (max-width: 768px) {
      :host {
        flex-direction: column;
      }
      .layout-content {
        padding-bottom: 56px;
      }
    }
  `],
})
export class LayoutComponent {}
