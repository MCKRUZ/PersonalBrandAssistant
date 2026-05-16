import { Component, inject } from '@angular/core';
import { FeedStore } from '../store/feed.store';

@Component({
  selector: 'app-feed-batch-toolbar',
  standalone: true,
  template: `
    @if (store.hasSelection()) {
      <div class="batch-toolbar" data-testid="batch-toolbar">
        <span class="selected-count" data-testid="selected-count">
          {{ store.selectedCount() }} selected
        </span>

        <div class="actions">
          <button class="btn btn-success" data-testid="btn-approve"
            (click)="approve()">
            Approve
          </button>
          <button class="btn btn-info" data-testid="btn-mark-read"
            (click)="markRead()">
            Mark Read
          </button>
          <button class="btn btn-secondary" data-testid="btn-dismiss"
            (click)="dismiss()">
            Dismiss
          </button>
          <button class="btn btn-text" data-testid="btn-clear"
            (click)="store.clearSelection()">
            Clear
          </button>
        </div>
      </div>
    }
  `,
  styles: [`
    .batch-toolbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      background: #161b22;
      border: 1px solid #30363d;
      border-radius: 8px;
      padding: 12px 16px;
    }

    .selected-count {
      color: #f0f6fc;
      font-size: 14px;
      font-weight: 500;
    }

    .actions {
      display: flex;
      gap: 8px;
    }

    .btn {
      border: none;
      border-radius: 6px;
      cursor: pointer;
      font-size: 13px;
      font-weight: 500;
      padding: 6px 14px;
      transition: opacity 0.2s;
    }

    .btn:hover { opacity: 0.85; }

    .btn-success {
      background: #238636;
      color: #f0f6fc;
    }

    .btn-info {
      background: #1f6feb;
      color: #f0f6fc;
    }

    .btn-secondary {
      background: #30363d;
      color: #c9d1d9;
    }

    .btn-text {
      background: transparent;
      color: #8b949e;
    }

    .btn-text:hover {
      color: #f0f6fc;
    }

    @media (max-width: 480px) {
      .batch-toolbar {
        flex-direction: column;
        gap: 12px;
      }
      .actions {
        flex-wrap: wrap;
        justify-content: center;
      }
    }
  `],
})
export class FeedBatchToolbarComponent {
  protected readonly store = inject(FeedStore);

  approve(): void {
    this.store.batchAct(this.store.selectedIds(), 'approve');
  }

  dismiss(): void {
    this.store.batchAct(this.store.selectedIds(), 'dismiss');
  }

  markRead(): void {
    this.store.batchMarkReadByIds(this.store.selectedIds());
  }
}
