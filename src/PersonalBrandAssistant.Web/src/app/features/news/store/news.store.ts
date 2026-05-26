import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { NewsService } from '../services/news.service';
import { NewsFeedItem, NewsFeedFilters, CategoryGroup, SourceGroup, GroupMode, CATEGORY_ORDER, ideaToFeedItem } from '../models/news.model';

const STORAGE_KEY_MAX_AGE = 'pba_news_maxAgeHours';

function loadMaxAgeHours(): number {
  try {
    const stored = localStorage.getItem(STORAGE_KEY_MAX_AGE);
    if (stored !== null) {
      const val = Number(stored);
      return Number.isFinite(val) ? val : 72;
    }
  } catch { /* localStorage unavailable */ }
  return 72;
}

function buildSourceGroups(items: readonly NewsFeedItem[]): readonly SourceGroup[] {
  const grouped = new Map<string, NewsFeedItem[]>();
  for (const item of items) {
    const key = item.sourceName;
    const list = grouped.get(key);
    if (list) {
      list.push(item);
    } else {
      grouped.set(key, [item]);
    }
  }
  return Array.from(grouped, ([sourceName, groupItems]) => ({
    sourceName,
    items: groupItems,
  })).sort((a, b) => a.sourceName.localeCompare(b.sourceName));
}

interface NewsState {
  readonly items: readonly NewsFeedItem[];
  readonly collapsedCategories: ReadonlySet<string>;
  readonly collapsedSources: ReadonlySet<string>;
  readonly filters: NewsFeedFilters;
  readonly groupMode: GroupMode;
  readonly loading: boolean;
  readonly refreshing: boolean;
  readonly lastRefreshDelta: number;
}

const initialState: NewsState = {
  items: [],
  collapsedCategories: new Set<string>(),
  collapsedSources: new Set<string>(),
  filters: {
    categories: [],
    maxAgeHours: loadMaxAgeHours(),
    searchQuery: '',
    showSavedOnly: false,
  },
  groupMode: 'category' as GroupMode,
  loading: false,
  refreshing: false,
  lastRefreshDelta: 0,
};

export const NewsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => {
    const allItems = computed(() => store.items());

    const filteredItems = computed(() => {
      const items = allItems();
      const filters = store.filters();
      return items.filter((item) => {
        if (filters.categories.length > 0 && !filters.categories.includes(item.sourceCategory)) {
          return false;
        }
        if (filters.maxAgeHours > 0) {
          const cutoff = Date.now() - filters.maxAgeHours * 60 * 60 * 1000;
          if (new Date(item.createdAt).getTime() < cutoff) {
            return false;
          }
        }
        if (filters.showSavedOnly && !item.saved) {
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
          const cat = item.sourceCategory;
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
        const overflow = [...grouped.entries()]
          .filter(([category, categoryItems]) => !CATEGORY_ORDER.includes(category) && categoryItems.length > 0)
          .sort(([a], [b]) => a.localeCompare(b));
        for (const [category, categoryItems] of overflow) {
          result.push({ category, items: categoryItems, sourceGroups: buildSourceGroups(categoryItems) });
        }
        return result;
      }),
      groupedBySource: computed(() => {
        const items = filteredItems();
        const grouped = new Map<string, NewsFeedItem[]>();
        for (const item of items) {
          const key = item.sourceName;
          const list = grouped.get(key);
          if (list) {
            list.push(item);
          } else {
            grouped.set(key, [item]);
          }
        }
        return Array.from(grouped, ([sourceName, sourceItems]) => ({
          category: sourceName,
          items: sourceItems,
          sourceGroups: [{ sourceName, items: sourceItems }] as readonly SourceGroup[],
        })).sort((a, b) => a.category.localeCompare(b.category)) as readonly CategoryGroup[];
      }),
      availableCategories: computed(() => {
        const items = allItems();
        const cats = new Set<string>();
        for (const item of items) {
          cats.add(item.sourceCategory);
        }
        return CATEGORY_ORDER.filter((c) => cats.has(c));
      }),
    };
  }),
  withMethods((store, newsService = inject(NewsService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          newsService.getIdeas().pipe(
            tapResponse({
              next: (ideas) => patchState(store, { items: ideas.map(ideaToFeedItem), loading: false }),
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
            newsService.refreshSources().pipe(
              switchMap(() => newsService.getIdeas()),
              tapResponse({
                next: (ideas) => {
                  const mapped = ideas.map(ideaToFeedItem);
                  patchState(store, { items: mapped, refreshing: false });
                  const delta = Math.max(0, mapped.length - prevCount);
                  patchState(store, { lastRefreshDelta: delta });
                },
                error: () => patchState(store, { refreshing: false }),
              })
            )
          )
        )
      );
    })(),

    dismiss(itemId: string) {
      const prev = store.items();
      patchState(store, { items: prev.filter((i) => i.id !== itemId) });
      newsService.dismissIdea(itemId).subscribe({
        error: () => patchState(store, { items: prev }),
      });
    },

    toggleSaved(itemId: string) {
      const item = store.items().find((i) => i.id === itemId);
      if (!item) return;

      if (item.saved) {
        newsService.dismissIdea(itemId).subscribe();
      } else {
        newsService.saveIdea(itemId).subscribe();
      }

      patchState(store, {
        items: store.items().map((i) =>
          i.id === itemId ? { ...i, saved: !i.saved } : i
        ),
      });
    },

    updateFilters(filters: Partial<NewsFeedFilters>) {
      const merged = { ...store.filters(), ...filters };
      patchState(store, { filters: merged });
      if (filters.maxAgeHours !== undefined) {
        try {
          localStorage.setItem(STORAGE_KEY_MAX_AGE, String(filters.maxAgeHours));
        } catch { /* localStorage unavailable */ }
      }
    },

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

    toggleGroupMode() {
      const next: GroupMode = store.groupMode() === 'category' ? 'source' : 'category';
      patchState(store, { groupMode: next, collapsedCategories: new Set<string>(), collapsedSources: new Set<string>() });
    },
  }))
);
