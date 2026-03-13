import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { Toast } from 'primeng/toast';
import { ButtonModule } from 'primeng/button';
import { UiStore } from './core/store/ui.store';

interface NavItem {
  label: string;
  icon: string;
  route: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Toast, ButtonModule],
  template: `
    <p-toast />
    <div class="app-layout" [class.sidebar-collapsed]="uiStore.sidebarCollapsed()">
      <aside class="sidebar" role="navigation" aria-label="Main navigation">
        <div class="sidebar-header">
          <span class="logo">PBA</span>
        </div>
        <nav>
          @for (item of navItems; track item.route) {
            <a
              [routerLink]="item.route"
              routerLinkActive="active"
              class="nav-item"
            >
              <i [class]="item.icon"></i>
              <span class="nav-label">{{ item.label }}</span>
            </a>
          }
        </nav>
      </aside>
      <div class="main-area">
        <header class="top-bar">
          <p-button
            icon="pi pi-bars"
            [text]="true"
            (onClick)="uiStore.toggleSidebar()"
            [attr.aria-label]="uiStore.sidebarCollapsed() ? 'Expand sidebar' : 'Collapse sidebar'"
            [attr.aria-expanded]="!uiStore.sidebarCollapsed()"
          />
          <span class="app-title">Personal Brand Assistant</span>
        </header>
        <main class="content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styleUrl: './app.component.scss',
})
export class AppComponent {
  readonly uiStore = inject(UiStore);

  readonly navItems: NavItem[] = [
    { label: 'Dashboard', icon: 'pi pi-chart-bar', route: '/dashboard' },
    { label: 'Content', icon: 'pi pi-file', route: '/content' },
    { label: 'Calendar', icon: 'pi pi-calendar', route: '/calendar' },
    { label: 'Analytics', icon: 'pi pi-chart-line', route: '/analytics' },
    { label: 'Platforms', icon: 'pi pi-share-alt', route: '/platforms' },
    { label: 'Settings', icon: 'pi pi-cog', route: '/settings' },
  ];
}
