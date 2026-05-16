import { Component, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';

@Component({
  selector: 'app-feed-stats-bar',
  standalone: true,
  imports: [DecimalPipe],
  template: `
    <div class="stats-bar">
      @if (store.summary(); as summary) {
        <div class="stat-card" data-testid="stat-unread" (click)="store.setFilter(null)">
          <span class="stat-value">{{ summary.unreadCount }}</span>
          <span class="stat-label">Unread</span>
        </div>
        <div class="stat-card" data-testid="stat-approvals" (click)="store.setFilter(approvalType)">
          <span class="stat-value">{{ summary.pendingApprovals }}</span>
          <span class="stat-label">Pending Approvals</span>
        </div>
        <div class="stat-card" data-testid="stat-trending" (click)="store.setFilter(trendType)">
          <span class="stat-value">{{ summary.trendingCount }}</span>
          <span class="stat-label">Trending</span>
        </div>
        <div class="stat-card" data-testid="stat-engagement" (click)="store.setFilter(analyticsType)">
          <span class="stat-value" [class.positive]="summary.engagementDelta >= 0" [class.negative]="summary.engagementDelta < 0">
            @if (summary.engagementDelta >= 0) {
              <span class="trend-up">&#9650;</span>
            } @else {
              <span class="trend-down">&#9660;</span>
            }
            {{ summary.engagementDelta | number:'1.1-1' }}%
          </span>
          <span class="stat-label">Engagement</span>
        </div>
      } @else {
        @for (_ of skeletonCards; track $index) {
          <div class="stat-card stat-skeleton">
            <span class="skeleton-value"></span>
            <span class="skeleton-label"></span>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .stats-bar {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 24px;
    }

    .stat-card {
      background: #161b22;
      border: 1px solid #30363d;
      border-radius: 8px;
      padding: 20px;
      cursor: pointer;
      transition: border-color 0.2s;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .stat-card:hover {
      border-color: #58a6ff;
    }

    .stat-value {
      font-size: 28px;
      font-weight: 700;
      color: #f0f6fc;
    }

    .stat-label {
      font-size: 13px;
      color: #8b949e;
    }

    .positive { color: #3fb950; }
    .negative { color: #f85149; }
    .trend-up { font-size: 14px; }
    .trend-down { font-size: 14px; }

    .stat-skeleton {
      cursor: default;
    }

    .stat-skeleton:hover {
      border-color: #30363d;
    }

    .skeleton-value {
      display: block;
      width: 60px;
      height: 28px;
      background: #21262d;
      border-radius: 4px;
      animation: pulse 1.5s ease-in-out infinite;
    }

    .skeleton-label {
      display: block;
      width: 80px;
      height: 13px;
      background: #21262d;
      border-radius: 4px;
      animation: pulse 1.5s ease-in-out infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    @media (max-width: 768px) {
      .stats-bar {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    @media (max-width: 480px) {
      .stats-bar {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class FeedStatsBarComponent {
  protected readonly store = inject(FeedStore);
  protected readonly approvalType = FeedItemType.ApprovalRequest;
  protected readonly trendType = FeedItemType.TrendAlert;
  protected readonly analyticsType = FeedItemType.AnalyticsHighlight;
  protected readonly skeletonCards = [0, 1, 2, 3];
}
