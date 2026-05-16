import { Component, computed, inject } from '@angular/core';
import { FeedStore } from '../store/feed.store';

@Component({
  selector: 'app-feed-new-items-banner',
  standalone: true,
  template: `
    @if (count() > 0) {
      <div class="banner slide-down" data-testid="new-items-banner">
        <span data-testid="banner-message">
          <i class="pi pi-arrow-up"></i>
          {{ count() }} new {{ count() === 1 ? 'item' : 'items' }}
        </span>
        <button type="button"
                class="show-btn"
                data-testid="show-btn"
                (click)="onShow()">
          Show
        </button>
      </div>
    }
  `,
  styles: [`
    .banner {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 10px 16px;
      background: rgba(31, 111, 235, 0.15);
      border: 1px solid #1f6feb;
      border-radius: 8px;
      font-size: 13px;
      color: #58a6ff;
    }

    .banner i { font-size: 12px; }

    .show-btn {
      font-size: 12px;
      font-weight: 600;
      padding: 4px 12px;
      color: #fff;
      background: #1f6feb;
      border: none;
      border-radius: 6px;
      cursor: pointer;
      transition: background 0.15s;
    }

    .show-btn:hover { background: #388bfd; }

    .slide-down {
      animation: slideDown 0.3s ease-out;
    }

    @keyframes slideDown {
      from {
        opacity: 0;
        transform: translateY(-10px);
      }
      to {
        opacity: 1;
        transform: translateY(0);
      }
    }
  `]
})
export class FeedNewItemsBannerComponent {
  protected readonly store = inject(FeedStore);
  protected readonly count = computed(() => this.store.newItemCount());

  protected onShow(): void {
    this.store.loadNewItems();
  }
}
