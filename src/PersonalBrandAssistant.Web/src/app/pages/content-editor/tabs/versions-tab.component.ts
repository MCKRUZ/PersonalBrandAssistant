import { Component, input } from '@angular/core';
import { ContentItem } from '../../../core/models/content.model';

@Component({
  selector: 'app-versions-tab',
  standalone: true,
  template: `
    <div class="versions-list">
      @if (versions().length === 0) {
        <div class="empty-versions">
          <p>Version history will appear here after saves</p>
        </div>
      } @else {
        @for (ver of versions(); track ver.version) {
          <div class="version-item">
            <span class="version-number">v{{ ver.version }}</span>
            <span class="version-date">{{ formatDate(ver.updatedAt) }}</span>
            <span class="version-length">{{ ver.body.length }} chars</span>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    @use '../../../../styles/variables' as *;

    .versions-list {
      display: flex;
      flex-direction: column;
    }

    .version-item {
      display: flex;
      align-items: center;
      gap: $space-3;
      padding: $space-3;
      border-bottom: 1px solid $surface-border;
      cursor: pointer;
      transition: background 150ms;

      &:hover { background: $surface-hover; }
    }

    .version-number {
      font-family: $font-mono;
      font-size: 0.8125rem;
      font-weight: 600;
      color: $brand-primary;
      min-width: 32px;
    }

    .version-date {
      flex: 1;
      font-size: 0.8125rem;
      color: $text-secondary;
    }

    .version-length {
      font-size: 0.75rem;
      color: $text-muted;
      font-family: $font-mono;
    }

    .empty-versions {
      text-align: center;
      padding: $space-6;
      color: $text-muted;
      font-size: 0.875rem;

      p { margin: 0; }
    }
  `],
})
export class VersionsTabComponent {
  readonly versions = input.required<readonly ContentItem[]>();

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleString(undefined, {
      month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }
}
