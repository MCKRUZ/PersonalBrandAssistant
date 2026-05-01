# Section 08: Approval Queue

## Overview

The Approval Queue is a feature-scoped Angular page at `/approval-queue` that displays all content items in `Review` status, allowing the operator to approve, reject, or batch-approve items before they are published. It consists of three new files (component, store, API service) plus test files, all under `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/`.

## Dependencies

- **Section 01 (Backend Extensions)**: The backend approval endpoints already exist (`GET /api/approval/pending`, `POST /api/approval/{id}/approve`, `POST /api/approval/{id}/reject`, `POST /api/approval/batch-approve`). Section 01 may extend these but the current surface is sufficient for this section.
- **Section 03 (Design System)**: Shared atom components (`StatusBadgeComponent`, `ScoreGaugeComponent`, `AxisBarComponent`) must be available for rendering brand scores and status indicators within cards.
- **Section 04 (App Shell)**: The route `/approval-queue` must be registered in `app.routes.ts` with `loadComponent` pointing to `ApprovalQueueComponent`. The sidebar must include the Approval Queue nav item under the "Distribute" group.

This section does not block any other section.

## Existing Backend API Surface

The approval endpoints are already implemented in `src/PersonalBrandAssistant.Api/Endpoints/ApprovalEndpoints.cs`:

| Method | Endpoint | Request Body | Response |
|--------|----------|-------------|----------|
| `GET` | `/api/approval/pending?pageSize=20` | - | `Content[]` |
| `POST` | `/api/approval/{id}/approve` | - | `Result<Unit>` |
| `POST` | `/api/approval/{id}/reject` | `{ feedback: string }` | `Result<Unit>` |
| `POST` | `/api/approval/batch-approve` | `{ contentIds: Guid[] }` | `Result<int>` (success count) |

The `IApprovalService` implementation (`src/PersonalBrandAssistant.Infrastructure/Services/ApprovalService.cs`) handles workflow transitions via `IWorkflowEngine`, chains to `Scheduled` status when `ScheduledAt` is set, and sends rejection notifications.

## Existing Data Models

The `Content` interface is already defined in `src/PersonalBrandAssistant.Web/src/app/shared/models/content.model.ts` with fields including `id`, `title`, `body`, `status`, `targetPlatforms`, `createdAt`, `version`, etc.

The `BrandVoiceScore` interface is in `src/PersonalBrandAssistant.Web/src/app/shared/models/workflow.model.ts`. Section 01 will update this to a 4-axis model (Authoritative, Pragmatic, Concise, Practitioner). This section should use the updated model when available but can fall back to the current 3-axis model if section 01 is not yet complete.

Platform types and content statuses are defined in `src/PersonalBrandAssistant.Web/src/app/shared/models/enums.ts`.

---

## Tests First

All tests use Jasmine/Karma with Angular TestBed. Write these before implementation.

### File: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-api.service.spec.ts`

```typescript
// Test: ApprovalApiService.getPending calls GET /api/approval/pending with pageSize param
// Test: ApprovalApiService.approve calls POST /api/approval/{id}/approve
// Test: ApprovalApiService.reject calls POST /api/approval/{id}/reject with { feedback } body
// Test: ApprovalApiService.batchApprove calls POST /api/approval/batch-approve with { contentIds } body
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval.store.spec.ts`

```typescript
// Test: ApprovalStore loads pending items from ApprovalApiService.getPending on init
// Test: ApprovalStore.approve calls ApprovalApiService.approve and removes item from list on success
// Test: ApprovalStore.reject calls ApprovalApiService.reject with feedback body and removes item from list
// Test: ApprovalStore.batchApprove calls ApprovalApiService.batchApprove with selected IDs and removes them from list
// Test: ApprovalStore.filterByPlatform filters items client-side by targetPlatforms
// Test: ApprovalStore.filterByPlatform with null/undefined shows all items
// Test: ApprovalStore.toggleSelection adds ID to selectedIds when not present
// Test: ApprovalStore.toggleSelection removes ID from selectedIds when already present
// Test: ApprovalStore.selectAll adds all visible (filtered) item IDs to selectedIds
// Test: ApprovalStore.clearSelection empties selectedIds
// Test: ApprovalStore computed signal filteredItems returns items matching platformFilter
// Test: ApprovalStore computed signal hasSelection returns true when selectedIds is non-empty
// Test: ApprovalStore sets isLoading=true during API calls and false on completion
// Test: ApprovalStore sets error signal on API failure
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.spec.ts`

