import { Component, inject, OnInit, signal, ViewEncapsulation, ElementRef, Injector, afterNextRender } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { Tooltip } from 'primeng/tooltip';
import { NewsStore } from '../../store/news.store';
import { NewsFeedFiltersComponent } from './news-feed-filters.component';
import { NewsFeedItemComponent } from './news-feed-item.component';
import { NewsFeedVideoCardComponent } from './news-feed-video-card.component';
import { CATEGORY_COLORS, CATEGORY_ICONS, NewsFeedItem, SOURCE_COLORS, SOURCE_ICONS } from '../../models/news.model';
import { ContentIdeaWizardComponent } from '../../../content/components/content-idea-wizard.component';
import { ContentPipelineDialogComponent } from '../../../content/components/content-pipeline-dialog.component';
import { ContentType, PlatformFormatOption, PlatformType } from '../../../../shared/models';

@Component({
  selector: 'app-news-feed',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [ButtonModule, SkeletonModule, Tooltip, NewsFeedFiltersComponent, NewsFeedItemComponent, NewsFeedVideoCardComponent, ContentIdeaWizardComponent, ContentPipelineDialogComponent],
  template: `
    <app-content-idea-wizard
      [(visible)]="wizardVisible"
      [storyContext]="wizardContext()"
      (contentRequested)="onContentRequested($event)"
    />
    @if (pipelineIdea()) {
      <app-content-pipeline-dialog
        [initialIdea]="pipelineIdea()!"
        (closed)="pipelineIdea.set(null)"
      />
    }

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
    } @else if (store.groupedByCategory().length === 0) {
      <!-- Rich empty state -->
      <div style="
        border: 1px dashed rgba(139,92,246,0.2); border-radius: 16px;
        padding: 3rem 2rem; text-align: center;
        background: linear-gradient(135deg, rgba(139,92,246,0.03) 0%, transparent 100%);
      ">
        <!-- Stats row showing category health -->
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

        <!-- Main message -->
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
          Try adjusting your category filters or time window, or hit Refresh to pull new trends
        </p>
        <p-button
          icon="pi pi-refresh" label="Refresh Trends" [outlined]="true" size="small"
          [loading]="store.refreshing()" (onClick)="store.refresh(undefined)"
        />
      </div>
    } @else {
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        @for (group of store.groupedByCategory(); track group.category) {
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
                        <i [class]="sourceIcon(sg.source)" [style.color]="sourceColor(sg.source)"></i>
                        <span class="source-section__name">{{ sg.sourceName }}</span>
                        <span class="source-section__count">{{ sg.items.length }}</span>
                      </div>
                      @if (!isSourceCollapsed(group.category, sg.sourceName)) {
                        <div class="source-section__items">
                          @for (item of sg.items; track item.id) {
                            @if (item.thumbnailUrl) {
                              <app-news-feed-video-card
                                [item]="item"
                                [analyzing]="store.analyzingIds().has(item.trendItemId)"
                                (bookmarked)="store.toggleSaved(item.trendItemId)"
                                (dismissed)="onDismiss(item.id, group.category)"
                                (analyzed)="store.analyzeItem(item.trendItemId)"
                              />
                            } @else {
                              <app-news-feed-item
                                [item]="item"
                                [analyzing]="store.analyzingIds().has(item.trendItemId)"
                                (bookmarked)="store.toggleSaved(item.trendItemId)"
                                (dismissed)="onDismiss(item.id, group.category)"
                                (analyzed)="store.analyzeItem(item.trendItemId)"
                                (newIdea)="openIdeaWizard($event)"
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
                          [analyzing]="store.analyzingIds().has(item.trendItemId)"
                          (bookmarked)="store.toggleSaved(item.trendItemId)"
                          (dismissed)="onDismiss(item.id, group.category)"
                          (analyzed)="store.analyzeItem(item.trendItemId)"
                          (newIdea)="openIdeaWizard($event)"
                        />
                      } @else {
                        <app-news-feed-item
                          [item]="item"
                          [analyzing]="store.analyzingIds().has(item.trendItemId)"
                          (bookmarked)="store.toggleSaved(item.trendItemId)"
                          (dismissed)="onDismiss(item.id, group.category)"
                          (analyzed)="store.analyzeItem(item.trendItemId)"
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

    /* ── Source sub-group ── */
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
  `,
})
export class NewsFeedComponent implements OnInit {
  readonly store = inject(NewsStore);
  private readonly el = inject(ElementRef);
  private readonly injector = inject(Injector);
  readonly skeletonItems = Array.from({ length: 5 }, (_, i) => i);

  wizardVisible = false;
  readonly wizardContext = signal<{ title: string; text: string; sourceUrl?: string } | null>(null);
  readonly pipelineIdea = signal<{ topic: string; type: ContentType; platform: PlatformType } | null>(null);

  openIdeaWizard(item: NewsFeedItem): void {
    this.wizardContext.set({
      title: item.title,
      text: item.summary!,
      sourceUrl: item.url,
    });
    this.wizardVisible = true;
  }

  onContentRequested(event: { options: PlatformFormatOption[]; storyText: string }): void {
    if (event.options.length === 0) return;
    const top = event.options[0];
    this.pipelineIdea.set({
      topic: top.suggestedAngle,
      type: top.format,
      platform: top.platform,
    });
  }

  readonly categoryStats = [
    { name: 'AI/ML', icon: CATEGORY_ICONS['AI/ML'], color: CATEGORY_COLORS['AI/ML'], status: 'Monitoring' },
    { name: '.NET/C#', icon: CATEGORY_ICONS['.NET/C#'], color: CATEGORY_COLORS['.NET/C#'], status: 'Monitoring' },
    { name: 'Angular', icon: CATEGORY_ICONS['Angular/Frontend'], color: CATEGORY_COLORS['Angular/Frontend'], status: 'Monitoring' },
    { name: 'Security', icon: CATEGORY_ICONS['Security'], color: CATEGORY_COLORS['Security'], status: 'Monitoring' },
  ];

  ngOnInit() {
    this.store.load(undefined);
    this.store.loadSaved(undefined);
  }

  categoryColor(category: string): string {
    return CATEGORY_COLORS[category] ?? '#6b7280';
  }

  categoryIcon(category: string): string {
    return CATEGORY_ICONS[category] ?? 'pi pi-th-large';
  }

  isCollapsed(category: string): boolean {
    return this.store.collapsedCategories().has(category);
  }

  sourceColor(source: string): string {
    return SOURCE_COLORS[source] ?? '#6b7280';
  }

  sourceIcon(source: string): string {
    return SOURCE_ICONS[source] ?? 'pi pi-circle';
  }

  isSourceCollapsed(category: string, source: string): boolean {
    return this.store.collapsedSources().has(`${category}::${source}`);
  }

  toggleSource(category: string, source: string) {
    this.store.toggleSourceCollapse(`${category}::${source}`);
  }

  allCollapsed(): boolean {
    const groups = this.store.groupedByCategory();
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
    if (!section) {
      this.store.dismiss(itemId);
      return;
    }

    const topBefore = section.getBoundingClientRect().top;
    this.store.dismiss(itemId);

    afterNextRender(() => {
      const sameSection: HTMLElement | null = this.el.nativeElement.querySelector(
        `[data-category="${CSS.escape(category)}"]`
      );
      if (!sameSection) return;
      const topAfter = sameSection.getBoundingClientRect().top;
      const diff = topAfter - topBefore;
      if (diff !== 0) {
        window.scrollBy(0, diff);
      }
    }, { injector: this.injector });
  }
}
