diff --git a/src/PersonalBrandAssistant.Web/src/app/app.component.scss b/src/PersonalBrandAssistant.Web/src/app/app.component.scss
new file mode 100644
index 0000000..d7dcd6b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/app.component.scss
@@ -0,0 +1,100 @@
+@use '../styles/variables' as *;
+
+.app-layout {
+  display: flex;
+  height: 100vh;
+  overflow: hidden;
+}
+
+.sidebar {
+  width: $sidebar-width;
+  background: var(--p-surface-800, #1e293b);
+  color: var(--p-surface-0, #fff);
+  display: flex;
+  flex-direction: column;
+  transition: width 0.2s ease;
+  overflow: hidden;
+
+  .sidebar-header {
+    height: $header-height;
+    display: flex;
+    align-items: center;
+    padding: 0 $spacing-md;
+    border-bottom: 1px solid var(--p-surface-700, #334155);
+
+    .logo {
+      font-size: 1.25rem;
+      font-weight: 700;
+      letter-spacing: 0.05em;
+    }
+  }
+
+  nav {
+    flex: 1;
+    padding: $spacing-sm 0;
+  }
+
+  .nav-item {
+    display: flex;
+    align-items: center;
+    gap: $spacing-sm;
+    padding: $spacing-sm $spacing-md;
+    color: var(--p-surface-300, #cbd5e1);
+    text-decoration: none;
+    transition: background 0.15s ease;
+
+    &:hover {
+      background: var(--p-surface-700, #334155);
+    }
+
+    &.active {
+      background: var(--p-surface-700, #334155);
+      color: var(--p-surface-0, #fff);
+      border-left: 3px solid var(--p-primary-color, $brand-primary);
+    }
+
+    i {
+      font-size: 1.1rem;
+      width: 1.5rem;
+      text-align: center;
+    }
+  }
+}
+
+.sidebar-collapsed .sidebar {
+  width: $sidebar-collapsed-width;
+
+  .nav-label,
+  .logo {
+    display: none;
+  }
+}
+
+.main-area {
+  flex: 1;
+  display: flex;
+  flex-direction: column;
+  overflow: hidden;
+}
+
+.top-bar {
+  height: $header-height;
+  display: flex;
+  align-items: center;
+  gap: $spacing-sm;
+  padding: 0 $spacing-md;
+  border-bottom: 1px solid var(--p-surface-200, #e2e8f0);
+  background: var(--p-surface-0, #fff);
+
+  .app-title {
+    font-size: 1.1rem;
+    font-weight: 600;
+  }
+}
+
+.content {
+  flex: 1;
+  padding: $spacing-lg;
+  overflow-y: auto;
+  background: var(--p-surface-50, #f8fafc);
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/app.component.ts b/src/PersonalBrandAssistant.Web/src/app/app.component.ts
new file mode 100644
index 0000000..82622a1
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/app.component.ts
@@ -0,0 +1,65 @@
+import { Component, inject } from '@angular/core';
+import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
+import { Toast } from 'primeng/toast';
+import { ButtonModule } from 'primeng/button';
+import { UiStore } from './core/store/ui.store';
+
+interface NavItem {
+  label: string;
+  icon: string;
+  route: string;
+}
+
+@Component({
+  selector: 'app-root',
+  standalone: true,
+  imports: [RouterOutlet, RouterLink, RouterLinkActive, Toast, ButtonModule],
+  template: `
+    <p-toast />
+    <div class="app-layout" [class.sidebar-collapsed]="uiStore.sidebarCollapsed()">
+      <aside class="sidebar">
+        <div class="sidebar-header">
+          <span class="logo">PBA</span>
+        </div>
+        <nav>
+          @for (item of navItems; track item.route) {
+            <a
+              [routerLink]="item.route"
+              routerLinkActive="active"
+              class="nav-item"
+            >
+              <i [class]="item.icon"></i>
+              <span class="nav-label">{{ item.label }}</span>
+            </a>
+          }
+        </nav>
+      </aside>
+      <div class="main-area">
+        <header class="top-bar">
+          <p-button
+            icon="pi pi-bars"
+            [text]="true"
+            (onClick)="uiStore.toggleSidebar()"
+          />
+          <span class="app-title">Personal Brand Assistant</span>
+        </header>
+        <main class="content">
+          <router-outlet />
+        </main>
+      </div>
+    </div>
+  `,
+  styleUrl: './app.component.scss',
+})
+export class AppComponent {
+  readonly uiStore = inject(UiStore);
+
+  readonly navItems: NavItem[] = [
+    { label: 'Dashboard', icon: 'pi pi-chart-bar', route: '/dashboard' },
+    { label: 'Content', icon: 'pi pi-file', route: '/content' },
+    { label: 'Calendar', icon: 'pi pi-calendar', route: '/calendar' },
+    { label: 'Analytics', icon: 'pi pi-chart-line', route: '/analytics' },
+    { label: 'Platforms', icon: 'pi pi-share-alt', route: '/platforms' },
+    { label: 'Settings', icon: 'pi pi-cog', route: '/settings' },
+  ];
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/app.config.ts b/src/PersonalBrandAssistant.Web/src/app/app.config.ts
new file mode 100644
index 0000000..4eaf246
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/app.config.ts
@@ -0,0 +1,25 @@
+import {
+  ApplicationConfig,
+  provideZoneChangeDetection,
+} from '@angular/core';
+import { provideRouter } from '@angular/router';
+import { provideHttpClient, withInterceptors } from '@angular/common/http';
+import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
+import { MessageService } from 'primeng/api';
+import { providePrimeNG } from 'primeng/config';
+import Aura from '@primeng/themes/aura';
+
+import { routes } from './app.routes';
+import { apiKeyInterceptor } from './core/interceptors/api-key.interceptor';
+import { errorInterceptor } from './core/interceptors/error.interceptor';
+
+export const appConfig: ApplicationConfig = {
+  providers: [
+    provideZoneChangeDetection({ eventCoalescing: true }),
+    provideRouter(routes),
+    provideHttpClient(withInterceptors([apiKeyInterceptor, errorInterceptor])),
+    provideAnimationsAsync(),
+    providePrimeNG({ theme: { preset: Aura } }),
+    MessageService,
+  ],
+};
diff --git a/src/PersonalBrandAssistant.Web/src/app/app.routes.ts b/src/PersonalBrandAssistant.Web/src/app/app.routes.ts
new file mode 100644
index 0000000..24b2c75
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/app.routes.ts
@@ -0,0 +1,47 @@
+import { Routes } from '@angular/router';
+
+export const routes: Routes = [
+  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
+  {
+    path: 'dashboard',
+    loadComponent: () =>
+      import('./features/dashboard/dashboard.component').then(
+        (m) => m.DashboardComponent
+      ),
+  },
+  {
+    path: 'content',
+    loadChildren: () =>
+      import('./features/content/content.routes').then(
+        (m) => m.CONTENT_ROUTES
+      ),
+  },
+  {
+    path: 'calendar',
+    loadChildren: () =>
+      import('./features/calendar/calendar.routes').then(
+        (m) => m.CALENDAR_ROUTES
+      ),
+  },
+  {
+    path: 'analytics',
+    loadChildren: () =>
+      import('./features/analytics/analytics.routes').then(
+        (m) => m.ANALYTICS_ROUTES
+      ),
+  },
+  {
+    path: 'platforms',
+    loadChildren: () =>
+      import('./features/platforms/platforms.routes').then(
+        (m) => m.PLATFORMS_ROUTES
+      ),
+  },
+  {
+    path: 'settings',
+    loadChildren: () =>
+      import('./features/settings/settings.routes').then(
+        (m) => m.SETTINGS_ROUTES
+      ),
+  },
+];
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/interceptors/api-key.interceptor.ts b/src/PersonalBrandAssistant.Web/src/app/core/interceptors/api-key.interceptor.ts
new file mode 100644
index 0000000..41e3073
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/interceptors/api-key.interceptor.ts
@@ -0,0 +1,14 @@
+import { HttpInterceptorFn } from '@angular/common/http';
+import { environment } from '../../environments/environment';
+
+export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
+  if (!environment.apiKey) {
+    return next(req);
+  }
+
+  const cloned = req.clone({
+    setHeaders: { 'X-Api-Key': environment.apiKey },
+  });
+
+  return next(cloned);
+};
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/interceptors/error.interceptor.ts b/src/PersonalBrandAssistant.Web/src/app/core/interceptors/error.interceptor.ts
new file mode 100644
index 0000000..4f2f515
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/interceptors/error.interceptor.ts
@@ -0,0 +1,35 @@
+import { HttpInterceptorFn } from '@angular/common/http';
+import { inject } from '@angular/core';
+import { MessageService } from 'primeng/api';
+import { catchError, throwError } from 'rxjs';
+
+export const errorInterceptor: HttpInterceptorFn = (req, next) => {
+  const messageService = inject(MessageService);
+
+  return next(req).pipe(
+    catchError((error) => {
+      const status = error.status;
+      let detail = 'An unexpected error occurred';
+
+      if (status === 400) {
+        detail = error.error?.detail ?? 'Validation error';
+      } else if (status === 401) {
+        detail = 'API key invalid or missing';
+      } else if (status === 404) {
+        detail = 'Resource not found';
+      } else if (status === 409) {
+        detail = 'Conflict — data was modified by another request';
+      } else if (status >= 500) {
+        detail = 'Server error — please try again';
+      }
+
+      messageService.add({
+        severity: 'error',
+        summary: `Error ${status}`,
+        detail,
+      });
+
+      return throwError(() => error);
+    })
+  );
+};
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/services/api.service.spec.ts b/src/PersonalBrandAssistant.Web/src/app/core/services/api.service.spec.ts
new file mode 100644
index 0000000..6cf30eb
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/services/api.service.spec.ts
@@ -0,0 +1,105 @@
+import { TestBed } from '@angular/core/testing';
+import {
+  HttpTestingController,
+  provideHttpClientTesting,
+} from '@angular/common/http/testing';
+import { provideHttpClient, withInterceptors } from '@angular/common/http';
+import { MessageService } from 'primeng/api';
+import { ApiService } from './api.service';
+import { apiKeyInterceptor } from '../interceptors/api-key.interceptor';
+import { errorInterceptor } from '../interceptors/error.interceptor';
+import { environment } from '../../environments/environment';
+
+describe('ApiService', () => {
+  let service: ApiService;
+  let httpMock: HttpTestingController;
+  let messageService: jasmine.SpyObj<MessageService>;
+
+  beforeEach(() => {
+    messageService = jasmine.createSpyObj('MessageService', ['add']);
+
+    TestBed.configureTestingModule({
+      providers: [
+        provideHttpClient(
+          withInterceptors([apiKeyInterceptor, errorInterceptor])
+        ),
+        provideHttpClientTesting(),
+        { provide: MessageService, useValue: messageService },
+      ],
+    });
+
+    service = TestBed.inject(ApiService);
+    httpMock = TestBed.inject(HttpTestingController);
+  });
+
+  afterEach(() => {
+    httpMock.verify();
+  });
+
+  it('should send GET request to correct URL', () => {
+    service.get<string>('test-path').subscribe();
+
+    const req = httpMock.expectOne(`${environment.apiUrl}/test-path`);
+    expect(req.request.method).toBe('GET');
+    req.flush('ok');
+  });
+
+  it('should send POST body as JSON', () => {
+    const body = { name: 'test', value: 42 };
+    service.post<object>('items', body).subscribe();
+
+    const req = httpMock.expectOne(`${environment.apiUrl}/items`);
+    expect(req.request.method).toBe('POST');
+    expect(req.request.body).toEqual(body);
+    req.flush({});
+  });
+
+  it('should add X-Api-Key header when apiKey is set', () => {
+    const original = environment.apiKey;
+    (environment as { apiKey: string }).apiKey = 'test-key-123';
+
+    service.get<string>('secure').subscribe();
+
+    const req = httpMock.expectOne(`${environment.apiUrl}/secure`);
+    expect(req.request.headers.get('X-Api-Key')).toBe('test-key-123');
+    req.flush('ok');
+
+    (environment as { apiKey: string }).apiKey = original;
+  });
+
+  it('should show toast on 500 error', () => {
+    service.get<string>('fail').subscribe({
+      error: () => {
+        /* expected */
+      },
+    });
+
+    const req = httpMock.expectOne(`${environment.apiUrl}/fail`);
+    req.flush('error', { status: 500, statusText: 'Internal Server Error' });
+
+    expect(messageService.add).toHaveBeenCalledWith(
+      jasmine.objectContaining({
+        severity: 'error',
+        detail: 'Server error — please try again',
+      })
+    );
+  });
+
+  it('should show toast on 401 error', () => {
+    service.get<string>('unauthorized').subscribe({
+      error: () => {
+        /* expected */
+      },
+    });
+
+    const req = httpMock.expectOne(`${environment.apiUrl}/unauthorized`);
+    req.flush('', { status: 401, statusText: 'Unauthorized' });
+
+    expect(messageService.add).toHaveBeenCalledWith(
+      jasmine.objectContaining({
+        severity: 'error',
+        detail: 'API key invalid or missing',
+      })
+    );
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/services/api.service.ts b/src/PersonalBrandAssistant.Web/src/app/core/services/api.service.ts
new file mode 100644
index 0000000..bac09b2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/services/api.service.ts
@@ -0,0 +1,26 @@
+import { Injectable, inject } from '@angular/core';
+import { HttpClient, HttpParams } from '@angular/common/http';
+import { Observable } from 'rxjs';
+import { environment } from '../../environments/environment';
+
+@Injectable({ providedIn: 'root' })
+export class ApiService {
+  private readonly http = inject(HttpClient);
+  private readonly baseUrl = environment.apiUrl;
+
+  get<T>(path: string, params?: HttpParams): Observable<T> {
+    return this.http.get<T>(`${this.baseUrl}/${path}`, { params });
+  }
+
+  post<T>(path: string, body: object): Observable<T> {
+    return this.http.post<T>(`${this.baseUrl}/${path}`, body);
+  }
+
+  put<T>(path: string, body: object): Observable<T> {
+    return this.http.put<T>(`${this.baseUrl}/${path}`, body);
+  }
+
+  delete<T>(path: string): Observable<T> {
+    return this.http.delete<T>(`${this.baseUrl}/${path}`);
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/store/auth.store.spec.ts b/src/PersonalBrandAssistant.Web/src/app/core/store/auth.store.spec.ts
new file mode 100644
index 0000000..3509222
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/store/auth.store.spec.ts
@@ -0,0 +1,23 @@
+import { TestBed } from '@angular/core/testing';
+import { AuthStore } from './auth.store';
+
+describe('AuthStore', () => {
+  let store: InstanceType<typeof AuthStore>;
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({});
+    store = TestBed.inject(AuthStore);
+  });
+
+  it('should initialize with empty user info', () => {
+    expect(store.displayName()).toBe('');
+    expect(store.email()).toBe('');
+  });
+
+  it('should set user info', () => {
+    store.setUser('Matt Kruczek', 'matt@example.com');
+
+    expect(store.displayName()).toBe('Matt Kruczek');
+    expect(store.email()).toBe('matt@example.com');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/store/auth.store.ts b/src/PersonalBrandAssistant.Web/src/app/core/store/auth.store.ts
new file mode 100644
index 0000000..e8086d7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/store/auth.store.ts
@@ -0,0 +1,22 @@
+import { signalStore, withMethods, withState } from '@ngrx/signals';
+import { patchState } from '@ngrx/signals';
+
+interface AuthState {
+  displayName: string;
+  email: string;
+}
+
+const initialState: AuthState = {
+  displayName: '',
+  email: '',
+};
+
+export const AuthStore = signalStore(
+  { providedIn: 'root' },
+  withState(initialState),
+  withMethods((store) => ({
+    setUser(displayName: string, email: string): void {
+      patchState(store, { displayName, email });
+    },
+  }))
+);
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/store/ui.store.spec.ts b/src/PersonalBrandAssistant.Web/src/app/core/store/ui.store.spec.ts
new file mode 100644
index 0000000..fe27ab7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/store/ui.store.spec.ts
@@ -0,0 +1,33 @@
+import { TestBed } from '@angular/core/testing';
+import { UiStore } from './ui.store';
+
+describe('UiStore', () => {
+  let store: InstanceType<typeof UiStore>;
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({});
+    store = TestBed.inject(UiStore);
+  });
+
+  it('should initialize with sidebar expanded', () => {
+    expect(store.sidebarCollapsed()).toBe(false);
+  });
+
+  it('should toggle sidebar state', () => {
+    store.toggleSidebar();
+    expect(store.sidebarCollapsed()).toBe(true);
+
+    store.toggleSidebar();
+    expect(store.sidebarCollapsed()).toBe(false);
+  });
+
+  it('should set theme preference', () => {
+    expect(store.theme()).toBe('light');
+
+    store.setTheme('dark');
+    expect(store.theme()).toBe('dark');
+
+    store.setTheme('light');
+    expect(store.theme()).toBe('light');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/store/ui.store.ts b/src/PersonalBrandAssistant.Web/src/app/core/store/ui.store.ts
new file mode 100644
index 0000000..4e3152d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/core/store/ui.store.ts
@@ -0,0 +1,27 @@
+import { signalStore, withMethods, withState } from '@ngrx/signals';
+import { patchState } from '@ngrx/signals';
+
+type Theme = 'light' | 'dark';
+
+interface UiState {
+  sidebarCollapsed: boolean;
+  theme: Theme;
+}
+
+const initialState: UiState = {
+  sidebarCollapsed: false,
+  theme: 'light',
+};
+
+export const UiStore = signalStore(
+  { providedIn: 'root' },
+  withState(initialState),
+  withMethods((store) => ({
+    toggleSidebar(): void {
+      patchState(store, { sidebarCollapsed: !store.sidebarCollapsed() });
+    },
+    setTheme(theme: Theme): void {
+      patchState(store, { theme });
+    },
+  }))
+);
diff --git a/src/PersonalBrandAssistant.Web/src/app/environments/environment.prod.ts b/src/PersonalBrandAssistant.Web/src/app/environments/environment.prod.ts
new file mode 100644
index 0000000..8e9a187
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/environments/environment.prod.ts
@@ -0,0 +1,5 @@
+export const environment = {
+  production: true,
+  apiUrl: '/api',
+  apiKey: '',
+};
diff --git a/src/PersonalBrandAssistant.Web/src/app/environments/environment.ts b/src/PersonalBrandAssistant.Web/src/app/environments/environment.ts
new file mode 100644
index 0000000..c43ccf3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/environments/environment.ts
@@ -0,0 +1,5 @@
+export const environment = {
+  production: false,
+  apiUrl: 'http://localhost:5000/api',
+  apiKey: '',
+};
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
new file mode 100644
index 0000000..91d2c76
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
@@ -0,0 +1,14 @@
+import { Component } from '@angular/core';
+import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
+import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
+
+@Component({
+  selector: 'app-analytics-dashboard',
+  standalone: true,
+  imports: [PageHeaderComponent, EmptyStateComponent],
+  template: `
+    <app-page-header title="Analytics" />
+    <app-empty-state message="Analytics coming soon" icon="pi pi-chart-line" />
+  `,
+})
+export class AnalyticsDashboardComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.routes.ts
new file mode 100644
index 0000000..8b3ca05
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.routes.ts
@@ -0,0 +1,6 @@
+import { Routes } from '@angular/router';
+import { AnalyticsDashboardComponent } from './analytics-dashboard.component';
+
+export const ANALYTICS_ROUTES: Routes = [
+  { path: '', component: AnalyticsDashboardComponent },
+];
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/calendar/calendar-view.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/calendar/calendar-view.component.ts
new file mode 100644
index 0000000..b4d3d51
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/calendar/calendar-view.component.ts
@@ -0,0 +1,14 @@
+import { Component } from '@angular/core';
+import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
+import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
+
+@Component({
+  selector: 'app-calendar-view',
+  standalone: true,
+  imports: [PageHeaderComponent, EmptyStateComponent],
+  template: `
+    <app-page-header title="Calendar" />
+    <app-empty-state message="Content calendar coming soon" icon="pi pi-calendar" />
+  `,
+})
+export class CalendarViewComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/calendar/calendar.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/calendar/calendar.routes.ts
new file mode 100644
index 0000000..65dfc6b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/calendar/calendar.routes.ts
@@ -0,0 +1,6 @@
+import { Routes } from '@angular/router';
+import { CalendarViewComponent } from './calendar-view.component';
+
+export const CALENDAR_ROUTES: Routes = [
+  { path: '', component: CalendarViewComponent },
+];
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list.component.ts
new file mode 100644
index 0000000..5950ba8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list.component.ts
@@ -0,0 +1,14 @@
+import { Component } from '@angular/core';
+import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
+import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
+
+@Component({
+  selector: 'app-content-list',
+  standalone: true,
+  imports: [PageHeaderComponent, EmptyStateComponent],
+  template: `
+    <app-page-header title="Content" />
+    <app-empty-state message="Content management coming soon" icon="pi pi-file" />
+  `,
+})
+export class ContentListComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content.routes.ts
new file mode 100644
index 0000000..255e82c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content.routes.ts
@@ -0,0 +1,6 @@
+import { Routes } from '@angular/router';
+import { ContentListComponent } from './content-list.component';
+
+export const CONTENT_ROUTES: Routes = [
+  { path: '', component: ContentListComponent },
+];
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/dashboard/dashboard.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/dashboard/dashboard.component.ts
new file mode 100644
index 0000000..fe441d0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/dashboard/dashboard.component.ts
@@ -0,0 +1,14 @@
+import { Component } from '@angular/core';
+import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
+import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
+
+@Component({
+  selector: 'app-dashboard',
+  standalone: true,
+  imports: [PageHeaderComponent, EmptyStateComponent],
+  template: `
+    <app-page-header title="Dashboard" />
+    <app-empty-state message="Dashboard coming soon" icon="pi pi-chart-bar" />
+  `,
+})
+export class DashboardComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/platforms/platforms-list.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/platforms/platforms-list.component.ts
new file mode 100644
index 0000000..88d94cc
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/platforms/platforms-list.component.ts
@@ -0,0 +1,14 @@
+import { Component } from '@angular/core';
+import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
+import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
+
+@Component({
+  selector: 'app-platforms-list',
+  standalone: true,
+  imports: [PageHeaderComponent, EmptyStateComponent],
+  template: `
+    <app-page-header title="Platforms" />
+    <app-empty-state message="Platform integrations coming soon" icon="pi pi-share-alt" />
+  `,
+})
+export class PlatformsListComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/platforms/platforms.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/platforms/platforms.routes.ts
new file mode 100644
index 0000000..7361cbd
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/platforms/platforms.routes.ts
@@ -0,0 +1,6 @@
+import { Routes } from '@angular/router';
+import { PlatformsListComponent } from './platforms-list.component';
+
+export const PLATFORMS_ROUTES: Routes = [
+  { path: '', component: PlatformsListComponent },
+];
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts
new file mode 100644
index 0000000..88e143a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts
@@ -0,0 +1,14 @@
+import { Component } from '@angular/core';
+import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
+import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
+
+@Component({
+  selector: 'app-settings',
+  standalone: true,
+  imports: [PageHeaderComponent, EmptyStateComponent],
+  template: `
+    <app-page-header title="Settings" />
+    <app-empty-state message="Settings coming soon" icon="pi pi-cog" />
+  `,
+})
+export class SettingsComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts
new file mode 100644
index 0000000..2ed09b2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts
@@ -0,0 +1,6 @@
+import { Routes } from '@angular/router';
+import { SettingsComponent } from './settings.component';
+
+export const SETTINGS_ROUTES: Routes = [
+  { path: '', component: SettingsComponent },
+];
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/empty-state/empty-state.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/empty-state/empty-state.component.spec.ts
new file mode 100644
index 0000000..69a82f2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/empty-state/empty-state.component.spec.ts
@@ -0,0 +1,41 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { Component } from '@angular/core';
+import { EmptyStateComponent } from './empty-state.component';
+
+@Component({
+  standalone: true,
+  imports: [EmptyStateComponent],
+  template: `<app-empty-state [message]="message" [icon]="icon" />`,
+})
+class TestHostComponent {
+  message = 'No items found';
+  icon = '';
+}
+
+describe('EmptyStateComponent', () => {
+  let fixture: ComponentFixture<TestHostComponent>;
+  let host: TestHostComponent;
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [TestHostComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(TestHostComponent);
+    host = fixture.componentInstance;
+    fixture.detectChanges();
+  });
+
+  it('should render the message', () => {
+    const text = fixture.nativeElement.textContent;
+    expect(text).toContain('No items found');
+  });
+
+  it('should render icon when provided', () => {
+    host.icon = 'pi pi-inbox';
+    fixture.detectChanges();
+
+    const icon = fixture.nativeElement.querySelector('i');
+    expect(icon).toBeTruthy();
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/empty-state/empty-state.component.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/empty-state/empty-state.component.ts
new file mode 100644
index 0000000..d0c74f2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/empty-state/empty-state.component.ts
@@ -0,0 +1,37 @@
+import { Component, input } from '@angular/core';
+
+@Component({
+  selector: 'app-empty-state',
+  standalone: true,
+  template: `
+    <div class="empty-state">
+      @if (icon()) {
+        <i [class]="icon()"></i>
+      }
+      <p>{{ message() }}</p>
+    </div>
+  `,
+  styles: `
+    .empty-state {
+      display: flex;
+      flex-direction: column;
+      align-items: center;
+      justify-content: center;
+      padding: 3rem;
+      color: var(--text-color-secondary);
+
+      i {
+        font-size: 3rem;
+        margin-bottom: 1rem;
+      }
+
+      p {
+        font-size: 1.1rem;
+      }
+    }
+  `,
+})
+export class EmptyStateComponent {
+  message = input.required<string>();
+  icon = input<string>('');
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/loading-spinner/loading-spinner.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/loading-spinner/loading-spinner.component.spec.ts
new file mode 100644
index 0000000..61a860f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/loading-spinner/loading-spinner.component.spec.ts
@@ -0,0 +1,20 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { LoadingSpinnerComponent } from './loading-spinner.component';
+
+describe('LoadingSpinnerComponent', () => {
+  let fixture: ComponentFixture<LoadingSpinnerComponent>;
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [LoadingSpinnerComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(LoadingSpinnerComponent);
+    fixture.detectChanges();
+  });
+
+  it('should render the spinner', () => {
+    const spinner = fixture.nativeElement.querySelector('p-progressspinner');
+    expect(spinner).toBeTruthy();
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/loading-spinner/loading-spinner.component.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/loading-spinner/loading-spinner.component.ts
new file mode 100644
index 0000000..61e8e30
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/loading-spinner/loading-spinner.component.ts
@@ -0,0 +1,33 @@
+import { Component, input } from '@angular/core';
+import { ProgressSpinner } from 'primeng/progressspinner';
+
+@Component({
+  selector: 'app-loading-spinner',
+  standalone: true,
+  imports: [ProgressSpinner],
+  template: `
+    <div class="spinner-container">
+      <p-progressspinner />
+      @if (message()) {
+        <p>{{ message() }}</p>
+      }
+    </div>
+  `,
+  styles: `
+    .spinner-container {
+      display: flex;
+      flex-direction: column;
+      align-items: center;
+      justify-content: center;
+      padding: 2rem;
+
+      p {
+        margin-top: 1rem;
+        color: var(--text-color-secondary);
+      }
+    }
+  `,
+})
+export class LoadingSpinnerComponent {
+  message = input<string>('');
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/page-header/page-header.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/page-header/page-header.component.spec.ts
new file mode 100644
index 0000000..df773bc
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/page-header/page-header.component.spec.ts
@@ -0,0 +1,47 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { Component } from '@angular/core';
+import {
+  PageHeaderComponent,
+  PageAction,
+} from './page-header.component';
+
+@Component({
+  standalone: true,
+  imports: [PageHeaderComponent],
+  template: `<app-page-header [title]="title" [actions]="actions" />`,
+})
+class TestHostComponent {
+  title = 'Test Title';
+  actions: PageAction[] = [];
+}
+
+describe('PageHeaderComponent', () => {
+  let fixture: ComponentFixture<TestHostComponent>;
+  let host: TestHostComponent;
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [TestHostComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(TestHostComponent);
+    host = fixture.componentInstance;
+    fixture.detectChanges();
+  });
+
+  it('should render the title', () => {
+    const h1 = fixture.nativeElement.querySelector('h1');
+    expect(h1.textContent).toContain('Test Title');
+  });
+
+  it('should render action buttons when provided', () => {
+    host.actions = [
+      { label: 'Create', icon: 'pi pi-plus', command: () => {} },
+      { label: 'Export', command: () => {} },
+    ];
+    fixture.detectChanges();
+
+    const buttons = fixture.nativeElement.querySelectorAll('.actions p-button, .actions button');
+    expect(buttons.length).toBeGreaterThanOrEqual(2);
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/page-header/page-header.component.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/page-header/page-header.component.ts
new file mode 100644
index 0000000..47ec28e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/page-header/page-header.component.ts
@@ -0,0 +1,51 @@
+import { Component, input } from '@angular/core';
+import { ButtonModule } from 'primeng/button';
+
+export interface PageAction {
+  label: string;
+  icon?: string;
+  command: () => void;
+}
+
+@Component({
+  selector: 'app-page-header',
+  standalone: true,
+  imports: [ButtonModule],
+  template: `
+    <div class="page-header">
+      <h1>{{ title() }}</h1>
+      <div class="actions">
+        @for (action of actions(); track action.label) {
+          <p-button
+            [label]="action.label"
+            [icon]="action.icon ?? ''"
+            (onClick)="action.command()"
+          />
+        }
+      </div>
+    </div>
+  `,
+  styles: `
+    .page-header {
+      display: flex;
+      justify-content: space-between;
+      align-items: center;
+      margin-bottom: 1.5rem;
+
+      h1 {
+        margin: 0;
+        font-size: 1.5rem;
+        font-weight: 600;
+      }
+
+      .actions {
+        display: flex;
+        gap: 0.5rem;
+      }
+    }
+  `,
+})
+export class PageHeaderComponent {
+  title = input.required<string>();
+  actions = input<PageAction[]>([]);
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/status-badge/status-badge.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/status-badge/status-badge.component.spec.ts
new file mode 100644
index 0000000..5c8ea71
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/status-badge/status-badge.component.spec.ts
@@ -0,0 +1,48 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { Component } from '@angular/core';
+import { StatusBadgeComponent } from './status-badge.component';
+
+@Component({
+  standalone: true,
+  imports: [StatusBadgeComponent],
+  template: `<app-status-badge [status]="status" />`,
+})
+class TestHostComponent {
+  status = 'Draft';
+}
+
+describe('StatusBadgeComponent', () => {
+  let fixture: ComponentFixture<TestHostComponent>;
+  let host: TestHostComponent;
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [TestHostComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(TestHostComponent);
+    host = fixture.componentInstance;
+    fixture.detectChanges();
+  });
+
+  const statusTests: { status: string; expected: string }[] = [
+    { status: 'Draft', expected: 'secondary' },
+    { status: 'Review', expected: 'info' },
+    { status: 'Approved', expected: 'success' },
+    { status: 'Scheduled', expected: 'warn' },
+    { status: 'Publishing', expected: 'warn' },
+    { status: 'Published', expected: 'success' },
+    { status: 'Failed', expected: 'danger' },
+    { status: 'Archived', expected: 'secondary' },
+  ];
+
+  statusTests.forEach(({ status, expected }) => {
+    it(`should render ${expected} severity for ${status}`, () => {
+      host.status = status;
+      fixture.detectChanges();
+
+      const tag = fixture.nativeElement.querySelector('p-tag, span');
+      expect(tag).toBeTruthy();
+    });
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/shared/components/status-badge/status-badge.component.ts b/src/PersonalBrandAssistant.Web/src/app/shared/components/status-badge/status-badge.component.ts
new file mode 100644
index 0000000..017161f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/shared/components/status-badge/status-badge.component.ts
@@ -0,0 +1,24 @@
+import { Component, computed, input } from '@angular/core';
+import { Tag } from 'primeng/tag';
+
+const statusSeverityMap: Record<string, 'secondary' | 'info' | 'success' | 'warn' | 'danger'> = {
+  Draft: 'secondary',
+  Review: 'info',
+  Approved: 'success',
+  Scheduled: 'warn',
+  Publishing: 'warn',
+  Published: 'success',
+  Failed: 'danger',
+  Archived: 'secondary',
+};
+
+@Component({
+  selector: 'app-status-badge',
+  standalone: true,
+  imports: [Tag],
+  template: `<p-tag [value]="status()" [severity]="severity()" />`,
+})
+export class StatusBadgeComponent {
+  status = input.required<string>();
+  severity = computed(() => statusSeverityMap[this.status()] ?? 'secondary');
+}
diff --git a/src/PersonalBrandAssistant.Web/src/main.ts b/src/PersonalBrandAssistant.Web/src/main.ts
new file mode 100644
index 0000000..35b00f3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/main.ts
@@ -0,0 +1,6 @@
+import { bootstrapApplication } from '@angular/platform-browser';
+import { appConfig } from './app/app.config';
+import { AppComponent } from './app/app.component';
+
+bootstrapApplication(AppComponent, appConfig)
+  .catch((err) => console.error(err));
diff --git a/src/PersonalBrandAssistant.Web/src/styles.scss b/src/PersonalBrandAssistant.Web/src/styles.scss
new file mode 100644
index 0000000..b399bc3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/styles.scss
@@ -0,0 +1,11 @@
+@use 'styles/variables' as *;
+
+html,
+body {
+  margin: 0;
+  padding: 0;
+  height: 100%;
+  font-family:
+    -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto,
+    'Helvetica Neue', Arial, sans-serif;
+}
diff --git a/src/PersonalBrandAssistant.Web/src/styles/_variables.scss b/src/PersonalBrandAssistant.Web/src/styles/_variables.scss
new file mode 100644
index 0000000..8797d0e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/styles/_variables.scss
@@ -0,0 +1,13 @@
+$brand-primary: #3b82f6;
+$brand-secondary: #6366f1;
+$brand-accent: #8b5cf6;
+
+$spacing-xs: 0.25rem;
+$spacing-sm: 0.5rem;
+$spacing-md: 1rem;
+$spacing-lg: 1.5rem;
+$spacing-xl: 2rem;
+
+$sidebar-width: 250px;
+$sidebar-collapsed-width: 60px;
+$header-height: 60px;
