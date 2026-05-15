import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { ContentService } from '../services/content.service';
import { Content, ContentFilterState } from '../models/content.model';

type ContentStoreState = {
  contents: Content[];
  totalCount: number;
  page: number;
  pageSize: number;
  filters: Partial<ContentFilterState>;
  viewMode: 'list' | 'grid';
  loading: boolean;
  error: string | null;
};

const initialState: ContentStoreState = {
  contents: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  filters: {},
  viewMode: 'list',
  loading: false,
  error: null,
};

export const ContentStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((state) => ({
    totalPages: computed(() => Math.ceil(state.totalCount() / state.pageSize())),
    hasNextPage: computed(() => state.page() * state.pageSize() < state.totalCount()),
    hasPreviousPage: computed(() => state.page() > 1),
  })),
  withMethods((store) => {
    const contentService = inject(ContentService);

    const loadContents = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          contentService.list(store.filters(), store.page(), store.pageSize()).pipe(
            tapResponse({
              next: (result) =>
                patchState(store, {
                  contents: result.items,
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

    return {
      loadContents,
      setFilter<K extends keyof ContentFilterState>(key: K, value: ContentFilterState[K]): void {
        patchState(store, {
          filters: { ...store.filters(), [key]: value },
          page: 1,
        });
        loadContents();
      },
      setPage(page: number): void {
        patchState(store, { page });
        loadContents();
      },
      deleteContent(id: string): void {
        contentService.delete(id).subscribe({
          next: () => loadContents(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
      toggleView(): void {
        patchState(store, {
          viewMode: store.viewMode() === 'list' ? 'grid' : 'list',
        });
      },
    };
  })
);
