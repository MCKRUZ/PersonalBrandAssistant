import { Component, inject, input, output, computed, signal, ViewEncapsulation } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';
import { RelativeTimePipe } from '../../../../shared/pipes/relative-time.pipe';
import { NewsFeedItem, CATEGORY_COLORS, CATEGORY_ICONS } from '../../models/news.model';

@Component({
  selector: 'app-news-feed-video-card',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [DecimalPipe, RelativeTimePipe],
  template: `
    <article class="video-card" [class.video-card--no-thumb]="thumbError()" [style.--source-color]="categoryColor()">
      <div class="video-card__accent"></div>

      @if (!thumbError()) {
        <a class="video-card__thumb" [href]="item().url" target="_blank" rel="noopener noreferrer">
          <img
            [src]="item().thumbnailUrl"
            [alt]="item().title"
            (error)="thumbError.set(true)"
            loading="lazy"
          />
          <div class="video-card__play">
            <i class="pi pi-play-circle"></i>
          </div>
        </a>
      }

      <div class="video-card__body">
        <!-- Row 1: Category + Source + Time ... Score -->
        <div class="video-card__meta">
          <span class="video-card__category" [style.--cat-color]="categoryColor()">
            <i [class]="categoryIcon()"></i> {{ item().sourceCategory ?? 'Uncategorized' }}
          </span>
          <span class="video-card__source-name">{{ item().sourceName ?? item().source }}</span>
          <span class="video-card__separator">&middot;</span>
          <span class="video-card__time">{{ item().createdAt | relativeTime }}</span>
          <div class="video-card__score" [attr.data-level]="scoreLevel()">
            {{ (item().relevanceScore * 100) | number:'1.0-0' }}
          </div>
        </div>

        <!-- Row 2: Title (2-line clamp) -->
        <a
          class="video-card__title"
          [href]="item().url"
          target="_blank"
          rel="noopener noreferrer"
        >
          {{ item().title }}
        </a>

        <!-- Row 3: Summary -->
        @if (item().description) {
          <p class="video-card__summary">{{ item().description }}</p>
        }

        <!-- Row 4: Triage actions -->
        <div class="video-card__actions">
          <button class="video-card__action" [class.video-card__action--active]="item().saved" (click)="bookmarked.emit()">
            <i class="pi pi-bookmark"></i> Save
          </button>
          <button class="video-card__action" (click)="openLink()">
            <i class="pi pi-external-link"></i> Read
          </button>
          @if (!item().summary && item().url) {
            <button class="video-card__action video-card__action--analyze" [disabled]="analyzing()" (click)="analyzed.emit()">
              @if (analyzing()) {
                <i class="pi pi-spin pi-spinner"></i> Analyzing...
              } @else {
                <i class="pi pi-sparkles"></i> Analyze
              }
            </button>
          } @else if (item().summary) {
            <button class="video-card__action video-card__action--analyze" [class.video-card__action--active]="showSummary()" (click)="toggleSummary()">
              <i class="pi pi-sparkles"></i> {{ showSummary() ? 'Hide' : 'Analysis' }}
            </button>
          }
          @if (item().summary) {
            <button class="video-card__action video-card__action--idea" (click)="newIdea.emit(item())">
              <i class="pi pi-lightbulb"></i> New Idea
            </button>
            <button class="video-card__action video-card__action--copy" (click)="copyPrompt()">
              <i [class]="copied() ? 'pi pi-check' : 'pi pi-copy'"></i> {{ copied() ? 'Copied' : 'Copy Prompt' }}
            </button>
          }
          <button class="video-card__action video-card__action--dismiss" (click)="dismissed.emit()">
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
    .video-card {
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
    .video-card:hover {
      transform: translateY(-2px);
      border-color: color-mix(in srgb, var(--source-color) 40%, transparent);
      box-shadow:
        0 8px 24px rgba(0, 0, 0, 0.35),
        0 0 0 1px color-mix(in srgb, var(--source-color) 15%, transparent),
        inset 0 1px 0 rgba(255, 255, 255, 0.06);
    }
    .video-card__accent {
      width: 4px;
      flex-shrink: 0;
      background: var(--source-color);
      border-radius: 12px 0 0 12px;
    }
    .video-card__thumb {
      position: relative;
      width: 160px;
      flex-shrink: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(0, 0, 0, 0.3);
      cursor: pointer;
      text-decoration: none;
      overflow: hidden;
    }
    .video-card__thumb img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }
    .video-card__play {
      position: absolute;
      inset: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(0, 0, 0, 0.25);
      transition: background 0.2s ease;
    }
    .video-card__play i {
      font-size: 2rem;
      color: rgba(255, 255, 255, 0.85);
      text-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
      transition: transform 0.2s ease;
    }
    .video-card__play:hover {
      background: rgba(0, 0, 0, 0.1);
    }
    .video-card__play:hover i {
      transform: scale(1.15);
    }
    .video-card__body {
      flex: 1;
      padding: 1rem 1.25rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      min-width: 0;
    }

    /* Row 1: Meta */
    .video-card__meta {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.8rem;
    }
    .video-card__category {
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
    .video-card__category i {
      font-size: 0.65rem;
    }
    .video-card__source-name {
      font-weight: 700;
      letter-spacing: 0.02em;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .video-card__separator {
      color: var(--p-text-muted-color);
      flex-shrink: 0;
    }
    .video-card__time {
      color: var(--p-text-muted-color);
      white-space: nowrap;
    }
    .video-card__score {
      margin-left: auto;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.75rem;
      font-weight: 800;
      color: white;
      flex-shrink: 0;
    }
    .video-card__score[data-level="high"] { background: #22c55e; box-shadow: 0 0 12px rgba(34, 197, 94, 0.3); }
    .video-card__score[data-level="medium"] { background: #eab308; box-shadow: 0 0 12px rgba(234, 179, 8, 0.3); }
    .video-card__score[data-level="low"] { background: #ef4444; box-shadow: 0 0 12px rgba(239, 68, 68, 0.3); }

    /* Row 2: Title */
    .video-card__title {
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
    .video-card__title:hover {
      color: var(--p-primary-color);
    }

    /* Row 3: Summary */
    .video-card__summary {
      font-size: 0.85rem;
      color: var(--p-text-muted-color);
      margin: 0;
      line-height: 1.55;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    /* Row 4: Triage actions — left-aligned */
    .video-card__actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 0.15rem;
    }
    .video-card__action {
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
    .video-card__action:hover {
      background: rgba(255, 255, 255, 0.06);
      color: var(--p-text-color);
      border-color: rgba(255, 255, 255, 0.1);
    }
    .video-card__action--active {
      color: #eab308;
    }
    .video-card__action--active:hover {
      color: #eab308;
    }
    .video-card__action--dismiss:hover {
      color: #ef4444;
      border-color: rgba(239, 68, 68, 0.2);
    }
    .video-card__action--analyze {
      color: #a78bfa;
    }
    .video-card__action--analyze:hover {
      color: #c4b5fd;
      border-color: rgba(167, 139, 250, 0.2);
    }
    .video-card__action--idea {
      color: #fb923c;
    }
    .video-card__action--idea:hover {
      color: #fdba74;
      border-color: rgba(251, 146, 60, 0.2);
    }
    .video-card__action--copy {
      color: #60a5fa;
    }
    .video-card__action--copy:hover {
      color: #93c5fd;
      border-color: rgba(96, 165, 250, 0.2);
    }
    .video-card__action i {
      font-size: 0.85rem;
    }

  `,
})
export class NewsFeedVideoCardComponent {
  private readonly sanitizer = inject(DomSanitizer);

