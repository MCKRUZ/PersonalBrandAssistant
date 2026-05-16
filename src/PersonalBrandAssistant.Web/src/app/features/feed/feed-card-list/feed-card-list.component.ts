import { Component, input, output } from '@angular/core';
import { FeedItem } from '../models/feed-item.model';
import { FeedCardComponent } from '../feed-card/feed-card.component';

@Component({
  selector: 'app-feed-card-list',
  standalone: true,
  imports: [FeedCardComponent],
  template: `
    @if (loading()) {
      <div class="skeleton-list" data-testid="skeleton-list">
        @for (_ of skeletonSlots; track $index) {
          <div class="skeleton-card" data-testid="skeleton-card">
            <div class="skeleton-header">
              <div class="skeleton-box" style="width: 120px; height: 14px;"></div>
              <div class="skeleton-box" style="width: 60px; height: 12px;"></div>
            </div>
            <div class="skeleton-box" style="width: 70%; height: 16px; margin-bottom: 6px;"></div>
            <div class="skeleton-box" style="width: 100%; height: 13px; margin-bottom: 4px;"></div>
            <div class="skeleton-box" style="width: 60%; height: 13px; margin-bottom: 12px;"></div>
            <div class="skeleton-actions">
              <div class="skeleton-box" style="width: 80px; height: 28px; border-radius: 6px;"></div>
              <div class="skeleton-box" style="width: 60px; height: 28px; border-radius: 6px;"></div>
            </div>
          </div>
        }
      </div>
    } @else if (items().length === 0) {
      <div class="empty-state" data-testid="empty-state">
        <i class="pi pi-check-circle empty-icon"></i>
        <p class="empty-text">You're all caught up!</p>
      </div>
    } @else {
      <div class="card-list" data-testid="card-list">
        @for (item of items(); track item.id) {
          <app-feed-card
            [item]="item"
            [selectedIds]="selectedIds()"
            (action)="action.emit($event)"
            (select)="select.emit($event)" />
        }
      </div>
    }
  `,
  styles: [`
    .card-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .skeleton-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .skeleton-card {
      background: #161b22;
      border: 1px solid #30363d;
      border-left: 3px solid #21262d;
      border-radius: 8px;
      padding: 16px;
    }

    .skeleton-header {
      display: flex;
      justify-content: space-between;
      margin-bottom: 12px;
    }

    .skeleton-box {
      display: block;
      height: 14px;
      background: linear-gradient(90deg, #21262d 25%, #30363d 50%, #21262d 75%);
      background-size: 200% 100%;
      border-radius: 4px;
      animation: shimmer 1.5s ease-in-out infinite;
    }

    .skeleton-actions {
      display: flex;
      gap: 8px;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 48px 24px;
      color: #484f58;
    }

    .empty-icon {
      font-size: 48px;
      color: #238636;
      margin-bottom: 16px;
    }

    .empty-text {
      font-size: 16px;
      color: #8b949e;
      margin: 0;
    }

    @keyframes shimmer {
      0% { background-position: 200% 0; }
      100% { background-position: -200% 0; }
    }
  `]
})
export class FeedCardListComponent {
  readonly items = input<FeedItem[]>([]);
  readonly loading = input(false);
  readonly selectedIds = input<string[]>([]);

  readonly action = output<{ id: string; action: string }>();
  readonly select = output<string>();

  protected readonly skeletonSlots = [0, 1, 2, 3, 4];
}
