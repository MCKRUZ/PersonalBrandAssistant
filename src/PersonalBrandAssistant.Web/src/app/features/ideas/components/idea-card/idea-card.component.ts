import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

@Component({
  selector: 'app-idea-card',
  standalone: true,
  imports: [DatePipe, ButtonModule, TagModule],
  template: `
    <div class="idea-card" data-testid="idea-card">
      @if (idea().thumbnailUrl) {
        <img [src]="idea().thumbnailUrl" [alt]="idea().title" class="card-thumbnail" />
      }
      <div class="card-body">
        <div class="card-header">
          <span class="status-badge" [attr.data-status]="idea().status">
            {{ idea().status }}
          </span>
          <span class="source-name">{{ idea().sourceName }}</span>
          <span class="detected-at">{{ idea().detectedAt | date: 'shortDate' }}</span>
        </div>
        @if (idea().url) {
          <a [href]="idea().url" target="_blank" rel="noopener noreferrer" class="card-title-link">
            <h3 class="card-title">{{ idea().title }}</h3>
          </a>
        } @else {
          <h3 class="card-title">{{ idea().title }}</h3>
        }
        @if (idea().summary || idea().description) {
          <p class="card-summary">{{ truncate((idea().summary || idea().description)!, 120) }}</p>
        }
        @if (idea().tags.length > 0) {
          <div class="card-tags">
            @for (tag of idea().tags.slice(0, 3); track tag) {
              <p-tag [value]="tag" severity="secondary" />
            }
            @if (idea().tags.length > 3) {
              <span class="more-tags">+{{ idea().tags.length - 3 }}</span>
            }
          </div>
        }
        <div class="card-actions">
          <p-button
            icon="pi pi-bookmark"
            severity="secondary"
            [text]="true"
            size="small"
            (onClick)="save.emit(idea().id)"
            data-testid="save-btn"
            pTooltip="Save" />
          <p-button
            icon="pi pi-times"
            severity="secondary"
            [text]="true"
            size="small"
            (onClick)="dismiss.emit(idea().id)"
            data-testid="dismiss-btn"
            pTooltip="Dismiss" />
          <p-button
            icon="pi pi-pencil"
            severity="secondary"
            [text]="true"
            size="small"
            (onClick)="createContent.emit(idea().id)"
            data-testid="create-content-btn"
            pTooltip="Create Content" />
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .idea-card {
        background: #161b22;
        border: 1px solid transparent;
        border-radius: 8px;
        overflow: hidden;
        transition: border-color 0.2s;
      }
      .idea-card:hover {
        border-color: #30363d;
      }
      .card-thumbnail {
        width: 100%;
        height: 140px;
        object-fit: cover;
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
      .status-badge[data-status='New'] {
        background: #1f6feb33;
        color: #58a6ff;
      }
      .status-badge[data-status='Saved'] {
        background: #23863633;
        color: #3fb950;
      }
      .status-badge[data-status='Used'] {
        background: #8b949e33;
        color: #8b949e;
      }
      .status-badge[data-status='Dismissed'] {
        background: #f8514933;
        color: #f85149;
      }
      .card-title-link {
        text-decoration: none;
      }
      .card-title-link:hover .card-title {
        color: #58a6ff;
      }
      .card-title {
        font-size: 15px;
        font-weight: 600;
        color: #f0f6fc;
        margin: 0 0 8px;
        line-height: 1.3;
        transition: color 0.15s;
      }
      .card-summary {
        font-size: 13px;
        color: #8b949e;
        margin: 0 0 8px;
        line-height: 1.4;
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
      .card-actions {
        display: flex;
        gap: 4px;
        border-top: 1px solid #21262d;
        padding-top: 8px;
      }
    `,
  ],
})
export class IdeaCardComponent {
  readonly idea = input.required<Idea>();
  readonly save = output<string>();
  readonly dismiss = output<string>();
  readonly createContent = output<string>();

  truncate(text: string, maxLength: number): string {
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
  }
}
