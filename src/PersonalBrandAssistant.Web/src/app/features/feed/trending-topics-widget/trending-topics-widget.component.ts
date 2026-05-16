import { Component, inject } from '@angular/core';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';

@Component({
  selector: 'app-trending-topics-widget',
  standalone: true,
  template: `
    <div class="widget">
      <h3 class="widget-title">
        <i class="pi pi-chart-line"></i>
        Trending Topics
      </h3>

      @if (store.loading() && store.trendingTopics().length === 0) {
        <div data-testid="trending-skeleton" class="skeleton-list">
          @for (_ of skeletonRows; track $index) {
            <div class="skeleton-row">
              <div class="skeleton-rank"></div>
              <div class="skeleton-text"></div>
              <div class="skeleton-badge"></div>
            </div>
          }
        </div>
      } @else if (store.trendingTopics().length === 0) {
        <p data-testid="trending-empty" class="empty-text">No trends yet</p>
      } @else {
        <div class="topic-list">
          @for (topic of store.trendingTopics(); track topic.topic; let i = $index) {
            <div class="topic-row"
                 data-testid="trending-topic"
                 (click)="onTopicClick()">
              <span class="topic-rank" data-testid="topic-rank">{{ i + 1 }}</span>
              <span class="topic-name" data-testid="topic-name">{{ topic.topic }}</span>
              <span class="topic-count" data-testid="topic-count">{{ topic.count }}</span>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .widget {
      background: #161b22;
      border: 1px solid #30363d;
      border-radius: 8px;
      padding: 16px;
    }

    .widget-title {
      font-size: 14px;
      font-weight: 600;
      color: #f0f6fc;
      margin: 0 0 12px;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .widget-title i { color: #f97316; font-size: 14px; }

    .topic-list { display: flex; flex-direction: column; gap: 4px; }

    .topic-row {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px;
      border-radius: 6px;
      cursor: pointer;
      transition: background 0.15s;
    }

    .topic-row:hover { background: #1c2128; }

    .topic-rank {
      font-size: 12px;
      font-weight: 600;
      color: #484f58;
      width: 18px;
      text-align: center;
    }

    .topic-name {
      flex: 1;
      font-size: 13px;
      color: #f0f6fc;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .topic-count {
      font-size: 11px;
      font-weight: 600;
      background: #30363d;
      color: #8b949e;
      padding: 2px 8px;
      border-radius: 10px;
    }

    .empty-text {
      font-size: 13px;
      color: #484f58;
      text-align: center;
      margin: 16px 0 8px;
    }

    .skeleton-list { display: flex; flex-direction: column; gap: 8px; }

    .skeleton-row {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px;
    }

    .skeleton-rank, .skeleton-text, .skeleton-badge {
      background: #21262d;
      border-radius: 4px;
      animation: pulse 1.5s ease-in-out infinite;
    }

    .skeleton-rank { width: 18px; height: 14px; }
    .skeleton-text { flex: 1; height: 14px; }
    .skeleton-badge { width: 32px; height: 18px; border-radius: 10px; }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
    }
  `]
})
export class TrendingTopicsWidgetComponent {
  protected readonly store = inject(FeedStore);
  protected readonly skeletonRows = Array(4);

  protected onTopicClick(): void {
    this.store.setFilter(FeedItemType.TrendAlert);
  }
}
