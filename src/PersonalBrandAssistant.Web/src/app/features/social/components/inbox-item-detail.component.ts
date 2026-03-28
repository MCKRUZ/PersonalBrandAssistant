import { Component, inject, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { Tooltip } from 'primeng/tooltip';
import { SocialService } from '../services/social.service';
import { SocialStore } from '../store/social.store';
import { SocialInboxItem } from '../models/social.model';

@Component({
  selector: 'app-inbox-item-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, TagModule, TextareaModule, Tooltip],
  template: `
    <div class="detail-container">
      <div class="detail-meta">
        <p-tag [value]="item().platform" severity="info" />
        <p-tag [value]="item().itemType" severity="secondary" />
        <span class="meta-date">{{ item().receivedAt | date:'medium' }}</span>
      </div>

      <div class="detail-author">
        <strong>{{ item().authorName }}</strong>
        @if (item().sourceUrl) {
          <a [href]="item().sourceUrl" target="_blank" rel="noopener" class="source-link"
             pTooltip="Open original post on the platform">
            <i class="pi pi-external-link"></i> View on {{ item().platform }}
          </a>
        }
      </div>

      <div class="detail-content">
        <p>{{ item().content }}</p>
      </div>

      <hr />

      <div class="reply-section">
        <h4>Reply</h4>
        @if (item().replySent) {
          <div class="reply-sent">
            <i class="pi pi-check-circle"></i>
            Reply sent: <em>{{ item().draftReply }}</em>
          </div>
        } @else {
          <div class="reply-actions">
            <p-button
              label="AI Draft"
              icon="pi pi-sparkles"
              [outlined]="true"
              [loading]="drafting"
              (onClick)="draftReply()"
              pTooltip="Generate an AI reply draft based on this message"
            />
          </div>

          @if (replyText) {
            <textarea
              pTextarea
              [(ngModel)]="replyText"
              [rows]="4"
              class="w-full mt-1"
            ></textarea>
            <div class="send-actions">
              <p-button
                label="Send Reply"
                icon="pi pi-send"
                (onClick)="sendReply()"
                [loading]="sending"
                pTooltip="Send this reply to the platform"
              />
            </div>
          }
        }
      </div>
    </div>
  `,
  styles: [`
    .detail-container {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .detail-meta {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .meta-date {
      margin-left: auto;
      font-size: 0.85rem;
      color: var(--text-color-secondary);
    }
    .detail-author {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .source-link {
      font-size: 0.85rem;
      color: var(--primary-color);
      text-decoration: none;
    }
    .detail-content {
      background: var(--surface-50);
      padding: 1rem;
      border-radius: 8px;
    }
    .detail-content p {
      margin: 0;
      white-space: pre-wrap;
      line-height: 1.5;
    }
    .reply-section h4 {
      margin: 0 0 0.5rem 0;
    }
    .reply-actions {
      margin-bottom: 0.75rem;
    }
    .send-actions {
      display: flex;
      justify-content: flex-end;
      margin-top: 0.5rem;
    }
    .reply-sent {
      background: var(--green-50);
      padding: 0.75rem;
      border-radius: 6px;
      color: var(--green-700);
    }
    .reply-sent em {
      display: block;
      margin-top: 0.5rem;
      color: var(--text-color);
    }
    .w-full { width: 100%; }
    .mt-1 { margin-top: 0.5rem; }
    hr {
      border: none;
      border-top: 1px solid var(--surface-200);
      margin: 0.5rem 0;
    }
  `],
})
export class InboxItemDetailComponent {
  private readonly service = inject(SocialService);
  private readonly store = inject(SocialStore);

  item = input.required<SocialInboxItem>();
  closed = output<void>();

  replyText = '';
  drafting = false;
  sending = false;

  draftReply() {
    this.drafting = true;
    this.service.draftReply(this.item().id).subscribe({
      next: draft => {
        this.replyText = typeof draft === 'string' ? draft : (draft as any)?.value ?? '';
        this.drafting = false;
      },
      error: () => {
        this.drafting = false;
      },
    });

    // Mark as read
    if (!this.item().isRead) {
      this.service.markRead(this.item().id).subscribe();
    }
  }

  sendReply() {
    if (!this.replyText.trim()) return;

    this.sending = true;
    this.service.sendReply(this.item().id, this.replyText).subscribe({
      next: () => {
        this.sending = false;
        this.store.loadInbox(this.store.inboxFilter());
        this.closed.emit();
      },
      error: () => {
        this.sending = false;
      },
    });
  }
}
