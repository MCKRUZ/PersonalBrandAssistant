import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { Toast } from 'primeng/toast';
import { ButtonModule } from 'primeng/button';
import { UiStore } from './core/store/ui.store';
import { NotificationBellComponent } from './shared/components/notification-bell/notification-bell.component';
import { SidecarChatPanelComponent } from './features/sidecar/sidecar-chat-panel.component';

interface NavItem {
  label: string;
  icon: string;
  route: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Toast, ButtonModule, NotificationBellComponent, SidecarChatPanelComponent],
  template: `
    <p-toast />
    <div class="app-layout" [class.sidebar-collapsed]="uiStore.sidebarCollapsed()" [class.sidecar-open]="uiStore.sidecarOpen()">
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
          <div class="flex-1"></div>
          <app-notification-bell />
          <p-button
            [icon]="uiStore.sidecarOpen() ? 'pi pi-times' : 'pi pi-comment'"
            [text]="true"
            [severity]="uiStore.sidecarOpen() ? 'secondary' : 'help'"
            (onClick)="uiStore.toggleSidecar()"
            aria-label="Toggle Claude assistant"
          ></p-button>
        </header>
        <main class="content">
          <router-outlet />
        </main>
      </div>
      @if (uiStore.sidecarOpen()) {
        <aside class="sidecar-panel">
          <app-sidecar-chat-panel />
        </aside>
      }
    </div>
  `,
  styleUrl: './app.component.scss',
})
export class AppComponent {
  readonly uiStore = inject(UiStore);

  readonly navItems: NavItem[] = [
    { label: 'Dashboard', icon: 'pi pi-chart-bar', route: '/dashboard' },
    { label: 'News', icon: 'pi pi-globe', route: '/news' },
    { label: 'Content', icon: 'pi pi-file', route: '/content' },
    { label: 'Calendar', icon: 'pi pi-calendar', route: '/calendar' },
    { label: 'Analytics', icon: 'pi pi-chart-line', route: '/analytics' },
    { label: 'Social', icon: 'pi pi-users', route: '/social' },
    { label: 'Platforms', icon: 'pi pi-share-alt', route: '/platforms' },
    { label: 'Settings', icon: 'pi pi-cog', route: '/settings' },
  ];
}
