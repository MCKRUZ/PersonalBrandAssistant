import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { IdeaService } from '../../../core/services/idea.service';
import { Idea, IdeaFilterState, IdeaSortState } from '../../../models/idea.model';

type IdeaStoreState = {
  ideas: Idea[];
  totalCount: number;
  page: number;
  pageSize: number;
  filter: IdeaFilterState;
  sort: IdeaSortState;
  viewMode: 'grid' | 'list';
  selectedIdeaId: string | null;
  loading: boolean;
  error: string | null;
};

const initialState: IdeaStoreState = {
  ideas: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  filter: {
    status: null,
    sourceId: null,
    category: null,
    tags: [],
    dateFrom: null,
    dateTo: null,
    searchText: null,
    minScore: null,
  },
  sort: { field: 'detectedAt', direction: 'desc' },
  viewMode: 'list',
  selectedIdeaId: null,
  loading: false,
  error: null,
};

export const IdeaStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((state) => ({
    totalPages: computed(() => Math.ceil(state.totalCount() / state.pageSize())),
    hasNextPage: computed(() => state.page() * state.pageSize() < state.totalCount()),
    hasPreviousPage: computed(() => state.page() > 1),
  })),
  withMethods((store) => {
    const ideaService = inject(IdeaService);

    const loadIdeas = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          ideaService.list(store.filter(), store.page(), store.pageSize(), store.sort()).pipe(
            tapResponse({
              next: (result) =>
                patchState(store, {
                  ideas: result.items,
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
      loadIdeas,
      setFilter(filter: Partial<IdeaFilterState>): void {
        patchState(store, {
          filter: { ...store.filter(), ...filter },
          page: 1,
        });
        loadIdeas();
      },
      setSort(sort: IdeaSortState): void {
        patchState(store, { sort, page: 1 });
        loadIdeas();
      },
      setPage(page: number): void {
        patchState(store, { page });
        loadIdeas();
      },
      toggleView(): void {
        patchState(store, {
          viewMode: store.viewMode() === 'list' ? 'grid' : 'list',
        });
      },
      selectIdea(id: string | null): void {
        patchState(store, { selectedIdeaId: id });
      },
      saveIdea(id: string, notes: string | null, tags: string[]): void {
        ideaService.save(id, notes, tags).subscribe({
          next: () => loadIdeas(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
      dismissIdea(id: string): void {
        ideaService.dismiss(id).subscribe({
          next: () => loadIdeas(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
      setError(message: string): void {
        patchState(store, { error: message });
      },
    };
  })
);