```typescript
// Test: ApprovalQueueComponent renders expandable cards for each pending item
// Test: Collapsed card shows title, platform icon, brand score badge, created date
// Test: Collapsed card shows quick action buttons (approve, reject)
// Test: Expanded card shows full content preview (body text)
// Test: Expanded card shows brand score axes (4 AxisBar components or 3 if pre-section-01)
// Test: Expanded card shows reject feedback textarea
// Test: Clicking approve button calls store.approve with item ID
// Test: Clicking reject button with feedback calls store.reject with item ID and feedback text
// Test: Bulk action bar appears when items are selected (store.hasSelection is true)
// Test: Bulk action bar hidden when no items selected
// Test: "Approve Selected" button calls store.batchApprove with selectedIds
// Test: Platform filter chips render for all platform types
// Test: Clicking a platform chip calls store.filterByPlatform
// Test: Active platform filter chip has terracotta highlight
// Test: "All" chip clears the platform filter
// Test: Empty state message shown when no pending items exist
// Test: Loading skeleton shown when store.isLoading is true
```

---

## Implementation

### File 1: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-api.service.ts`

A dedicated API service for approval operations. Follows the same pattern as the existing `ContentService` (injects `ApiService`, returns `Observable<T>`).

```typescript
@Injectable({ providedIn: 'root' })
export class ApprovalApiService {
  private readonly api = inject(ApiService);

  /** GET /api/approval/pending?pageSize={pageSize} */
  getPending(pageSize = 50): Observable<Content[]> { ... }

  /** POST /api/approval/{id}/approve */
  approve(id: string): Observable<void> { ... }

  /** POST /api/approval/{id}/reject with { feedback } body */
  reject(id: string, feedback: string): Observable<void> { ... }

  /** POST /api/approval/batch-approve with { contentIds } body */
  batchApprove(contentIds: readonly string[]): Observable<{ successCount: number }> { ... }
}
```

Note: The existing `ContentService` already has `getPendingApproval`, `approve`, and `reject` methods. The new `ApprovalApiService` is a focused extraction that includes `batchApprove` and keeps the approval queue page self-contained. The implementer can choose to either create this dedicated service or reuse `ContentService` and add `batchApprove` to it -- either approach works as long as the store has the methods it needs.

### File 2: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval.store.ts`

NgRx SignalStore, feature-scoped (provided at the component level, not root). State is released when the user navigates away from the approval queue.

**State shape:**

```typescript
interface ApprovalState {
  items: Content[];          // all pending items from API
  selectedIds: string[];     // IDs selected for bulk operations
  platformFilter: PlatformType | null;  // null = show all
  isLoading: boolean;
  error: string | null;
}
```

**Initial state:** `{ items: [], selectedIds: [], platformFilter: null, isLoading: false, error: null }`

**Computed signals:**
- `filteredItems`: Derives from `items` and `platformFilter`. When `platformFilter` is null, returns all items. Otherwise filters to items where `targetPlatforms` includes the selected platform.
- `hasSelection`: `selectedIds.length > 0`
- `selectedCount`: `selectedIds.length`
- `pendingCount`: `items.length`

**Methods (via `withMethods`):**
- `loadPending()`: Sets `isLoading`, calls `ApprovalApiService.getPending()`, patches `items` on success, clears `selectedIds` and `platformFilter`.
- `approve(id: string)`: Calls `ApprovalApiService.approve(id)`. On success, removes the item from `items` and from `selectedIds` if present. Shows success toast via PrimeNG `MessageService`.
- `reject(id: string, feedback: string)`: Calls `ApprovalApiService.reject(id, feedback)`. On success, removes the item from `items`. Shows info toast.
- `batchApprove(ids: string[])`: Calls `ApprovalApiService.batchApprove(ids)`. On success, removes all approved IDs from `items` and clears `selectedIds`. Shows toast with count.
- `filterByPlatform(platform: PlatformType | null)`: Patches `platformFilter`. Client-side only, no API call.
- `toggleSelection(id: string)`: Adds or removes `id` from `selectedIds`.
- `selectAll()`: Sets `selectedIds` to all IDs in `filteredItems`.
- `clearSelection()`: Sets `selectedIds` to `[]`.

**Lifecycle:** Use `withHooks({ onInit })` or an `effect` to call `loadPending()` when the store initializes (which happens when the component mounts since the store is feature-scoped).

### File 3: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.ts`

Standalone component with inline or external template. Provides `ApprovalStore` at the component level.

**Template structure:**

