import { Component, input } from '@angular/core';
import { ContentItem } from '../../../core/models/content.model';

@Component({
  selector: 'app-preview-tab',
  standalone: true,
  template: `
    <div class="preview-container">
      @if (content(); as c) {
        <div class="preview-card" [class]="'preview-' + c.platform.toLowerCase()">
          <div class="preview-header">
            <div class="preview-avatar"></div>
            <div class="preview-meta">
              <span class="preview-name">Matthew Kruczek</span>
              <span class="preview-platform">{{ c.platform }}</span>
            </div>
          </div>
          <div class="preview-title">{{ c.title }}</div>
          <div class="preview-body">{{ truncate(c.body, 300) }}</div>
        </div>
      }
    </div>
  `,
  styles: [`
    @use '../../../../styles/variables' as *;

    .preview-container { padding: $space-2 0; }

    .preview-card {
      background: $surface-card;
      border: 1px solid $surface-border;
      border-radius: 8px;
      padding: $space-4;
    }

    .preview-header {
      display: flex;
      align-items: center;
      gap: $space-3;
      margin-bottom: $space-3;
    }

    .preview-avatar {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      background: $surface-hover;
    }

    .preview-meta {
      display: flex;
      flex-direction: column;
    }

    .preview-name {
      font-size: 0.875rem;
      font-weight: 600;
      color: $text-primary;
    }

    .preview-platform {
      font-size: 0.75rem;
      color: $text-muted;
    }

    .preview-title {
      font-size: 1rem;
      font-weight: 600;
      color: $text-primary;
      margin-bottom: $space-2;
    }

    .preview-body {
      font-size: 0.875rem;
      color: $text-secondary;
      line-height: 1.5;
      white-space: pre-wrap;
    }
  `],
})
export class PreviewTabComponent {
  readonly content = input.required<ContentItem>();

  truncate(text: string, max: number): string {
    if (!text || text.length <= max) return text ?? '';
    return text.slice(0, max) + '...see more';
  }
}
