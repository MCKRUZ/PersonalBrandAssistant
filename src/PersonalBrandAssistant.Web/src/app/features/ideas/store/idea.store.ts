import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { IdeaService } from '../../../core/services/idea.service';
import { Idea, IdeaFilterState, IdeaSortState } from '../../../models/idea.model';

// The ranked window is a ranked-view overlay, NOT a mutation of the user's shared filter:
// "today" = local midnight → now; "week" = the rolling last 7 days.
function rankedWindowFrom(window: 'today' | 'week'): string {
  const from =
    window === 'today'
      ? new Date(new Date().setHours(0, 0, 0, 0))
      : new Date(Date.now() - 7 * 24 * 60 * 60 * 1000);
  return from.toISOString();
}

type IdeaStoreState = {
  ideas: Idea[];
  totalCount: number;
  page: number;
  pageSize: number;
  filter: IdeaFilterState;
  sort: IdeaSortState;
  viewMode: 'grid' | 'list' | 'ranked';
  rankedWindow: 'today' | 'week';
  rankedTopN: number;
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
  sort: { field: 'rank', direction: 'desc' },
  viewMode: 'list',
  rankedWindow: 'today',
  rankedTopN: 20,
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
        switchMap(() => {
          // In ranked mode, overlay the window dateFrom and request rankedTopN rows — WITHOUT touching the
          // shared filter or pageSize, so switching back to grid/list restores the user's view untouched.
          const ranked = store.viewMode() === 'ranked';
          const filter = ranked
            ? { ...store.filter(), dateFrom: rankedWindowFrom(store.rankedWindow()) }
            : store.filter();
          const size = ranked ? store.rankedTopN() : store.pageSize();
          return ideaService.list(filter, store.page(), size, store.sort()).pipe(
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
          );
        })
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
      setViewMode(mode: 'grid' | 'list' | 'ranked'): void {
        // Reload so the ranked-window/topN overlay is applied on entering 'ranked' and dropped on leaving.
        patchState(store, { viewMode: mode, page: 1 });
        loadIdeas();
      },
      setRankedWindow(window: 'today' | 'week'): void {
        patchState(store, { rankedWindow: window, page: 1 });
        loadIdeas();
      },
      setRankedTopN(n: number): void {
        patchState(store, { rankedTopN: n, page: 1 });
        loadIdeas();
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