```
<div class="approval-queue-page">
  <!-- Page header -->
  <h1>Approval Queue</h1>
  <span class="pending-count">{{ store.pendingCount() }} pending</span>

  <!-- Platform filter chips -->
  <div class="filter-bar">
    <p-chip label="All" [styleClass]="..." (click)="store.filterByPlatform(null)" />
    @for (platform of platforms; track platform) {
      <p-chip [label]="platform" [styleClass]="..." (click)="store.filterByPlatform(platform)" />
    }
  </div>

  <!-- Loading skeleton -->
  @if (store.isLoading()) {
    <p-skeleton ... />
  }

  <!-- Empty state -->
  @if (!store.isLoading() && store.filteredItems().length === 0) {
    <div class="empty-state">No items pending review</div>
  }

  <!-- Card list -->
  @for (item of store.filteredItems(); track item.id) {
    <div class="approval-card" [class.expanded]="expandedId() === item.id">
      <!-- Collapsed view -->
      <div class="card-header" (click)="toggleExpand(item.id)">
        <span class="title">{{ item.title || 'Untitled' }}</span>
        <app-status-badge [status]="item.status" />
        <span class="platform-icons">...</span>
        <span class="score-badge">{{ item.brandScore?.overallScore ?? '—' }}</span>
        <span class="date">{{ item.createdAt | date:'short' }}</span>
        <div class="quick-actions">
          <p-button icon="pi pi-check" severity="success" (click)="onApprove(item.id, $event)" />
          <p-button icon="pi pi-times" severity="danger" (click)="onRejectOpen(item.id, $event)" />
          <p-checkbox [binary]="true" [ngModel]="isSelected(item.id)" (onChange)="store.toggleSelection(item.id)" />
        </div>
      </div>

      <!-- Expanded view -->
      @if (expandedId() === item.id) {
        <div class="card-body">
          <div class="preview">{{ item.body }}</div>
          <div class="score-axes">
            <!-- AxisBar components for each scoring axis -->
          </div>
          <div class="reject-form">
            <textarea [(ngModel)]="rejectFeedback" placeholder="Rejection feedback..."></textarea>
            <p-button label="Reject with Feedback" (click)="onReject(item.id)" />
          </div>
        </div>
      }
    </div>
  }

  <!-- Bulk action bar -->
  @if (store.hasSelection()) {
    <div class="bulk-action-bar">
      <span>{{ store.selectedCount() }} selected</span>
      <p-button label="Approve Selected" (click)="onBatchApprove()" />
      <p-button label="Select All" (click)="store.selectAll()" />
      <p-button label="Clear" (click)="store.clearSelection()" />
    </div>
  }
</div>
```

**Component class:**

```typescript
@Component({
  selector: 'app-approval-queue',
  standalone: true,
  imports: [/* PrimeNG modules, shared atoms, FormsModule, DatePipe */],
  providers: [ApprovalStore],
  templateUrl: './approval-queue.component.html',
  styleUrl: './approval-queue.component.scss'
})
export class ApprovalQueueComponent {
  protected readonly store = inject(ApprovalStore);

  /** All platform types for filter chips */
  protected readonly platforms: PlatformType[] = ['TwitterX', 'LinkedIn', 'Instagram', 'YouTube', 'Reddit', 'PersonalBlog', 'Substack'];

  /** Currently expanded card ID (single-expand accordion pattern) */
  protected readonly expandedId = signal<string | null>(null);

  /** Feedback text for rejection (bound to textarea in expanded card) */
  protected rejectFeedback = '';

  protected toggleExpand(id: string): void { ... }
  protected isSelected(id: string): boolean { ... }
  protected onApprove(id: string, event: Event): void { ... }
  protected onRejectOpen(id: string, event: Event): void { ... }
  protected onReject(id: string): void { ... }
  protected onBatchApprove(): void { ... }
}
```

**Key behaviors:**
- `toggleExpand`: Sets `expandedId` to the clicked ID, or null if already expanded (accordion toggle). Clears `rejectFeedback` on collapse.
- `onApprove`: Calls `event.stopPropagation()` (prevents card expand), then `store.approve(id)`.
- `onRejectOpen`: Calls `event.stopPropagation()`, expands the card, focuses the feedback textarea.
- `onReject`: Calls `store.reject(id, this.rejectFeedback)` then clears feedback and collapses.
- `onBatchApprove`: Calls `store.batchApprove(store.selectedIds())`.
- `isSelected`: Returns `store.selectedIds().includes(id)`.

### File 4: `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.scss`

Styling for the approval queue page. Key considerations:

