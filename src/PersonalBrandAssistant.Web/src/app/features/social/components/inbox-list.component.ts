import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { SelectModule } from 'primeng/select';
import { Tooltip } from 'primeng/tooltip';
import { SocialStore } from '../store/social.store';
import { SocialInboxItem, SocialPlatformType } from '../models/social.model';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';
import { InboxItemDetailComponent } from './inbox-item-detail.component';

@Component({
  selector: 'app-inbox-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ButtonModule, TagModule, SelectModule,
    Tooltip, EmptyStateComponent, LoadingSpinnerComponent, InboxItemDetailComponent,
  ],
  template: `
    <div class="inbox-toolbar">
      <p-select
        [options]="platformOptions"
        [(ngModel)]="platformFilter"
        (ngModelChange)="onFilterChange()"
        placeholder="All Platforms"
        [showClear]="true"
        [style]="{width: '180px'}"
        pTooltip="Filter inbox by platform"
      />
      <p-select
        [options]="readOptions"
        [(ngModel)]="readFilter"
        (ngModelChange)="onFilterChange()"
        optionLabel="label"
        optionValue="value"
        placeholder="All Messages"
        [showClear]="true"
        [style]="{width: '160px'}"
        pTooltip="Filter by read/unread status"
      />
      <div class="flex-1"></div>
      <p-button icon="pi pi-refresh" [text]="true" (onClick)="refresh()" pTooltip="Refresh inbox from all platforms" />
    </div>

    @if (store.loading()) {
      <app-loading-spinner />
    } @else if (!store.hasInboxItems()) {
      <app-empty-state
        icon="pi pi-inbox"
        title="Inbox is empty"
        message="No mentions, replies, or direct messages yet. They'll appear here as platforms are polled."
      />
    } @else {
      <div class="inbox-split-pane" [class.mobile-detail-open]="mobileDetailOpen">
        <div class="inbox-left-panel" [class.hidden-mobile]="mobileDetailOpen">
          @for (item of store.inboxItems(); track item.id) {
            <div
              class="inbox-item"
              [class.unread]="!item.isRead"
              [class.selected]="store.selectedInboxItem()?.id === item.id"
              (click)="selectItem(item)"
            >
              <div class="item-left">
                <p-tag [value]="item.platform" [severity]="getPlatformSeverity(item.platform)" size="small" />
                <p-tag [value]="item.itemType" severity="secondary" size="small" />
              </div>
              <div class="item-content">
                <div class="item-author">
                  <strong>{{ item.authorName }}</strong>
                  <span class="item-date">{{ item.receivedAt | date:'short' }}</span>
                </div>
                <p class="item-text">{{ item.content | slice:0:120 }}{{ item.content.length > 120 ? '...' : '' }}</p>
              </div>
              <div class="item-right">
                @if (item.replySent) {
                  <i class="pi pi-check-circle replied-icon" pTooltip="Reply sent"></i>
                } @else if (item.draftReply) {
                  <i class="pi pi-pencil draft-icon" pTooltip="AI draft ready"></i>
                }
              </div>
            </div>
          }
        </div>
        <div class="inbox-right-panel" [class.hidden-mobile]="!mobileDetailOpen">
          @if (store.selectedInboxItem()) {
            <div class="mobile-back">
              <p-button
                icon="pi pi-arrow-left"
                label="Back"
                [text]="true"
                size="small"
                (onClick)="mobileDetailOpen = false"
              />
            </div>
            <app-inbox-item-detail
              [item]="store.selectedInboxItem()!"
              (closed)="onDetailClosed()"
            />
          } @else {
            <div class="empty-detail">
              <i class="pi pi-inbox empty-detail-icon"></i>
              <p>Select a message to view details</p>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    .inbox-toolbar {
      display: flex;
      gap: 0.5rem;
      align-items: center;
      margin-bottom: 1rem;
    }
    .flex-1 { flex: 1; }
    .inbox-split-pane {
      display: flex;
      border: 1px solid var(--surface-200);
      border-radius: 8px;
      height: 60vh;
      overflow: hidden;
    }
    .inbox-left-panel {
      width: 340px;
      border-right: 1px solid var(--surface-200);
      overflow-y: auto;
    }
    .inbox-right-panel {
      flex: 1;
      overflow-y: auto;
      padding: 1rem;
    }
    .inbox-item {
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      cursor: pointer;
      transition: background 0.15s;
      border-bottom: 1px solid var(--surface-100);
    }
    .inbox-item:hover {
      background: var(--surface-50);
    }
    .inbox-item.unread {
      border-left: 3px solid var(--primary-color);
      background: var(--surface-0);
    }
    .inbox-item.selected {
      background: var(--primary-50);
      border-left: 3px solid var(--primary-color);
    }
    .item-left {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-width: 80px;
    }
    .item-content {
      flex: 1;
      min-width: 0;
    }
    .item-author {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 0.25rem;
    }
    .item-date {
      font-size: 0.8rem;
      color: var(--text-color-secondary);
    }
    .item-text {
      margin: 0;
      font-size: 0.85rem;
      color: var(--text-color-secondary);
      line-height: 1.4;
    }
    .item-right {
      display: flex;
      align-items: center;
    }
    .replied-icon {
      color: var(--green-500);
      font-size: 1.1rem;
    }
    .draft-icon {
      color: var(--orange-500);
      font-size: 1.1rem;
    }
    .empty-detail {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      height: 100%;
      color: var(--text-color-secondary);
    }
    .empty-detail-icon {
      font-size: 3rem;
      margin-bottom: 1rem;
      opacity: 0.4;
    }
    .empty-detail p {
      margin: 0;
      font-size: 0.95rem;
    }
    .mobile-back {
      display: none;
      margin-bottom: 0.5rem;
    }
    @media (max-width: 768px) {
      .inbox-left-panel {
        width: 100%;
        border-right: none;
      }
      .inbox-right-panel {
        width: 100%;
      }
      .hidden-mobile {
        display: none !important;
      }
      .mobile-back {
        display: block;
      }
    }
  `],
})
export class InboxListComponent {
  readonly store = inject(SocialStore);

  platformFilter: SocialPlatformType | null = null;
  readFilter: boolean | null = null;
  mobileDetailOpen = false;

  platformOptions = ['Reddit', 'TwitterX', 'LinkedIn', 'Instagram', 'YouTube'];
  readOptions = [
    { label: 'Unread', value: false },
    { label: 'Read', value: true },
  ];

  getPlatformSeverity(platform: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const map: Record<string, 'success' | 'info' | 'warn' | 'danger' | 'secondary'> = {
      Reddit: 'danger',
      TwitterX: 'info',
      LinkedIn: 'success',
      Instagram: 'warn',
      YouTube: 'danger',
    };
    return map[platform] ?? 'secondary';
  }

  onFilterChange() {
    this.store.loadInbox({
      platform: this.platformFilter ?? undefined,
      isRead: this.readFilter ?? undefined,
    });
  }

  refresh() {
    this.store.loadInbox(this.store.inboxFilter());
  }

  selectItem(item: SocialInboxItem) {
    this.store.selectInboxItem(item);
    this.mobileDetailOpen = true;
  }

  onDetailClosed() {
    this.store.selectInboxItem(undefined);
    this.mobileDetailOpen = false;
  }
}
