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
        @if (content().platformPublishes?.length > 0) {
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
        background: #161b22;
        border: 1px solid transparent;
        border-radius: 8px;
        overflow: hidden;
        transition: border-color 0.2s;
      }
      .content-card:hover {
        border-color: #30363d;
      }
      .card-body {
        padding: 12px;
      }
      .card-header {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 12px;
        color: #8b949e;
        margin-bottom: 8px;
      }
      .status-badge {
        font-size: 11px;
        font-weight: 600;
        padding: 2px 8px;
        border-radius: 12px;
        text-transform: uppercase;
      }
      .status-badge[data-status='Idea'] { background: #1f6feb33; color: #58a6ff; }
      .status-badge[data-status='Draft'] { background: #d2992233; color: #d29922; }
      .status-badge[data-status='Review'] { background: #bc8cff33; color: #bc8cff; }
      .status-badge[data-status='Approved'] { background: #3fb95033; color: #3fb950; }
      .status-badge[data-status='Scheduled'] { background: #39d2c033; color: #39d2c0; }
      .status-badge[data-status='Published'] { background: #2ea04333; color: #2ea043; }
      .status-badge[data-status='Archived'] { background: #8b949e33; color: #8b949e; }
      .platform-icon {
        font-size: 14px;
      }
      .content-type {
        font-size: 11px;
        color: #8b949e;
      }
      .voice-dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        margin-left: auto;
      }
      .voice-green { background: #3fb950; }
      .voice-amber { background: #d29922; }
      .voice-red { background: #f85149; }
      .voice-none { background: #8b949e; }
      .card-title {
        font-size: 15px;
        font-weight: 600;
        color: #f0f6fc;
        margin: 0 0 8px;
        line-height: 1.3;
      }
      .card-meta {
        font-size: 12px;
        color: #8b949e;
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
        color: #8b949e;
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
      .pub-badge[data-status='Published'] { background: #2ea04333; color: #2ea043; }
      .pub-badge[data-status='Failed'] { background: #f8514933; color: #f85149; }
      .pub-badge[data-status='Pending'],
      .pub-badge[data-status='Formatting'] { background: #d2992233; color: #d29922; }
      .retry-btn {
        background: none; border: none; color: inherit; cursor: pointer;
        padding: 0; font-size: 11px; line-height: 1;
      }
      .card-actions {
        display: flex;
        gap: 4px;
        border-top: 1px solid #21262d;
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