- Card list uses flexbox column layout with gap
- Collapsed cards: single-row horizontal layout with title taking flex-grow, actions pinned right
- Expanded cards: adds body section below header with preview and score axes
- Bulk action bar: fixed to bottom of page (sticky), dark surface with terracotta accent buttons
- Platform filter chips: horizontal row with active chip highlighted using `var(--primary-color)` (terracotta)
- Empty state: centered text with muted color
- Score badge: Uses `ScoreGaugeComponent` or a simple colored badge (green >= 80, yellow >= 60, red < 60)
- Responsive: cards stack naturally; bulk bar remains sticky

### Route Registration (Section 04 responsibility, referenced here)

In `app.routes.ts`, the following route must exist:

```typescript
{
  path: 'approval-queue',
  loadComponent: () => import('./pages/approval-queue/approval-queue.component').then(m => m.ApprovalQueueComponent),
  data: { title: 'Approval Queue', sidecarContext: 'approval-queue' }
}
```

This is handled by Section 04 but listed here so the implementer knows what to expect.

## PrimeNG Components Used

- `p-chip` -- platform filter chips
- `p-button` -- approve/reject/batch actions
- `p-checkbox` -- item selection for bulk operations
- `p-skeleton` -- loading state placeholder
- `p-toast` -- success/error notifications (via `MessageService`)

Shared atoms from Section 03:
- `StatusBadgeComponent` -- status indicator on each card
- `AxisBarComponent` -- brand voice axis display in expanded card (4 bars when section 01 is complete)
- `ScoreGaugeComponent` -- optional, for expanded card overall score display

## Error Handling

| Scenario | Behavior |
|----------|----------|
| `getPending` fails | Set `error` signal, show PrimeNG error toast, render empty state with retry |
| `approve` fails | Show error toast with message, item remains in list |
| `reject` fails | Show error toast, item remains in list, feedback preserved |
| `batchApprove` partial failure | Show warning toast with success/total count, reload list to get accurate state |
| Network error | Error interceptor shows toast (global behavior from `error.interceptor.ts`) |

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/.../pages/approval-queue/approval-api.service.ts` | Create | API service wrapping approval endpoints |
| `src/.../pages/approval-queue/approval-api.service.spec.ts` | Create | Unit tests for API service |
| `src/.../pages/approval-queue/approval.store.ts` | Create | NgRx SignalStore for approval queue state |
| `src/.../pages/approval-queue/approval.store.spec.ts` | Create | Unit tests for store state management |
| `src/.../pages/approval-queue/approval-queue.component.ts` | Create | Main page component with template |
| `src/.../pages/approval-queue/approval-queue.component.spec.ts` | Create | Component rendering and interaction tests |
| `src/.../pages/approval-queue/approval-queue.component.scss` | Create | Component styles |
| `src/.../pages/approval-queue/approval-queue.component.html` | Create | Component template (or inline) |

All paths are relative to `src/PersonalBrandAssistant.Web/src/app/`.

---

## Implementation Notes (What Was Actually Built)

### Files Created
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-api.service.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-api.service.spec.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval.store.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval.store.spec.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.html`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.scss`
- `src/PersonalBrandAssistant.Web/src/app/pages/approval-queue/approval-queue.component.spec.ts`

### Files Modified
- `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` — Updated route to load from `pages/approval-queue/` instead of `features/approval-queue/`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/brand-voice-panel/brand-voice-panel.component.spec.ts` — Fixed PrimeNG button click test selector
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/preview-tab.component.spec.ts` — Fixed truncate assertion (11 chars for '...see more')

### Test Count: 21 new tests
- `approval-api.service.spec.ts` — 4 tests (getPending, approve, reject, batchApprove)
- `approval.store.spec.ts` — 10 tests (load, approve, reject, batch, filter, toggle, selectAll, clear, error)
- `approval-queue.component.spec.ts` — 7 tests (create, empty state, cards render, count, chips, expand, loading)

### Deviations from Plan
1. **AxisBarComponent not used in template** — Removed from imports. Score axes display deferred to a later enhancement.
2. **Route points to `pages/` not `features/`** — Consistent with sections 06-07 pattern of moving pages to `pages/` directory.
3. **No toast notifications** — MessageService/toast infrastructure not yet in place. Errors caught silently for now.
4. **`batchApprove()` takes no args** — Reads `selectedIds` from store directly rather than accepting IDs as a parameter.

### Code Review Fixes Applied
- Fixed `toggleExpand` logic bug: signal was updated before condition check, causing stale feedback to leak between cards
- Removed unused `AxisBarComponent` import
