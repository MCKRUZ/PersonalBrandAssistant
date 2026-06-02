import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { Content, Platform } from '../../models/content.model';
import { formatContentType, voiceScoreClass, platformIconClass, truncateText } from '../content-display.utils';

@Component({
  selector: 'app-content-card',
  standalone: true,
  imports: [DatePipe, ButtonModule, TagModule],
  template: `
    <div class="content-card" data-testid="content-card">
      <div class="card-body">
        <div class="card-header">
          <span class="status-badge" [attr.data-status]="content().status">
            {{ content().status }}
          </span>
          <span class="platform-icon" [attr.data-platform]="content().primaryPlatform">
            <i [class]="platformIcon(content().primaryPlatform)"></i>
          </span>
          <span class="content-type">{{ formatType(content().contentType) }}</span>
          <span class="voice-dot" [class]="voiceClass(content().voiceScore)"></span>
        </div>
        <h3 class="card-title">{{ truncate(content().title, 100) }}</h3>
        <div class="card-meta">
          <span class="updated-at">{{ content().updatedAt | date: 'shortDate' }}</span>
        </div>
        @if ((content().platformPublishes?.length ?? 0) > 0) {
          <div class="publish-badges" data-testid="publish-badges">
            @for (pub of content().platformPublishes; track pub.platform) {
              <span class="pub-badge" [attr.data-status]="pub.publishStatus"
                    [attr.data-platform]="pub.platform">
                <i [class]="platformIcon(pub.platform)"></i>
                @if (pub.publishStatus === 'Failed') {
                  <button class="retry-btn" (click)="retry.emit(pub.platform); $event.stopPropagation()"
                          data-testid="retry-btn">
                    <i class="pi pi-refresh"></i>
                  </button>
                }
              </span>
            }
          </div>
        }
        @if (content().tags.length > 0) {
          <div class="card-tags">
            @for (tag of content().tags.slice(0, 3); track tag) {
              <p-tag [value]="tag" severity="secondary" />
            }
            @if (content().tags.length > 3) {
              <span class="more-tags">+{{ content().tags.length - 3 }}</span>
            }
          </div>
        }
        <div class="card-actions">
          <p-button
            icon="pi pi-pencil"
            severity="secondary"
            [text]="true"
            size="small"
            (onClick)="edit.emit(content().id)"
            data-testid="edit-btn"
            pTooltip="Edit" />
          <p-button
            icon="pi pi-copy"
            severity="secondary"
            [text]="true"
            size="small"
            (onClick)="duplicate.emit(content().id)"
            data-testid="duplicate-btn"
            pTooltip="Duplicate" />
          <p-button
            icon="pi pi-trash"
            severity="danger"
            [text]="true"
            size="small"
            (onClick)="onDelete.emit(content().id)"
            data-testid="delete-btn"
            pTooltip="Delete" />
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .content-card {
        background: var(--surface-card);
        border: 1px solid transparent;
        border-radius: 8px;
        overflow: hidden;
        transition: border-color 0.2s;
      }
      .content-card:hover {
        border-color: var(--surface-border);
      }
      .card-body {
        padding: 12px;
      }
      .card-header {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 12px;
        color: var(--text-secondary);
        margin-bottom: 8px;
      }
      .status-badge {
        font-size: 11px;
        font-weight: 600;
        padding: 2px 8px;
        border-radius: 12px;
        text-transform: uppercase;
      }
      .status-badge[data-status='Idea'] { background: var(--accent-soft); color: var(--brand-primary); }
      .status-badge[data-status='Draft'] { background: rgba(210, 153, 34, 0.2); color: var(--voice-mid); }
      .status-badge[data-status='Review'] { background: #bc8cff33; color: #bc8cff; }
      .status-badge[data-status='Approved'] { background: rgba(63, 185, 80, 0.2); color: var(--status-approved); }
      .status-badge[data-status='Scheduled'] { background: #39d2c033; color: #39d2c0; }
      .status-badge[data-status='Published'] { background: rgba(46, 160, 67, 0.2); color: var(--status-approved); }
      .status-badge[data-status='Archived'] { background: rgba(138, 138, 150, 0.2); color: var(--text-secondary); }
      .platform-icon {
        font-size: 14px;
      }
      .content-type {
        font-size: 11px;
        color: var(--text-secondary);
      }
      .voice-dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        margin-left: auto;
      }
      .voice-green { background: var(--status-approved); }
      .voice-amber { background: var(--voice-mid); }
      .voice-red { background: var(--voice-low); }
      .voice-none { background: var(--text-secondary); }
      .card-title {
        font-size: 15px;
        font-weight: 600;
        color: var(--text-primary);
        margin: 0 0 8px;
        line-height: 1.3;
      }
      .card-meta {
        font-size: 12px;
        color: var(--text-secondary);
        margin-bottom: 8px;
      }
      .card-tags {
        display: flex;
        gap: 4px;
        flex-wrap: wrap;
        margin-bottom: 8px;
      }
      .more-tags {
        font-size: 11px;
        color: var(--text-secondary);
        align-self: center;
      }
      .publish-badges {
        display: flex;
        gap: 4px;
        flex-wrap: wrap;
        margin-bottom: 8px;
      }
      .pub-badge {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 6px;
        border-radius: 4px;
        font-size: 12px;
      }
      .pub-badge[data-status='Published'] { background: rgba(46, 160, 67, 0.2); color: var(--status-approved); }
      .pub-badge[data-status='Failed'] { background: rgba(248, 81, 73, 0.2); color: var(--voice-low); }
      .pub-badge[data-status='Pending'],
      .pub-badge[data-status='Formatting'] { background: rgba(210, 153, 34, 0.2); color: var(--voice-mid); }
      .retry-btn {
        background: none; border: none; color: inherit; cursor: pointer;
        padding: 0; font-size: 11px; line-height: 1;
      }
      .card-actions {
        display: flex;
        gap: 4px;
        border-top: 1px solid var(--surface-elevated);
        padding-top: 8px;
      }
    `,
  ],
})
export class ContentCardComponent {
  readonly content = input.required<Content>();
  readonly edit = output<string>();
  readonly onDelete = output<string>();
  readonly duplicate = output<string>();
  readonly retry = output<Platform>();

  readonly truncate = truncateText;
  readonly platformIcon = platformIconClass;
  readonly formatType = formatContentType;
  readonly voiceClass = voiceScoreClass;
}
