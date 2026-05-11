import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { IdeaSource, IdeaSourceType } from '../../../../../models/idea.model';

@Component({
  selector: 'app-source-card',
  standalone: true,
  imports: [DatePipe, ButtonModule, TagModule, TooltipModule],
  template: `
    <div class="source-card" data-testid="source-card">
      <div class="card-header">
        <div class="health-dot" [attr.data-health]="healthColor()" [pTooltip]="healthTooltip()"></div>
        <h3 class="source-name">{{ source().name }}</h3>
        <span class="type-badge" [attr.data-type]="source().type">{{ source().type }}</span>
      </div>

      @if (source().feedUrl) {
        <p class="feed-url" [pTooltip]="source().feedUrl!">{{ source().feedUrl }}</p>
      }

      <div class="card-meta">
        <span class="idea-count">{{ source().ideaCount }} ideas</span>
        <span class="category">{{ source().category }}</span>
      </div>

      @if (source().lastPolledAt) {
        <p class="meta-line">Last polled: {{ source().lastPolledAt | date: 'short' }}</p>
      }
      @if (source().lastError) {
        <p class="error-text" data-testid="source-error">{{ source().lastError }}</p>
      }

      <div class="card-actions">
        <p-button [icon]="source().isEnabled ? 'pi pi-pause' : 'pi pi-play'" severity="secondary"
          [text]="true" size="small" (onClick)="toggleEnabled.emit()" data-testid="toggle-btn"
          [pTooltip]="source().isEnabled ? 'Disable' : 'Enable'" />
        <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small"
          (onClick)="edit.emit()" data-testid="edit-btn" pTooltip="Edit" />
        <p-button icon="pi pi-trash" severity="danger" [text]="true" size="small"
          (onClick)="delete.emit()" data-testid="delete-btn" pTooltip="Delete" />
      </div>
    </div>
  `,
  styles: [
    `
      .source-card {
        background: #161b22;
        border: 1px solid transparent;
        border-radius: 8px;
        padding: 16px;
        transition: border-color 0.2s;
      }
      .source-card:hover {
        border-color: #30363d;
      }
      .card-header {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 8px;
      }
      .health-dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        flex-shrink: 0;
      }
      .health-dot[data-health='green'] { background: #3fb950; }
      .health-dot[data-health='yellow'] { background: #d29922; }
      .health-dot[data-health='red'] { background: #f85149; }
      .source-name {
        font-size: 15px;
        font-weight: 600;
        color: #f0f6fc;
        margin: 0;
        flex: 1;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .type-badge {
        font-size: 11px;
        font-weight: 600;
        padding: 2px 8px;
        border-radius: 12px;
        text-transform: uppercase;
      }
      .type-badge[data-type='RSS'] { background: #1f6feb33; color: #58a6ff; }
      .type-badge[data-type='API'] { background: #8957e533; color: #bc8cff; }
      .type-badge[data-type='Manual'] { background: #8b949e33; color: #8b949e; }
      .type-badge[data-type='AIGenerated'] { background: #23863633; color: #3fb950; }
      .feed-url {
        font-size: 12px;
        color: #8b949e;
        margin: 0 0 8px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .card-meta {
        display: flex;
        gap: 12px;
        font-size: 13px;
        color: #8b949e;
        margin-bottom: 8px;
      }
      .idea-count {
        font-weight: 600;
        color: #c9d1d9;
      }
      .meta-line {
        font-size: 12px;
        color: #8b949e;
        margin: 0 0 4px;
      }
      .error-text {
        font-size: 12px;
        color: #f85149;
        margin: 0 0 8px;
      }
      .card-actions {
        display: flex;
        gap: 4px;
        border-top: 1px solid #21262d;
        padding-top: 8px;
        margin-top: 8px;
      }
    `,
  ],
})
export class SourceCardComponent {
  readonly source = input.required<IdeaSource>();
  readonly edit = output<void>();
  readonly delete = output<void>();
  readonly toggleEnabled = output<void>();

  healthColor(): string {
    const s = this.source();
    if (!s.isEnabled) return 'red';
    if (s.consecutiveFailures >= 3) return 'red';
    if (s.consecutiveFailures >= 1) return 'yellow';
    return 'green';
  }

  healthTooltip(): string {
    const s = this.source();
    if (!s.isEnabled) return 'Source disabled';
    if (s.consecutiveFailures >= 3) return `${s.consecutiveFailures} consecutive failures`;
    if (s.consecutiveFailures >= 1) return `${s.consecutiveFailures} recent failure(s)`;
    return 'Healthy';
  }
}
