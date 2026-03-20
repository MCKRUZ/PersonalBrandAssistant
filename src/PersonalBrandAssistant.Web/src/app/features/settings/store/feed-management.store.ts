import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState, withComputed } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { NewsService } from '../../news/services/news.service';
import { NewsSource } from '../../news/models/news.model';
import { FEED_CATALOG } from '../data/feed-catalog';
import { CatalogFeed } from '../models/feed-catalog.model';

interface FeedManagementState {
  readonly feeds: readonly NewsSource[];
  readonly loading: boolean;
  readonly searchQuery: string;
}

const initialState: FeedManagementState = {
  feeds: [],
  loading: false,
  searchQuery: '',
};

export const FeedManagementStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => ({
    feedsByCategory: computed(() => {
      const grouped = new Map<string, NewsSource[]>();

      for (const feed of store.feeds()) {
        const category = feed.type === 'RssFeed' ? (feed.category ?? 'Uncategorized') : 'Other';
        const existing = grouped.get(category) ?? [];
        grouped.set(category, [...existing, feed]);
      }

      return [...grouped.entries()]
        .map(([category, feeds]) => ({ category, feeds }))
        .sort((a, b) => a.category.localeCompare(b.category));
    }),

    subscribedUrls: computed(() =>
      new Set(
        store.feeds()
          .filter((f) => f.feedUrl)
          .map((f) => f.feedUrl!)
      )
    ),

    searchResults: computed(() => {
      const query = store.searchQuery().toLowerCase();

      if (query.length < 2) {
        return [] as CatalogFeed[];
      }

      return FEED_CATALOG.filter(
        (feed) =>
          feed.name.toLowerCase().includes(query) ||
          feed.description.toLowerCase().includes(query) ||
          feed.category.toLowerCase().includes(query) ||
          feed.tags.some((tag) => tag.toLowerCase().includes(query))
      );
    }),
  })),
  withMethods((store, newsService = inject(NewsService)) => ({
    loadFeeds: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          newsService.getSources().pipe(
            tapResponse({
              next: (sources) =>
                patchState(store, {
                  feeds: sources.filter((s) => s.type === 'RssFeed'),
                  loading: false,
                }),
              error: () => patchState(store, { loading: false }),
            })
          )
        )
      )
    ),

    toggleFeed: rxMethod<string>(
      pipe(
        tap((id) =>
          patchState(store, {
            feeds: store.feeds().map((f) =>
              f.id === id ? { ...f, isEnabled: !f.isEnabled } : f
            ),
          })
        ),
        switchMap((id) =>
          newsService.toggleSource(id).pipe(
            tapResponse({
              next: () => {},
              error: () =>
                patchState(store, {
                  feeds: store.feeds().map((f) =>
                    f.id === id ? { ...f, isEnabled: !f.isEnabled } : f
                  ),
                }),
            })
          )
        )
      )
    ),

    deleteFeed: rxMethod<string>(
      pipe(
        tap((id) =>
          patchState(store, {
            feeds: store.feeds().filter((f) => f.id !== id),
          })
        ),
        switchMap((id) =>
          newsService.deleteSource(id).pipe(
            tapResponse({
              next: () => {},
              error: () =>
                newsService.getSources().pipe(
                  tapResponse({
                    next: (sources) =>
                      patchState(store, {
                        feeds: sources.filter((s) => s.type === 'RssFeed'),
                      }),
                    error: () => {},
                  })
                ),
            })
          )
        )
      )
    ),

    addFeed: rxMethod<CatalogFeed>(
      pipe(
        switchMap((feed) =>
          newsService.createSource(feed.name, feed.feedUrl, feed.category).pipe(
            tapResponse({
              next: (created) =>
                patchState(store, {
                  feeds: [...store.feeds(), created],
                }),
              error: () => {},
            })
          )
        )
      )
    ),

    setSearchQuery(query: string): void {
      patchState(store, { searchQuery: query });
    },
  }))
);
