import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, mergeMap, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { TrendSuggestion } from '../../../shared/models';
import { NewsService } from '../services/news.service';
import { NewsFeedItem, NewsFeedFilters, CategoryGroup, SourceGroup, CATEGORY_ORDER } from '../models/news.model';

const STORAGE_KEY_MAX_AGE = 'pba_news_maxAgeHours';

function loadMaxAgeHours(): number {
  try {
    const stored = localStorage.getItem(STORAGE_KEY_MAX_AGE);
    if (stored !== null) {
      const val = Number(stored);
      return Number.isFinite(val) ? val : 24;
    }
  } catch { /* localStorage unavailable */ }
  return 24;
}

interface SavedEntry {
  readonly savedId: string;
  readonly trendItemId: string;
}

interface NewsState {
  readonly suggestions: readonly TrendSuggestion[];
  readonly savedEntries: readonly SavedEntry[];
  readonly analyzingIds: ReadonlySet<string>;
  readonly collapsedCategories: ReadonlySet<string>;
  readonly collapsedSources: ReadonlySet<string>;
  readonly filters: NewsFeedFilters;
  readonly loading: boolean;
  readonly refreshing: boolean;
  readonly lastRefreshDelta: number;
}

const initialState: NewsState = {
  suggestions: [],
  savedEntries: [],
  analyzingIds: new Set<string>(),
  collapsedCategories: new Set<string>(),
  collapsedSources: new Set<string>(),
  filters: {
    sourceTypes: [],
    categories: [],
    maxAgeHours: loadMaxAgeHours(),
    minRelevance: 0,
    searchQuery: '',
  },
  loading: false,
  refreshing: false,
  lastRefreshDelta: 0,
};

function flattenToFeedItems(
  suggestions: readonly TrendSuggestion[],
  savedTrendItemIds: ReadonlySet<string>
): readonly NewsFeedItem[] {
  return suggestions.flatMap((s) =>
    s.relatedTrends.map((item, idx) => ({
      id: `${s.id}-${idx}`,
      suggestionId: s.id,
      source: item.source,
      sourceName: item.sourceName,
      title: item.title,
      description: item.description || (item.thumbnailUrl ? item.sourceName : s.rationale),
      url: item.url,
      thumbnailUrl: item.thumbnailUrl,
      sourceCategory: item.sourceCategory ?? 'Uncategorized',
      score: item.score,
      relevanceScore: s.relevanceScore,
      topic: s.topic,
      suggestedContentType: s.suggestedContentType,
      suggestedPlatforms: s.suggestedPlatforms,
      createdAt: s.createdAt,
      saved: savedTrendItemIds.has(item.trendItemId),
      trendItemId: item.trendItemId,
      summary: item.summary,
    }))
  );
}

function buildSourceGroups(items: readonly NewsFeedItem[]): readonly SourceGroup[] {
  const grouped = new Map<string, NewsFeedItem[]>();
  const sourceTypes = new Map<string, string>();
  for (const item of items) {
    const key = item.sourceName ?? item.source;
    const list = grouped.get(key);
    if (list) {
      list.push(item);
    } else {
      grouped.set(key, [item]);
      sourceTypes.set(key, item.source);
    }
  }
  return Array.from(grouped, ([sourceName, groupItems]) => ({
    source: sourceTypes.get(sourceName) ?? sourceName,
    sourceName,
    items: groupItems,
  }));
}

