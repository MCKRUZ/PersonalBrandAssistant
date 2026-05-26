import { Component, computed, inject, OnInit, ViewEncapsulation, ElementRef, Injector, afterNextRender, ChangeDetectorRef } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { Tooltip } from 'primeng/tooltip';
import { NewsStore } from '../../store/news.store';
import { NewsFeedFiltersComponent } from './news-feed-filters.component';
import { NewsFeedItemComponent } from './news-feed-item.component';
import { NewsFeedVideoCardComponent } from './news-feed-video-card.component';
import { CATEGORY_COLORS, CATEGORY_ICONS, CategoryGroup, SOURCE_COLORS, SOURCE_ICONS, GroupMode } from '../../models/news.model';

@Component({
  selector: 'app-news-feed',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [ButtonModule, SkeletonModule, Tooltip, NewsFeedFiltersComponent, NewsFeedItemComponent, NewsFeedVideoCardComponent],
  template: `
    <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1.25rem;">
      <app-news-feed-filters style="flex: 1;" />
      <p-button
        [icon]="allCollapsed() ? 'pi pi-angle-double-down' : 'pi pi-angle-double-up'"
        [text]="true" size="small"
        [pTooltip]="allCollapsed() ? 'Expand all' : 'Collapse all'"
        (onClick)="toggleAllCollapse()"
      />
      <p-button
        icon="pi pi-refresh" label="Refresh" [text]="true" size="small"
        [loading]="store.refreshing()" (onClick)="store.refresh(undefined)"
      />
    </div>

    @if (store.loading()) {
      <div style="display: flex; flex-direction: column; gap: 0.75rem;">
        @for (i of skeletonItems; track i) {
          <p-skeleton height="100px" borderRadius="12px" />
        }
      </div>
    } @else if (groups().length === 0) {
      <div style="
        border: 1px dashed rgba(139,92,246,0.2); border-radius: 16px;
        padding: 3rem 2rem; text-align: center;
        background: linear-gradient(135deg, rgba(139,92,246,0.03) 0%, transparent 100%);
      ">
        <div style="
          display: flex; justify-content: center; gap: 1.5rem;
          margin-bottom: 2rem; flex-wrap: wrap;
        ">
          @for (cat of categoryStats; track cat.name) {
            <div style="
              display: flex; flex-direction: column; align-items: center; gap: 0.4rem;
              padding: 1rem 1.25rem; border-radius: 12px;
              background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06);
              min-width: 100px;
            ">
              <div [style]="'width: 36px; height: 36px; border-radius: 10px; display: flex; align-items: center; justify-content: center; background: ' + cat.color + '20;'">
                <i [class]="cat.icon" [style]="'font-size: 1rem; color: ' + cat.color + ';'"></i>
              </div>
              <span style="font-size: 0.75rem; font-weight: 600; color: rgba(255,255,255,0.6);">{{ cat.name }}</span>
              <span style="font-size: 0.65rem; color: rgba(255,255,255,0.25);">{{ cat.status }}</span>
            </div>
          }
        </div>

        <div style="
          width: 56px; height: 56px; border-radius: 50%; margin: 0 auto 1rem;
          display: flex; align-items: center; justify-content: center;
          background: rgba(139,92,246,0.08); border: 1px solid rgba(139,92,246,0.15);
        ">
          <i class="pi pi-inbox" style="font-size: 1.5rem; color: rgba(139,92,246,0.5);"></i>
        </div>
        <p style="margin: 0 0 0.25rem; font-size: 0.95rem; font-weight: 600; color: rgba(255,255,255,0.5);">
          No articles match your filters
        </p>
        <p style="margin: 0 0 1.5rem; font-size: 0.8rem; color: rgba(255,255,255,0.25);">
          Try adjusting your category filters or time window, or hit Refresh to pull new articles
        </p>
        <p-button
          icon="pi pi-refresh" label="Refresh Sources" [outlined]="true" size="small"
          [loading]="store.refreshing()" (onClick)="store.refresh(undefined)"
        />
      </div>
    } @else {
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        @for (group of groups(); track $index) {
          <div class="category-section" [attr.data-category]="group.category">
            <div class="category-section__header" (click)="store.toggleCategoryCollapse(group.category)">
              <i class="category-section__chevron pi"
                 [class.pi-chevron-down]="!isCollapsed(group.category)"
                 [class.pi-chevron-right]="isCollapsed(group.category)"></i>
              <i [class]="categoryIcon(group.category)" [style.color]="categoryColor(group.category)"></i>
              <span class="category-section__name">{{ group.category }}</span>
              <span class="category-section__count">{{ group.items.length }}</span>
            </div>
            @if (!isCollapsed(group.category)) {
              <div class="category-section__items">
                @for (sg of group.sourceGroups; track sg.sourceName) {
                  @if (group.sourceGroups.length > 1) {
                    <div class="source-section">
                      <div class="source-section__header" (click)="toggleSource(group.category, sg.sourceName)">
                        <i class="source-section__chevron pi"
                           [class.pi-chevron-down]="!isSourceCollapsed(group.category, sg.sourceName)"
                           [class.pi-chevron-right]="isSourceCollapsed(group.category, sg.sourceName)"></i>
                        <i [class]="sourceIcon(sg.sourceName)" [style.color]="sourceColor(sg.sourceName)"></i>
                        <span class="source-section__name">{{ sg.sourceName }}</span>
                        <span class="source-section__count">{{ sg.items.length }}</span>
                      </div>
                      @if (!isSourceCollapsed(group.category, sg.sourceName)) {
                        <div class="source-section__items">
                          @for (item of sg.items; track item.id) {
                            @if (item.thumbnailUrl) {
                              <app-news-feed-video-card
                                [item]="item"
                                (bookmarked)="store.toggleSaved(item.id)"
                                (dismissed)="onDismiss(item.id, group.category)"
                              />
                            } @else {
                              <app-news-feed-item
                                [item]="item"
                                (bookmarked)="store.toggleSaved(item.id)"
                                (dismissed)="onDismiss(item.id, group.category)"
                              />
                            }
                          }
                        </div>
                      }
                    </div>
                  } @else {
                    @for (item of sg.items; track item.id) {
                      @if (item.thumbnailUrl) {
                        <app-news-feed-video-card
                          [item]="item"
                          (bookmarked)="store.toggleSaved(item.id)"
                          (dismissed)="onDismiss(item.id, group.category)"
                        />
                      } @else {
                        <app-news-feed-item
                          [item]="item"
                          (bookmarked)="store.toggleSaved(item.id)"
                          (dismissed)="onDismiss(item.id, group.category)"
                        />
                      }
                    }
                  }
                }
              </div>
            }
          </div>
        }
      </div>
    }
  `,
  styles: `
    .category-section__header {
      display: flex;
      align-items: center;
      gap: 0.6rem;
      padding: 0.6rem 0;
      margin-bottom: 0.75rem;
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
      position: sticky;
      top: 0;
      z-index: 5;
      background: var(--p-surface-950);
      cursor: pointer;
      user-select: none;
    }
    .category-section__header:hover .category-section__chevron {
      color: rgba(255, 255, 255, 0.6);
    }
    .category-section__header:hover .category-section__name {
      color: rgba(255, 255, 255, 0.8);
    }
    .category-section__header i {
      font-size: 1rem;
    }
    .category-section__chevron {
      font-size: 0.75rem !important;
      color: rgba(255, 255, 255, 0.3);
      transition: transform 0.2s ease, color 0.2s ease;
      width: 16px;
      text-align: center;
    }
    .category-section__name {
      font-size: 0.78rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: rgba(255, 255, 255, 0.6);
      transition: color 0.2s ease;
    }
    .category-section__count {
      font-size: 0.65rem;
      font-weight: 700;
      padding: 0.15rem 0.5rem;
      border-radius: 10px;
      background: rgba(255, 255, 255, 0.06);
      color: rgba(255, 255, 255, 0.4);
    }
    .category-section__items {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .source-section {
      border-left: 2px solid rgba(255, 255, 255, 0.04);
      padding-left: 0.75rem;
    }
    .source-section__header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.4rem 0;
      margin-bottom: 0.5rem;
      cursor: pointer;
      user-select: none;
    }
    .source-section__header:hover .source-section__chevron {
      color: rgba(255, 255, 255, 0.5);
    }
    .source-section__header:hover .source-section__name {
      color: rgba(255, 255, 255, 0.7);
    }
    .source-section__chevron {
      font-size: 0.65rem !important;
      color: rgba(255, 255, 255, 0.2);
      transition: color 0.2s ease;
      width: 14px;
      text-align: center;
    }
    .source-section__header i:not(.source-section__chevron) {
      font-size: 0.8rem;
    }
    .source-section__name {
      font-size: 0.72rem;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.45);
      transition: color 0.2s ease;
    }
    .source-section__count {
      font-size: 0.6rem;
      font-weight: 700;
      padding: 0.1rem 0.4rem;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.04);
      color: rgba(255, 255, 255, 0.3);
    }
    .source-section__items {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    @media (max-width: 768px) {
      .category-section__header { padding: 0.5rem 0; margin-bottom: 0.5rem; }
      .category-section__name { font-size: 0.72rem; }
      .category-section__items { gap: 0.5rem; }
      .source-section { padding-left: 0.5rem; }
      .source-section__items { gap: 0.5rem; }
    }
  `,
})
export class NewsFeedComponent implements OnInit {
  readonly store = inject(NewsStore);
  private readonly el = inject(ElementRef);
  private readonly injector = inject(Injector);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly skeletonItems = Array.from({ length: 5 }, (_, i) => i);
  readonly groups = computed<readonly CategoryGroup[]>(() =>
    this.store.groupMode() === 'category'
      ? this.store.groupedByCategory()
      : this.store.groupedBySource()
  );

