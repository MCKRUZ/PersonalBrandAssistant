import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { RelativeTimePipe } from '../../../../shared/pipes/relative-time.pipe';
import { SavedItemsStore } from '../../store/saved-items.store';
import { SOURCE_COLORS, SOURCE_ICONS } from '../../models/news.model';

@Component({
  selector: 'app-saved-items',
  standalone: true,
  imports: [ButtonModule, SkeletonModule, EmptyStateComponent, RelativeTimePipe],
  template: `
    @if (store.loading()) {
      <div class="flex flex-column gap-3">
        @for (i of skeletonItems; track i) {
          <p-skeleton height="100px" />
        }
      </div>
    } @else if (store.items().length === 0) {
      <app-empty-state message="No saved articles yet. Bookmark articles from the Feed tab." icon="pi pi-bookmark" />
    } @else {
      <div class="flex flex-column gap-3">
        @for (item of store.items(); track item.id) {
          <article class="saved-card" [style.--source-color]="getSourceColor(item.sourceName)">
            <div class="saved-card__accent"></div>
            <div class="saved-card__body">
              <div class="saved-card__top">
                <div class="saved-card__source">
                  <i [class]="getSourceIcon(item.sourceName)" [style.color]="getSourceColor(item.sourceName)"></i>
                  <span>{{ item.sourceName }}</span>
                </div>
                <span class="saved-card__time">{{ item.createdAt | relativeTime }}</span>
              </div>

              <a class="saved-card__title" [href]="item.url" target="_blank" rel="noopener noreferrer">
                {{ item.title }}
              </a>

              <div class="saved-card__actions">
                <p-button icon="pi pi-pencil" label="Create Content" size="small" [text]="true"
                  (onClick)="createContent(item.title)" />
                <p-button icon="pi pi-trash" label="Remove" size="small" [text]="true"
                  severity="danger" (onClick)="store.remove(item.id)" />
              </div>
            </div>
          </article>
        }
      </div>
    }
  `,
  styles: `
    .saved-card {
      --source-color: #6b7280;
      display: flex;
      border-radius: 12px;
      background: var(--p-surface-800, #27272a);
      border: 1px solid var(--p-surface-700, #3f3f46);
      overflow: hidden;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
      transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1),
                  box-shadow 0.25s cubic-bezier(0.4, 0, 0.2, 1);

      &:hover {
        transform: translateY(-2px);
        box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2);
      }

      &__accent {
        width: 4px;
        flex-shrink: 0;
        background: var(--source-color);
      }

      &__body {
        flex: 1;
        padding: 1rem 1.25rem;
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
      }

      &__top {
        display: flex;
        justify-content: space-between;
        align-items: center;
      }

      &__source {
        display: flex;
        align-items: center;
        gap: 0.4rem;
        font-size: 0.8rem;
        font-weight: 700;
      }

      &__time {
        font-size: 0.75rem;
        color: var(--p-text-muted-color);
      }

      &__title {
        font-size: 1rem;
        font-weight: 700;
        color: var(--p-text-color);
        text-decoration: none;
        transition: color 0.15s;

        &:hover { color: var(--p-primary-color); }
      }

      &__actions {
        display: flex;
        gap: 0.5rem;
        justify-content: flex-end;
      }
    }

    @media (max-width: 768px) {
      .saved-card {
        &:hover { transform: none; }
        &__body { padding: 0.75rem 0.85rem; gap: 0.35rem; }
        &__title { font-size: 0.9rem; }
        &__source { font-size: 0.72rem; }
      }
    }
  `,
})
export class SavedItemsComponent implements OnInit {
  readonly store = inject(SavedItemsStore);
  readonly skeletonItems = Array.from({ length: 3 }, (_, i) => i);
  private readonly router = inject(Router);

  ngOnInit() {
    this.store.load(undefined);
  }

  getSourceColor(sourceName: string): string {
    return SOURCE_COLORS[sourceName] ?? '#6b7280';
  }

  getSourceIcon(sourceName: string): string {
    return SOURCE_ICONS[sourceName] ?? 'pi pi-rss';
  }

  createContent(topic: string) {
    this.router.navigate(['/content/new'], { queryParams: { topic } });
  }
}
