import { Component, inject, input, output, computed, signal, ViewEncapsulation } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';
import { RelativeTimePipe } from '../../../../shared/pipes/relative-time.pipe';
import { NewsFeedItem, CATEGORY_COLORS, CATEGORY_ICONS } from '../../models/news.model';

@Component({
  selector: 'app-news-feed-item',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [RelativeTimePipe],
  template: `
    <article class="feed-card" [style.--source-color]="categoryColor()">
      <div class="feed-card__accent"></div>

      <div class="feed-card__body">
        <div class="feed-card__meta">
          <span class="feed-card__category" [style.--cat-color]="categoryColor()">
            <i [class]="categoryIcon()"></i> {{ item().sourceCategory }}
          </span>
          <span class="feed-card__source-name">{{ item().sourceName }}</span>
          <span class="feed-card__separator">&middot;</span>
          <span class="feed-card__time">{{ item().createdAt | relativeTime }}</span>
        </div>

        <a
          class="feed-card__title"
          [href]="item().url"
          target="_blank"
          rel="noopener noreferrer"
        >
          {{ item().title }}
        </a>

        @if (item().description) {
          <p class="feed-card__summary">{{ item().description }}</p>
        }

        <div class="feed-card__actions">
          <button class="feed-card__action" [class.feed-card__action--active]="item().saved" (click)="bookmarked.emit()">
            <i class="pi pi-bookmark"></i> Save
          </button>
          <button class="feed-card__action" (click)="openLink()">
            <i class="pi pi-external-link"></i> Read
          </button>
          @if (item().summary) {
            <button class="feed-card__action feed-card__action--analyze" [class.feed-card__action--active]="showSummary()" (click)="toggleSummary()">
              <i class="pi pi-sparkles"></i> {{ showSummary() ? 'Hide' : 'Summary' }}
            </button>
          }
          <button class="feed-card__action feed-card__action--dismiss" (click)="dismissed.emit()">
            <i class="pi pi-times"></i> Dismiss
          </button>
        </div>

        @if (item().summary && showSummary()) {
          <div class="analysis-panel" [innerHTML]="renderedSummary()"></div>
        }
      </div>
    </article>
  `,
  styles: `
    .feed-card {
      --source-color: #6b7280;
      position: relative;
      display: flex;
      border-radius: 12px;
      background: var(--p-surface-800, #27272a);
      border: 1px solid var(--p-surface-700, #3f3f46);
      overflow: hidden;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
      transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1),
                  box-shadow 0.25s cubic-bezier(0.4, 0, 0.2, 1),
                  border-color 0.25s ease;
    }
    .feed-card:hover {
      transform: translateY(-2px);
      border-color: color-mix(in srgb, var(--source-color) 40%, transparent);
      box-shadow:
        0 8px 24px rgba(0, 0, 0, 0.35),
        0 0 0 1px color-mix(in srgb, var(--source-color) 15%, transparent),
        inset 0 1px 0 rgba(255, 255, 255, 0.06);
    }
    .feed-card__accent {
      width: 4px;
      flex-shrink: 0;
      background: var(--source-color);
      border-radius: 12px 0 0 12px;
    }
    .feed-card__body {
      flex: 1;
      padding: 1rem 1.25rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      min-width: 0;
    }

    .feed-card__meta {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.8rem;
    }
    .feed-card__category {
      display: inline-flex;
      align-items: center;
      gap: 0.3rem;
      font-size: 0.72rem;
      font-weight: 700;
      padding: 0.15rem 0.6rem;
      border-radius: 10px;
      color: var(--cat-color, #6b7280);
      background: color-mix(in srgb, var(--cat-color, #6b7280) 12%, transparent);
      letter-spacing: 0.02em;
      white-space: nowrap;
    }
    .feed-card__category i {
      font-size: 0.65rem;
    }
    .feed-card__source-name {
      font-weight: 700;
      letter-spacing: 0.02em;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .feed-card__separator {
      color: var(--p-text-muted-color);
      flex-shrink: 0;
    }
    .feed-card__time {
      color: var(--p-text-muted-color);
      white-space: nowrap;
    }

    .feed-card__title {
      font-size: 1rem;
      font-weight: 700;
      color: var(--p-text-color);
      text-decoration: none;
      line-height: 1.4;
      letter-spacing: -0.01em;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      transition: color 0.15s ease;
    }
    .feed-card__title:hover {
      color: var(--p-primary-color);
    }

    .feed-card__summary {
      font-size: 0.85rem;
      color: var(--p-text-muted-color);
      margin: 0;
      line-height: 1.55;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .feed-card__actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 0.15rem;
    }
    .feed-card__action {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      padding: 0.35rem 0.75rem;
      border: 1px solid transparent;
      border-radius: 8px;
      background: transparent;
      color: var(--p-text-muted-color);
      font-size: 0.78rem;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s ease;
    }
    .feed-card__action:hover {
      background: rgba(255, 255, 255, 0.06);
      color: var(--p-text-color);
      border-color: rgba(255, 255, 255, 0.1);
    }
    .feed-card__action--active {
      color: #eab308;
    }
    .feed-card__action--active:hover {
      color: #eab308;
    }
    .feed-card__action--dismiss:hover {
      color: #ef4444;
      border-color: rgba(239, 68, 68, 0.2);
    }
    .feed-card__action--analyze {
      color: #a78bfa;
    }
    .feed-card__action--analyze:hover {
      color: #c4b5fd;
      border-color: rgba(167, 139, 250, 0.2);
    }
    .feed-card__action i {
      font-size: 0.85rem;
    }

    @media (max-width: 768px) {
      .feed-card__body { padding: 0.75rem 0.85rem; gap: 0.35rem; }
      .feed-card__meta { gap: 0.35rem; font-size: 0.72rem; }
      .feed-card__title { font-size: 0.9rem; }
      .feed-card__summary { font-size: 0.8rem; -webkit-line-clamp: 1; }
      .feed-card__actions { gap: 0.25rem; }
      .feed-card__action {
        padding: 0.4rem;
        font-size: 0;
        border-radius: 6px;
      }
      .feed-card__action i { font-size: 0.9rem; }
      .feed-card:hover { transform: none; }
    }
  `,
})
export class NewsFeedItemComponent {
  private readonly sanitizer = inject(DomSanitizer);

  item = input.required<NewsFeedItem>();
  bookmarked = output<void>();
  dismissed = output<void>();

  readonly showSummary = signal(false);
  readonly categoryColor = computed(() => CATEGORY_COLORS[this.item().sourceCategory] ?? '#6b7280');
  readonly categoryIcon = computed(() => CATEGORY_ICONS[this.item().sourceCategory] ?? 'pi pi-th-large');
  readonly renderedSummary = computed((): SafeHtml => {
    const md = this.item().summary;
    if (!md) return '';
    const html = marked.parse(md, { async: false }) as string;
    return this.sanitizer.bypassSecurityTrustHtml(html);
  });

  toggleSummary() {
    this.showSummary.update((v) => !v);
  }

  openLink() {
    const url = this.item().url;
    if (url) window.open(url, '_blank', 'noopener,noreferrer');
  }
}
