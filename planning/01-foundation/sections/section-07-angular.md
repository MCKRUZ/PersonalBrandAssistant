# Section 07 — Angular Foundation

## Overview

This section creates the Angular 19 frontend shell for the Personal Brand Assistant. It establishes the project structure, installs PrimeNG for UI components, builds the app shell layout (sidebar, header, main content area), sets up lazy-loaded routing, creates shared reusable components, configures NgRx signals store for global state, and implements the API service layer with HTTP interceptors for API key injection and error handling.

**Dependencies:** Section 01 (scaffolding) must be complete so the solution structure exists. This section can run in parallel with sections 02 through 05 (backend layers).

**Blocks:** Section 09 (verification) depends on this section being complete.

---

## Project Location

All Angular code lives under:

```
PersonalBrandAssistant/src/PersonalBrandAssistant.Web/
```

This is the Angular 19 workspace root. It sits alongside the .NET projects in the `src/` directory.

---

## Tests First

All Angular tests use Jasmine/Karma. Tests should be written before or alongside each component/service. Run tests with `ng test --watch=false --browsers=ChromeHeadless` from the Web project directory.

### API Service Tests

File: `src/app/core/services/api.service.spec.ts`

- **GET request sends correct URL with base from environment** -- Issue a GET through ApiService, verify `HttpTestingController` receives a request to `${environment.apiUrl}/some-path`. Call `afterEach(() => httpMock.verify())`.
- **POST request sends body as JSON** -- Issue a POST with an object body, verify the request body matches and content-type is JSON.
- **HTTP interceptor adds X-Api-Key header to all requests** -- Make any HTTP request through ApiService, verify the outgoing request has the `X-Api-Key` header set to the value from environment config.
- **HTTP error interceptor shows toast on 4xx/5xx responses** -- Flush a 400 or 500 response, verify that the PrimeNG `MessageService` (or equivalent) is called with an error message.
- **401 response handled** -- Flush a 401 response, verify appropriate handling (error toast with "API key invalid" or similar message).

### Shared Component Tests

File: `src/app/shared/components/page-header/page-header.component.spec.ts`

- **PageHeaderComponent renders title** -- Set the `title` input, verify it appears in the rendered template.
- **PageHeaderComponent renders action buttons when provided** -- Provide action button configuration via input, verify buttons are rendered in the DOM.

File: `src/app/shared/components/status-badge/status-badge.component.spec.ts`

- **StatusBadgeComponent renders correct color for each ContentStatus** -- For each status value (Draft=gray, Review=blue, Approved=teal, Scheduled=orange, Publishing=yellow, Published=green, Failed=red, Archived=gray), verify the correct CSS class or PrimeNG severity is applied.

File: `src/app/shared/components/loading-spinner/loading-spinner.component.spec.ts`

- **LoadingSpinnerComponent shows spinner** -- Verify the PrimeNG ProgressSpinner element is present in the DOM.

File: `src/app/shared/components/empty-state/empty-state.component.spec.ts`

- **EmptyStateComponent renders message** -- Set the `message` input, verify the text appears in the rendered template.

### Routing Tests

File: `src/app/app.routes.spec.ts`

- **Default route redirects to /dashboard** -- Navigate to `/`, verify the router redirects to `/dashboard`.
- **Lazy routes load correctly** -- Navigate to `/content`, `/calendar`, `/analytics`, `/platforms`, `/settings` and verify each route resolves without error (the loaded component renders).

### NgRx Store Tests

File: `src/app/core/store/ui.store.spec.ts`

- **ui store toggles sidebar state** -- Call the toggle method, verify `sidebarCollapsed` signal flips from false to true and back.
- **ui store persists theme preference** -- Set a theme value, verify the signal reflects the new theme.

File: `src/app/core/store/auth.store.spec.ts`

- **auth store holds user info** -- Set user info (displayName, email), verify the signals reflect the values.

---

## Implementation Details

### 1. Angular Project Creation

Create the Angular 19 project inside `src/PersonalBrandAssistant.Web/`:

```bash
ng new PersonalBrandAssistant.Web --directory src/PersonalBrandAssistant.Web --style scss --ssr false --routing
```

Key creation options:
- **Standalone components** (default in Angular 19, no NgModules)
- **SCSS** for styling
- **SSR disabled** (SPA only, served by nginx in production)
- **Routing enabled**

### 2. PrimeNG Setup

Install dependencies:
- `primeng` (v19.x)
- `primeicons`
- `primeflex`

Configuration steps:
- Import a PrimeNG theme (Lara or Aura) in `styles.scss`
- Import PrimeIcons CSS in `styles.scss`
- Import PrimeFlex CSS in `styles.scss`
- Create `src/app/styles/_variables.scss` with brand color tokens and spacing variables
- Provide `PrimeNG` and `MessageService` in the app config (`app.config.ts`)

### 3. Environment Configuration

