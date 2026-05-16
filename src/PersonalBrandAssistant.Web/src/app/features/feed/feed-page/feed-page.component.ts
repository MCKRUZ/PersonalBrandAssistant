import { Component, computed, inject } from '@angular/core';
import { FeedStatsBarComponent } from '../feed-stats-bar/feed-stats-bar.component';
import { FeedFilterTabsComponent } from '../feed-filter-tabs/feed-filter-tabs.component';
import { FeedBatchToolbarComponent } from '../feed-batch-toolbar/feed-batch-toolbar.component';
import { FeedCardListComponent } from '../feed-card-list/feed-card-list.component';
import { FeedStore } from '../store/feed.store';

@Component({
  selector: 'app-feed-page',
  standalone: true,
  imports: [FeedStatsBarComponent, FeedFilterTabsComponent, FeedBatchToolbarComponent, FeedCardListComponent],
  template: `
    <div class="page">
      <h1>Feed</h1>
      <p class="subtitle">Your daily command center</p>

      <app-feed-stats-bar />

      <div class="feed-grid">
        <div class="feed-main">
          <app-feed-filter-tabs />

          <app-feed-batch-toolbar />

          @if (store.newItemCount() > 0) {
            <div data-testid="new-items-banner-slot" class="placeholder">
              {{ store.newItemCount() }} new items
            </div>
          }

          <app-feed-card-list
            [items]="store.items()"
            [loading]="store.loading()"
            [selectedIds]="store.selectedIds()"
            (action)="onCardAction($event)"
            (select)="store.toggleSelect($event)" />

          @if (store.totalCount() > store.pageSize()) {
            <div data-testid="paginator" class="paginator">
              Page {{ store.page() }} of {{ totalPages() }}
            </div>
          }
        </div>

        <aside class="feed-sidebar" data-testid="sidebar-slot">
          <div class="placeholder">Sidebar</div>
        </aside>
      </div>
    </div>
  `,
  styles: [`
    .page { padding: 8px 0; }
    h1 { font-size: 24px; font-weight: 600; margin: 0 0 4px; color: #f0f6fc; }
    .subtitle { color: #8b949e; margin: 0 0 24px; font-size: 14px; }

    .feed-grid {
      display: grid;
      grid-template-columns: 1fr 320px;
      gap: 24px;
      align-items: start;
    }

    .feed-main {
      display: flex;
      flex-direction: column;
      gap: 16px;
      min-width: 0;
    }

    .feed-sidebar {
      position: sticky;
      top: 24px;
    }

    .placeholder {
      background: #161b22;
      border: 1px dashed #30363d;
      border-radius: 8px;
      padding: 24px;
      color: #484f58;
      text-align: center;
      font-size: 13px;
    }

    .paginator {
      display: flex;
      justify-content: center;
      padding: 16px;
      color: #8b949e;
      font-size: 14px;
    }

    @media (max-width: 768px) {
      .feed-grid {
        grid-template-columns: 1fr;
      }
      .feed-sidebar {
        position: static;
      }
    }
  `]
})
export class FeedPageComponent {
  protected readonly store = inject(FeedStore);
  protected readonly totalPages = computed(() =>
    Math.ceil(this.store.totalCount() / this.store.pageSize())
  );

  protected onCardAction(event: { id: string; action: string }): void {
    this.store.actOnItem(event.id, event.action);
  }
}