  readonly categoryStats = [
    { name: 'AI/ML', icon: CATEGORY_ICONS['AI/ML'], color: CATEGORY_COLORS['AI/ML'], status: 'Monitoring' },
    { name: '.NET/C#', icon: CATEGORY_ICONS['.NET/C#'], color: CATEGORY_COLORS['.NET/C#'], status: 'Monitoring' },
    { name: 'Angular', icon: CATEGORY_ICONS['Angular/Frontend'], color: CATEGORY_COLORS['Angular/Frontend'], status: 'Monitoring' },
    { name: 'Security', icon: CATEGORY_ICONS['Security'], color: CATEGORY_COLORS['Security'], status: 'Monitoring' },
  ];

  ngOnInit() {
    this.store.load(undefined);
  }

  categoryColor(category: string): string {
    if (this.store.groupMode() === 'source') {
      return SOURCE_COLORS[category] ?? '#6b7280';
    }
    return CATEGORY_COLORS[category] ?? '#6b7280';
  }

  categoryIcon(category: string): string {
    if (this.store.groupMode() === 'source') {
      return SOURCE_ICONS[category] ?? 'pi pi-rss';
    }
    return CATEGORY_ICONS[category] ?? 'pi pi-th-large';
  }

  isCollapsed(category: string): boolean {
    return this.store.collapsedCategories().has(category);
  }

