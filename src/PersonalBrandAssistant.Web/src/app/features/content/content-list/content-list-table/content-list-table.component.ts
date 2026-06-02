import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { Content } from '../../models/content.model';
import { formatContentType, voiceScoreClass } from '../content-display.utils';

@Component({
  selector: 'app-content-list-table',
  standalone: true,
  imports: [DatePipe, ButtonModule],
  template: `
    <div class="content-list-table" data-testid="content-list-table">
      <div class="list-header">
        <span class="col-status">Status</span>
        <span class="col-title">Title</span>
        <span class="col-platform">Platform</span>
        <span class="col-type">Type</span>
        <span class="col-voice">Voice</span>
        <span class="col-date">Updated</span>
        <span class="col-actions">Actions</span>
      </div>

      @for (content of contents(); track content.id) {
        <div class="list-row" data-testid="content-row">
          <span class="col-status">
            <span class="status-dot" [attr.data-status]="content.status"></span>
          </span>
          <span class="col-title">{{ content.title }}</span>
          <span class="col-platform">{{ content.primaryPlatform }}</span>
          <span class="col-type">{{ formatType(content.contentType) }}</span>
          <span class="col-voice">
            @if (content.voiceScore !== null) {
              <span class="voice-dot" [class]="voiceClass(content.voiceScore)"></span>
              {{ content.voiceScore }}%
            } @else {
              <span class="voice-dot voice-none"></span>
            }
          </span>
          <span class="col-date">{{ content.updatedAt | date: 'shortDate' }}</span>
          <span class="col-actions">
            <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small"
              (onClick)="edit.emit(content.id)" pTooltip="Edit" />
            <p-button icon="pi pi-copy" severity="secondary" [text]="true" size="small"
              (onClick)="duplicate.emit(content.id)" pTooltip="Duplicate" />
            <p-button icon="pi pi-trash" severity="danger" [text]="true" size="small"
              (onClick)="onDelete.emit(content.id)" pTooltip="Delete" />
          </span>
        </div>
      } @empty {
        <div class="empty-state" data-testid="empty-state">
          <p>No content found</p>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .content-list-table {
        display: flex;
        flex-direction: column;
      }
      .list-header {
        display: grid;
        grid-template-columns: 60px 1fr 100px 120px 80px 100px 120px;
        gap: 8px;
        padding: 8px 12px;
        font-size: 12px;
        font-weight: 600;
        color: var(--text-secondary);
        text-transform: uppercase;
        border-bottom: 1px solid var(--surface-elevated);
      }
      .list-row {
        display: grid;
        grid-template-columns: 60px 1fr 100px 120px 80px 100px 120px;
        gap: 8px;
        padding: 10px 12px;
        align-items: center;
        font-size: 13px;
        color: var(--text-primary);
        border-bottom: 1px solid var(--surface-card);
        transition: background 0.15s;
      }
      .list-row:hover {
        background: var(--surface-card);
      }
      .status-dot {
        display: inline-block;
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: var(--text-secondary);
      }
      .status-dot[data-status='Idea'] { background: var(--brand-primary); }
      .status-dot[data-status='Draft'] { background: var(--voice-mid); }
      .status-dot[data-status='Review'] { background: #bc8cff; }
      .status-dot[data-status='Approved'] { background: var(--status-approved); }
      .status-dot[data-status='Scheduled'] { background: #39d2c0; }
      .status-dot[data-status='Published'] { background: var(--status-approved); }
      .status-dot[data-status='Archived'] { background: var(--text-secondary); }
      .voice-dot {
        display: inline-block;
        width: 8px;
        height: 8px;
        border-radius: 50%;
        margin-right: 4px;
      }
      .voice-green { background: var(--status-approved); }
      .voice-amber { background: var(--voice-mid); }
      .voice-red { background: var(--voice-low); }
      .voice-none { background: var(--text-secondary); }
      .col-title {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .col-platform, .col-type {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: var(--text-secondary);
      }
      .col-voice {
        font-size: 12px;
        color: var(--text-secondary);
        display: flex;
        align-items: center;
      }
      .col-date {
        color: var(--text-secondary);
        font-size: 12px;
      }
      .col-actions {
        display: flex;
        gap: 2px;
      }
      .empty-state {
        text-align: center;
        padding: 48px 16px;
        color: var(--text-secondary);
      }
    `,
  ],
})
export class ContentListTableComponent {
  readonly contents = input.required<Content[]>();
  readonly edit = output<string>();
  readonly onDelete = output<string>();
  readonly duplicate = output<string>();

  readonly formatType = formatContentType;
  readonly voiceClass = voiceScoreClass;
}