  item = input.required<NewsFeedItem>();
  analyzing = input<boolean>(false);
  bookmarked = output<void>();
  dismissed = output<void>();
  analyzed = output<void>();
  newIdea = output<NewsFeedItem>();

  readonly thumbError = signal(false);
  readonly showSummary = signal(false);
  readonly copied = signal(false);
  readonly categoryColor = computed(() => CATEGORY_COLORS[this.item().sourceCategory ?? ''] ?? '#6b7280');
  readonly categoryIcon = computed(() => CATEGORY_ICONS[this.item().sourceCategory ?? ''] ?? 'pi pi-th-large');
  readonly scoreLevel = computed(() => {
    const s = this.item().relevanceScore;
    return s >= 0.6 ? 'high' : s >= 0.3 ? 'medium' : 'low';
  });
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

  copyPrompt() {
    const i = this.item();
    const prompt = [
      `# ${i.title}`,
      '',
      `**Source:** ${i.sourceName ?? i.source} (${i.sourceCategory ?? 'Uncategorized'})`,
      `**Topic:** ${i.topic}`,
      `**Relevance:** ${Math.round(i.relevanceScore * 100)}%`,
      i.url ? `**URL:** ${i.url}` : '',
      '',
      '## Analysis',
      i.summary ?? '',
    ].filter(Boolean).join('\n');

    navigator.clipboard.writeText(prompt);
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 2000);
  }
}