  sourceColor(source: string): string {
    return SOURCE_COLORS[source] ?? '#6b7280';
  }

  sourceIcon(source: string): string {
    return SOURCE_ICONS[source] ?? 'pi pi-rss';
  }

  isSourceCollapsed(category: string, source: string): boolean {
    return this.store.collapsedSources().has(`${category}::${source}`);
  }

  toggleSource(category: string, source: string) {
    this.store.toggleSourceCollapse(`${category}::${source}`);
  }

  allCollapsed(): boolean {
    const groups = this.groups();
    return groups.length > 0 && groups.every((g) => this.isCollapsed(g.category));
  }

  toggleAllCollapse() {
    if (this.allCollapsed()) {
      this.store.expandAll();
    } else {
      this.store.collapseAll();
    }
  }

  onDismiss(itemId: string, category: string): void {
    const section: HTMLElement | null = this.el.nativeElement.querySelector(
      `[data-category="${CSS.escape(category)}"]`
    );
    const allSections: HTMLElement[] = Array.from(
      this.el.nativeElement.querySelectorAll('.category-section')
    );
    const sectionIdx = section ? allSections.indexOf(section) : -1;
    const nextSection = sectionIdx >= 0 ? allSections[sectionIdx + 1] ?? allSections[sectionIdx - 1] : null;
    const anchorTop = nextSection?.getBoundingClientRect().top ?? null;

    this.store.dismiss(itemId);
    this.cdr.detectChanges();

    afterNextRender(() => {
      const sameSection: HTMLElement | null = this.el.nativeElement.querySelector(
        `[data-category="${CSS.escape(category)}"]`
      );
      if (sameSection) {
        if (section) {
          const topAfter = sameSection.getBoundingClientRect().top;
          const topBefore = section.getBoundingClientRect().top;
          const diff = topAfter - topBefore;
          if (Math.abs(diff) > 1) {
            window.scrollBy(0, diff);
          }
        }
        return;
      }
      if (nextSection && anchorTop !== null) {
        const newTop = nextSection.getBoundingClientRect().top;
        const diff = newTop - anchorTop;
        if (Math.abs(diff) > 1) {
          window.scrollBy(0, diff);
        }
        nextSection.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    }, { injector: this.injector });
  }
}
