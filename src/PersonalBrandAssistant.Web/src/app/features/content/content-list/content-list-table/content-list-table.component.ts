import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Content } from '../../models/content.model';
import { formatContentType, TYPE_GLYPH, relativeTime } from '../content-display.utils';
import { StatusTagComponent } from '../../shared/status-tag.component';
import { VoiceScoreRingComponent } from '../../shared/voice-score-ring.component';
import { PlatformDotComponent } from '../../shared/platform-dot.component';

/**
 * Card-wrapped table view. Columns: Status · Title (+tag line) · Type · Platforms · Voice · Updated.
 * No Actions column — row click opens the detail drawer via `openRow`.
 */
@Component({
  selector: 'app-content-list-table',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StatusTagComponent, VoiceScoreRingComponent, PlatformDotComponent],
  template: `
    <div class="table-card" data-testid="content-list-table">
      <div class="list-header">
        <span class="col-status">Status</span>
        <span class="col-title">Title</span>
        <span class="col-type">Type</span>
        <span class="col-platform">Platforms</span>
        <span class="col-voice">Voice</span>
        <span class="col-date">Updated</span>
      </div>

      @for (content of contents(); track content.id) {
        <div
          class="list-row"
          data-testid="content-row"
          [attr.data-id]="content.id"
          (click)="openRow.emit(content.id)">
          <span class="col-status">
            <app-status-tag [status]="content.status" />
          </span>
          <span class="col-title">
            <span class="title-text">{{ content.title }}</span>
            @if (content.tags.length > 0) {
              <span class="tag-line">{{ tagLine(content) }}</span>
            }
          </span>
          <span class="col-type">
            <span class="glyph">{{ glyph(content) }}</span>
            {{ formatType(content.contentType) }}
          </span>
          <span class="col-platform">
            @for (p of content.targetPlatforms; track p) {
              <app-platform-dot [platform]="p" variant="dot" />
            }
          </span>
          <span class="col-voice">
            <app-voice-score-ring [score]="content.voiceScore" [size]="30" />
          </span>
          <span class="col-date">{{ updated(content) }}</span>
        </div>
      } @empty {
        <div class="empty-state" data-testid="empty-state">
          <p>No content found</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .table-card {
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: var(--r);
      overflow: hidden;
    }
    .list-header,
    .list-row {
      display: grid;
      grid-template-columns: 120px 1fr 130px 110px 70px 90px;
      gap: 12px;
      align-items: center;
    }
    .list-header {
      padding: 12px 18px;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.4px;
      color: var(--text-secondary);
      text-transform: uppercase;
      border-bottom: 1px solid var(--surface-border);
    }
    .list-row {
      padding: 13px 18px;
      font-size: 13px;
      color: var(--text-primary);
      border-bottom: 1px solid var(--surface-border);
      cursor: pointer;
      transition: background 0.14s;
    }
    .list-row:last-child {
      border-bottom: none;
    }
    .list-row:hover {
      background: var(--surface-elevated);
    }
    .col-title {
      display: flex;
      flex-direction: column;
      gap: 3px;
      overflow: hidden;
    }
    .title-text {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .tag-line {
      font-family: var(--font-mono);
      font-size: 11px;
      color: var(--text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .col-type {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      color: var(--text-secondary);
      font-size: 12px;
    }
    .col-type .glyph {
      font-size: 14px;
      color: var(--text-secondary);
    }
    .col-platform {
      display: inline-flex;
      align-items: center;
      gap: 5px;
    }
    .col-date {
      font-family: var(--font-mono);
      font-size: 11px;
      color: var(--text-muted);
      text-align: right;
    }
    .empty-state {
      text-align: center;
      padding: 48px 16px;
      color: var(--text-secondary);
    }
  `],
})
export class ContentListTableComponent {
  readonly contents = input.required<Content[]>();
  /** Row click -> orchestrator opens the detail drawer with this id. */
  readonly openRow = output<string>();

  readonly formatType = formatContentType;

  glyph(content: Content): string {
    return TYPE_GLYPH[content.contentType];
  }

  tagLine(content: Content): string {
    return content.tags.map((t) => `#${t}`).join(' ');
  }

  updated(content: Content): string {
    return relativeTime(content.scheduledAt ?? content.updatedAt);
  }
}