export const NewsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => {
    const savedTrendItemIds = computed(() =>
      new Set(store.savedEntries().map((e) => e.trendItemId))
    );
    const allItems = computed(() =>
      flattenToFeedItems(store.suggestions(), savedTrendItemIds())
    );

    const filteredItems = computed(() => {
      const items = allItems();
      const filters = store.filters();
      return items.filter((item) => {
        if (filters.sourceTypes.length > 0 && !filters.sourceTypes.includes(item.source)) {
          return false;
        }
        if (filters.categories.length > 0 && !filters.categories.includes(item.sourceCategory ?? 'Uncategorized')) {
          return false;
        }
        if (filters.maxAgeHours > 0) {
          const cutoff = Date.now() - filters.maxAgeHours * 60 * 60 * 1000;
          if (new Date(item.createdAt).getTime() < cutoff) {
            return false;
          }
        }
        if (item.relevanceScore < filters.minRelevance / 100) {
          return false;
        }
        if (filters.searchQuery) {
          const q = filters.searchQuery.toLowerCase();
          return (
            item.title.toLowerCase().includes(q) ||
            item.topic.toLowerCase().includes(q) ||
            (item.description?.toLowerCase().includes(q) ?? false)
          );
        }
        return true;
      });
    });

    return {
      allItems,
      filteredItems,
      groupedByCategory: computed(() => {
        const items = filteredItems();
        const grouped = new Map<string, NewsFeedItem[]>();
        for (const item of items) {
          const cat = item.sourceCategory ?? 'Uncategorized';
          const list = grouped.get(cat);
          if (list) {
            list.push(item);
          } else {
            grouped.set(cat, [item]);
          }
        }
        const result: CategoryGroup[] = [];
        for (const category of CATEGORY_ORDER) {
          const categoryItems = grouped.get(category);
          if (categoryItems && categoryItems.length > 0) {
            result.push({ category, items: categoryItems, sourceGroups: buildSourceGroups(categoryItems) });
          }
        }
        // Include any categories not in CATEGORY_ORDER
        for (const [category, categoryItems] of grouped) {
          if (!CATEGORY_ORDER.includes(category) && categoryItems.length > 0) {
            result.push({ category, items: categoryItems, sourceGroups: buildSourceGroups(categoryItems) });
          }
        }
        return result;
      }),
      availableCategories: computed(() => {
        const items = allItems();
        const cats = new Set<string>();
        for (const item of items) {
          cats.add(item.sourceCategory ?? 'Uncategorized');
        }
        return CATEGORY_ORDER.filter((c) => cats.has(c));
      }),
      sourceBreakdown: computed(() => {
        const items = allItems();
        const counts: Record<string, number> = {};
        for (const item of items) {
          counts[item.source] = (counts[item.source] ?? 0) + 1;
        }
        return counts;
      }),
    };
  }),
  withMethods((store, newsService = inject(NewsService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          newsService.getSuggestions(100).pipe(
            tapResponse({
              next: (suggestions) => patchState(store, { suggestions, loading: false }),
              error: () => patchState(store, { loading: false }),
            })
          )
        )
      )
    ),

    refresh: (() => {
      let prevCount = 0;
      return rxMethod<void>(
        pipe(
          tap(() => {
            prevCount = store.allItems().length;
            patchState(store, { refreshing: true, lastRefreshDelta: 0 });
          }),
          switchMap(() =>
            newsService.refreshTrends().pipe(
              switchMap(() => newsService.getSuggestions(100)),
              tapResponse({
                next: (suggestions) => {
                  patchState(store, { suggestions, refreshing: false });
                  const delta = Math.max(0, store.allItems().length - prevCount);
                  patchState(store, { lastRefreshDelta: delta });
                },
                error: () => patchState(store, { refreshing: false }),
              })
            )
          )
        )
      );
    })(),

    dismiss: rxMethod<string>(
      pipe(
        switchMap((suggestionId) =>
          newsService.dismissSuggestion(suggestionId).pipe(
            tapResponse({
              next: () =>
                patchState(store, {
                  suggestions: store.suggestions().filter((s) => s.id !== suggestionId),
                }),
              error: () => {},
            })
          )
        )
      )
    ),

    updateFilters(filters: Partial<NewsFeedFilters>) {
      const merged = { ...store.filters(), ...filters };
      patchState(store, { filters: merged });
      if (filters.maxAgeHours !== undefined) {
        try {
          localStorage.setItem(STORAGE_KEY_MAX_AGE, String(filters.maxAgeHours));
        } catch { /* localStorage unavailable */ }
      }
    },

    toggleSaved: rxMethod<string>(
      pipe(
        mergeMap((trendItemId) => {
          const existing = store.savedEntries().find((e) => e.trendItemId === trendItemId);
          if (existing) {
            // Optimistic remove
            patchState(store, {
              savedEntries: store.savedEntries().filter((e) => e.trendItemId !== trendItemId),
            });
            return newsService.removeSavedItem(existing.savedId).pipe(
              tapResponse({
                next: () => {},
                error: () => {
                  // Rollback on error
                  patchState(store, { savedEntries: [...store.savedEntries(), existing] });
                },
              })
            );
          } else {
            return newsService.saveItem(trendItemId).pipe(
              tapResponse({
                next: (saved) => {
                  patchState(store, {
                    savedEntries: [...store.savedEntries(), { savedId: saved.id, trendItemId: saved.trendItemId }],
                  });
                },
                error: () => {},
              })
            );
          }
        })
      )
    ),

    loadSaved: rxMethod<void>(
      pipe(
        switchMap(() =>
          newsService.getSavedItems().pipe(
            tapResponse({
              next: (items) => {
                const entries: SavedEntry[] = items.map((i) => ({ savedId: i.id, trendItemId: i.trendItemId }));
                patchState(store, { savedEntries: entries });
              },
              error: () => {},
            })
          )
        )
      )
    ),

    toggleCategoryCollapse(category: string) {
      const current = store.collapsedCategories();
      const next = new Set(current);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      patchState(store, { collapsedCategories: next });
    },

    toggleSourceCollapse(key: string) {
      const current = store.collapsedSources();
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      patchState(store, { collapsedSources: next });
    },

    collapseAll() {
      const allCats = store.groupedByCategory().map((g) => g.category);
      patchState(store, { collapsedCategories: new Set(allCats) });
    },

    expandAll() {
      patchState(store, { collapsedCategories: new Set<string>(), collapsedSources: new Set<string>() });
    },

    analyzeItem: rxMethod<string>(
      pipe(
        tap((trendItemId) => {
          const next = new Set(store.analyzingIds());
          next.add(trendItemId);
          patchState(store, { analyzingIds: next });
        }),
        mergeMap((trendItemId) =>
          newsService.analyzeItem(trendItemId).pipe(
            tapResponse({
              next: (result) => {
                const updated = store.suggestions().map((s) => ({
                  ...s,
                  relatedTrends: s.relatedTrends.map((rt) =>
                    rt.trendItemId === trendItemId
                      ? { ...rt, summary: result.summary, thumbnailUrl: result.imageUrl ?? rt.thumbnailUrl }
                      : rt
                  ),
                }));
                const nextIds = new Set(store.analyzingIds());
                nextIds.delete(trendItemId);
                patchState(store, { suggestions: updated, analyzingIds: nextIds });
              },
              error: () => {
                const nextIds = new Set(store.analyzingIds());
                nextIds.delete(trendItemId);
                patchState(store, { analyzingIds: nextIds });
              },
            })
          )
        )
      )
    ),
  }))
);
