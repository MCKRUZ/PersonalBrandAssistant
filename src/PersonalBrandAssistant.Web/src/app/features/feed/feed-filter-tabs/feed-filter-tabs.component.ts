import { Component, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { take } from 'rxjs';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';

interface TabConfig {
  label: string;
  value: FeedItemType | null;
  testId: string;
}

@Component({
  selector: 'app-feed-filter-tabs',
  standalone: true,
  template: `
    <div class="filter-tabs" role="tablist">
      @for (tab of tabs; track tab.testId) {
        <button
          class="tab"
          [class.active]="store.activeFilter() === tab.value"
          [attr.data-testid]="'tab-' + tab.testId"
          [attr.aria-selected]="store.activeFilter() === tab.value"
          role="tab"
          (click)="selectTab(tab.value)">
          {{ tab.label }}
          @if (getBadge(tab.value); as badge) {
            <span class="badge">{{ badge }}</span>
          }
        </button>
      }
    </div>
  `,
  styles: [`
    .filter-tabs {
      display: flex;
      gap: 4px;
      border-bottom: 1px solid #30363d;
      padding-bottom: 0;
    }

    .tab {
      background: none;
      border: none;
      border-bottom: 2px solid transparent;
      color: #8b949e;
      cursor: pointer;
      font-size: 14px;
      padding: 8px 16px;
      transition: color 0.2s, border-color 0.2s;
      display: flex;
      align-items: center;
      gap: 8px;
      white-space: nowrap;
    }

    .tab:hover {
      color: #f0f6fc;
    }

    .tab.active {
      color: #f0f6fc;
      border-bottom-color: #58a6ff;
    }

    .badge {
      background: #30363d;
      color: #8b949e;
      font-size: 11px;
      font-weight: 600;
      padding: 1px 6px;
      border-radius: 10px;
      min-width: 18px;
      text-align: center;
    }

    .tab.active .badge {
      background: #1f6feb;
      color: #f0f6fc;
    }

    @media (max-width: 768px) {
      .filter-tabs {
        overflow-x: auto;
        scrollbar-width: none;
      }
      .filter-tabs::-webkit-scrollbar {
        display: none;
      }
    }
  `]
})
export class FeedFilterTabsComponent {
  protected readonly store = inject(FeedStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly tabs: readonly TabConfig[] = [
    { label: 'All', value: null, testId: 'all' },
    { label: 'Drafts', value: FeedItemType.AgentDraft, testId: 'drafts' },
    { label: 'Trends', value: FeedItemType.TrendAlert, testId: 'trends' },
    { label: 'Ideas', value: FeedItemType.IdeaSuggestion, testId: 'ideas' },
    { label: 'Analytics', value: FeedItemType.AnalyticsHighlight, testId: 'analytics' },
    { label: 'Approvals', value: FeedItemType.ApprovalRequest, testId: 'approvals' },
  ];

  constructor() {
    this.route.queryParams.pipe(take(1)).subscribe(params => {
      const type = params['type'];
      if (type && Object.values(FeedItemType).includes(type as FeedItemType)) {
        this.store.setFilter(type as FeedItemType);
      }
    });
  }

  selectTab(type: FeedItemType | null): void {
    this.store.setFilter(type);
    this.router.navigate([], {
      queryParams: { type },
      queryParamsHandling: 'merge',
    });
  }

  protected getBadge(type: FeedItemType | null): number | null {
    const summary = this.store.summary();
    if (!summary) return null;
    if (type === null) return summary.unreadCount || null;
    if (type === FeedItemType.TrendAlert) return summary.trendingCount || null;
    if (type === FeedItemType.ApprovalRequest) return summary.pendingApprovals || null;
    return null;
  }
}
