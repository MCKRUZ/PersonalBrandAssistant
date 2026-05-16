import { computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { signalStore, withState, withComputed, withMethods, withHooks, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { FeedService } from '../services/feed.service';
import { FeedHubService } from '../services/feed-hub.service';
import { FeedItemType } from '../models/feed-item.model';
import type { FeedItem } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { TrendingTopic } from '../models/trending-topic.model';

type FeedState = {
  items: FeedItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  activeFilter: FeedItemType | null;
  loading: boolean;
  error: string | null;
  summary: FeedSummary | null;
  summaryLoading: boolean;
  trendingTopics: TrendingTopic[];
  selectedIds: string[];
  newItemCount: number;
  lastBatchFailures: { id: string; reason: string }[];
};

const initialState: FeedState = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  activeFilter: null,
  loading: false,
  error: null,
  summary: null,
  summaryLoading: false,
  trendingTopics: [],
  selectedIds: [],
  newItemCount: 0,
  lastBatchFailures: [],
};

export const FeedStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(({ selectedIds, items }) => ({
    hasSelection: computed(() => selectedIds().length > 0),
    selectedCount: computed(() => selectedIds().length),
    isAllSelected: computed(() => selectedIds().length === items().length && items().length > 0),
  })),
  withMethods((store) => {
    const feedService = inject(FeedService);

    const loadItems = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          feedService.list({
            type: store.activeFilter() ?? undefined,
            page: store.page(),
            pageSize: store.pageSize(),
          }).pipe(
            tapResponse({
              next: (result) =>
                patchState(store, {
                  items: result.items,
                  totalCount: result.totalCount,
                  loading: false,
                }),
              error: (err: Error) =>
                patchState(store, { loading: false, error: err.message }),
            })
          )
        )
      )
    );

    const loadSummary = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { summaryLoading: true })),
        switchMap(() =>
          feedService.getSummary().pipe(
            tapResponse({
              next: (summary) =>
                patchState(store, { summary, summaryLoading: false }),
              error: (err: Error) =>
                patchState(store, { summaryLoading: false, error: err.message }),
            })
          )
        )
      )
    );

    const loadTrending = rxMethod<void>(
      pipe(
        switchMap(() =>
          feedService.getTrending().pipe(
            tapResponse({
              next: (trendingTopics) => patchState(store, { trendingTopics }),
              error: (err: Error) => patchState(store, { error: err.message }),
            })
          )
        )
      )
    );

    return {
      loadItems,
      loadSummary,
      loadTrending,

      setFilter(type: FeedItemType | null): void {
        patchState(store, { activeFilter: type, page: 1 });
        loadItems();
      },

      setPage(page: number): void {
        patchState(store, { page });
        loadItems();
      },

      toggleSelect(id: string): void {
        const current = store.selectedIds();
        const updated = current.includes(id)
          ? current.filter((x) => x !== id)
          : [...current, id];
        patchState(store, { selectedIds: updated });
      },

      selectAll(): void {
        patchState(store, { selectedIds: store.items().map((i) => i.id) });
      },

      clearSelection(): void {
        patchState(store, { selectedIds: [] });
      },

      markRead(id: string): void {
        feedService.markRead(id).subscribe({
          next: () => {
            patchState(store, {
              items: store.items().map((item) =>
                item.id === id ? { ...item, isRead: true } : item
              ),
            });
            loadSummary();
          },
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },

      actOnItem(id: string, action: string): void {
        feedService.actOnItem(id, action).subscribe({
          next: () => {
            patchState(store, {
              items: store.items().map((item) =>
                item.id === id ? { ...item, isActedOn: true } : item
              ),
            });
            loadSummary();
          },
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },

      batchMarkRead(type?: FeedItemType, isRead: boolean = true): void {
        feedService.batchMarkRead(type, isRead).subscribe({
          next: () => {
            loadItems();
            loadSummary();
          },
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },

      batchDismiss(type: FeedItemType): void {
        feedService.batchDismiss(type).subscribe({
          next: () => {
            loadItems();
            loadSummary();
          },
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },

      batchAct(ids: string[], action: string): void {
        feedService.batchAct(ids, action).subscribe({
          next: (result) => {
            patchState(store, { selectedIds: [], lastBatchFailures: result.failures });
            loadItems();
            loadSummary();
          },
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },

      incrementNewItemCount(): void {
        patchState(store, { newItemCount: store.newItemCount() + 1 });
      },

      loadNewItems(): void {
        patchState(store, { page: 1, newItemCount: 0 });
        loadItems();
        loadSummary();
      },

      updateSummary(summary: FeedSummary): void {
        patchState(store, { summary });
      },
    };
  }),
  withHooks({
    onInit(store) {
      store.loadItems();
      store.loadSummary();
      store.loadTrending();

      const destroyRef = inject(DestroyRef);
      const feedHubService = inject(FeedHubService);
      feedHubService.feedItemReceived$
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe(() => store.incrementNewItemCount());
      feedHubService.summaryUpdated$
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe((summary) => store.updateSummary(summary));
    },
  })
);
