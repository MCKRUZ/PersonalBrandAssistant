import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterOutlet, NavigationEnd, ActivatedRoute } from '@angular/router';
import { filter, map } from 'rxjs';
import { Toast } from 'primeng/toast';
import { UiStore } from './core/store/ui.store';
import { SidebarComponent } from './shell/sidebar/sidebar.component';
import { TopbarComponent } from './shell/topbar/topbar.component';
import { SidecarComponent } from './shell/sidecar/sidecar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, Toast, SidebarComponent, TopbarComponent, SidecarComponent],
  template: `
    <p-toast />
    <div class="app-layout" [class.sidebar-collapsed]="uiStore.sidebarCollapsed()" [class.sidecar-open]="uiStore.sidecarOpen()">
      <app-sidebar [collapsed]="uiStore.sidebarCollapsed()" (toggleCollapse)="uiStore.toggleSidebar()" />
      <div class="main-area">
        <app-topbar
          [pageTitle]="pageTitle()"
          [sidebarCollapsed]="uiStore.sidebarCollapsed()"
          (toggleSidebar)="uiStore.toggleSidebar()"
          (toggleSidecar)="uiStore.toggleSidecar()" />
        <main class="content">
          <router-outlet />
        </main>
      </div>
      @if (uiStore.sidecarOpen()) {
        <aside class="sidecar-panel">
          <app-sidecar />
        </aside>
      }
    </div>
  `,
  styleUrl: './app.component.scss',
})
export class AppComponent {
  readonly uiStore = inject(UiStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly navEnd$ = this.router.events.pipe(
    filter((e): e is NavigationEnd => e instanceof NavigationEnd),
    map(() => {
      let r = this.route;
      while (r.firstChild) r = r.firstChild;
      return r.snapshot.data['title'] as string | undefined;
    }),
  );

  private readonly routeTitle = toSignal(this.navEnd$, { initialValue: undefined });
  pageTitle = computed(() => this.routeTitle() ?? 'Dashboard');
}