File: `src/app/environments/environment.ts`

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000/api',
  apiKey: '' // loaded from local config or dev default
};
```

File: `src/app/environments/environment.prod.ts`

```typescript
export const environment = {
  production: true,
  apiUrl: '/api', // same-origin in production (nginx proxy)
  apiKey: '' // injected at build time or runtime
};
```

### 4. App Shell Layout

File: `src/app/app.component.ts`

The root component provides the application shell with three regions:
- **Sidebar** -- A collapsible navigation panel using PrimeNG `Sidebar` or a custom component. Contains navigation links for: Dashboard, Content, Calendar, Analytics, Platforms, Settings. Each item uses `routerLink` with `routerLinkActive` for highlighting.
- **Header** -- A top bar (`div` or PrimeNG `Toolbar`) displaying the app name ("Personal Brand Assistant"), a sidebar toggle button, and placeholder slots for user info and notification bell.
- **Main content area** -- A `<router-outlet>` that renders the active route's component.

The sidebar collapsed/expanded state is managed by the NgRx UI store (see below).

### 5. Routing Configuration

File: `src/app/app.routes.ts`

Define routes with lazy loading for all feature areas:

```typescript
export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
  { path: 'content', loadChildren: () => import('./features/content/content.routes').then(m => m.CONTENT_ROUTES) },
  { path: 'calendar', loadChildren: () => import('./features/calendar/calendar.routes').then(m => m.CALENDAR_ROUTES) },
  { path: 'analytics', loadChildren: () => import('./features/analytics/analytics.routes').then(m => m.ANALYTICS_ROUTES) },
  { path: 'platforms', loadChildren: () => import('./features/platforms/platforms.routes').then(m => m.PLATFORMS_ROUTES) },
  { path: 'settings', loadChildren: () => import('./features/settings/settings.routes').then(m => m.SETTINGS_ROUTES) },
];
```

Each feature folder (e.g., `src/app/features/content/`) contains:
- A route definition file (e.g., `content.routes.ts`) exporting a `Routes` array
- A placeholder component that renders the `EmptyStateComponent` with a "Coming soon" message

The `DashboardComponent` is eager-loaded as the default route. All other features are lazy-loaded.

### 6. Shared Components

All shared components live in `src/app/shared/components/`.

#### PageHeaderComponent

File: `src/app/shared/components/page-header/page-header.component.ts`

Inputs:
- `title: string` -- Page title text
- `actions: { label: string; icon?: string; command: () => void }[]` -- Optional array of action button definitions

Renders an `h1` with the title and a row of PrimeNG `Button` components for each action. Uses `@for` for button iteration.

#### StatusBadgeComponent

File: `src/app/shared/components/status-badge/status-badge.component.ts`

Input:
- `status: string` -- A ContentStatus value

Renders a PrimeNG `Tag` component with severity/color mapped by status:

| Status | Severity/Color |
|--------|---------------|
| Draft | secondary (gray) |
| Review | info (blue) |
| Approved | success (teal) |
| Scheduled | warning (orange) |
| Publishing | warning (yellow) |
| Published | success (green) |
| Failed | danger (red) |
| Archived | secondary (gray) |

#### LoadingSpinnerComponent

File: `src/app/shared/components/loading-spinner/loading-spinner.component.ts`

A thin wrapper around PrimeNG `ProgressSpinner`. No inputs required (uses default size). Can optionally accept a `message` input for text below the spinner.

#### EmptyStateComponent

File: `src/app/shared/components/empty-state/empty-state.component.ts`

Inputs:
- `message: string` -- Text to display (e.g., "No content yet" or "Coming soon")
- `icon?: string` -- Optional PrimeIcon class name

Renders a centered container with the icon (if provided) and message text.

### 7. NgRx Signals Store

Install `@ngrx/signals` (v19.x).

#### UI Store

File: `src/app/core/store/ui.store.ts`

Uses `signalStore` from `@ngrx/signals`:

State shape:
- `sidebarCollapsed: boolean` (default: `false`)
- `theme: 'light' | 'dark'` (default: `'light'`)

Methods:
- `toggleSidebar()` -- Flips `sidebarCollapsed`
- `setTheme(theme: 'light' | 'dark')` -- Sets the theme preference

The store should be provided at the application root level in `app.config.ts`.

#### Auth Store

File: `src/app/core/store/auth.store.ts`

State shape:
- `displayName: string` (default: `''`)
- `email: string` (default: `''`)

Methods:
- `setUser(displayName: string, email: string)` -- Updates user info

This is a placeholder for future auth integration. Feature stores (content, calendar, etc.) will be added in split 06.

### 8. API Service

File: `src/app/core/services/api.service.ts`

A central service wrapping `HttpClient` with generic methods:

```typescript
/** Central API communication service. All backend calls go through this. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  get<T>(path: string, params?: HttpParams): Observable<T> { /* ... */ }
  post<T>(path: string, body: object): Observable<T> { /* ... */ }
  put<T>(path: string, body: object): Observable<T> { /* ... */ }
  delete<T>(path: string): Observable<T> { /* ... */ }
}
```

Each method prepends `environment.apiUrl` to the path.

### 9. HTTP Interceptors

Angular 19 uses functional interceptors registered via `provideHttpClient(withInterceptors([...]))` in `app.config.ts`.

#### API Key Interceptor

File: `src/app/core/interceptors/api-key.interceptor.ts`

A functional interceptor that clones every outgoing request and adds the `X-Api-Key` header with the value from `environment.apiKey`.

```typescript
export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  /** Clone request, add X-Api-Key header from environment config */
};
```

#### Error Interceptor

File: `src/app/core/interceptors/error.interceptor.ts`

A functional interceptor that catches HTTP error responses in the `pipe` and displays a PrimeNG toast via `MessageService`:
- 400 errors: Show validation error message from ProblemDetails response body
- 401 errors: Show "API key invalid or missing"
- 404 errors: Show "Resource not found"
- 409 errors: Show "Conflict — data was modified by another request"
- 500 errors: Show "Server error — please try again"

The interceptor should re-throw the error so calling code can also handle it if needed.

### 10. App Configuration

File: `src/app/app.config.ts`

Brings everything together:

```typescript
export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([apiKeyInterceptor, errorInterceptor])),
    provideAnimations(),
    MessageService, // PrimeNG toast service
    // NgRx signal stores provided here or at component level
  ]
};
```

---

## File Structure Summary

```
src/PersonalBrandAssistant.Web/
├── src/
│   ├── app/
│   │   ├── app.component.ts          # Shell layout (sidebar, header, router-outlet)
│   │   ├── app.component.scss
│   │   ├── app.config.ts             # Application providers
│   │   ├── app.routes.ts             # Top-level routes
│   │   ├── app.routes.spec.ts        # Routing tests
│   │   ├── core/
│   │   │   ├── interceptors/
│   │   │   │   ├── api-key.interceptor.ts
│   │   │   │   └── error.interceptor.ts
│   │   │   ├── services/
│   │   │   │   ├── api.service.ts
│   │   │   │   └── api.service.spec.ts
│   │   │   └── store/
│   │   │       ├── ui.store.ts
│   │   │       ├── ui.store.spec.ts
│   │   │       ├── auth.store.ts
│   │   │       └── auth.store.spec.ts
│   │   ├── features/
│   │   │   ├── dashboard/
│   │   │   │   └── dashboard.component.ts
│   │   │   ├── content/
│   │   │   │   └── content.routes.ts
│   │   │   ├── calendar/
│   │   │   │   └── calendar.routes.ts
│   │   │   ├── analytics/
│   │   │   │   └── analytics.routes.ts
│   │   │   ├── platforms/
│   │   │   │   └── platforms.routes.ts
│   │   │   └── settings/
│   │   │       └── settings.routes.ts
│   │   ├── shared/
│   │   │   └── components/
│   │   │       ├── page-header/
│   │   │       │   ├── page-header.component.ts
│   │   │       │   └── page-header.component.spec.ts
│   │   │       ├── status-badge/
│   │   │       │   ├── status-badge.component.ts
│   │   │       │   └── status-badge.component.spec.ts
│   │   │       ├── loading-spinner/
│   │   │       │   ├── loading-spinner.component.ts
│   │   │       │   └── loading-spinner.component.spec.ts
│   │   │       └── empty-state/
│   │   │           ├── empty-state.component.ts
│   │   │           └── empty-state.component.spec.ts
│   │   └── environments/
│   │       ├── environment.ts
│   │       └── environment.prod.ts
│   ├── styles/
│   │   └── _variables.scss
│   └── styles.scss                    # Global styles, PrimeNG imports
├── angular.json
├── package.json
└── tsconfig.json
```

---

## Implementation Checklist

1. Create Angular 19 project with standalone components, SCSS, no SSR, routing enabled
2. Install PrimeNG, PrimeIcons, PrimeFlex, and `@ngrx/signals`
3. Configure PrimeNG theme and global styles in `styles.scss`
4. Create environment files with `apiUrl` and `apiKey`
5. Create the API key interceptor (adds `X-Api-Key` header)
6. Create the error interceptor (catches HTTP errors, shows PrimeNG toast)
7. Create `ApiService` with generic GET/POST/PUT/DELETE methods
8. Write API service tests (verify URL construction, headers, error handling)
9. Create NgRx UI store (sidebar state, theme preference)
10. Create NgRx auth store (user info placeholder)
11. Write store tests (toggle sidebar, set theme, set user)
12. Create shared components: PageHeader, StatusBadge, LoadingSpinner, EmptyState
13. Write shared component tests
14. Create app shell layout (sidebar with nav links, header bar, router-outlet)
15. Configure lazy-loaded routes in `app.routes.ts`
16. Create placeholder components/routes for each feature area
17. Write routing tests (default redirect, lazy load verification)
18. Wire everything together in `app.config.ts`
19. Verify all tests pass: `ng test --watch=false --browsers=ChromeHeadless`