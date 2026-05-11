import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { IdeaService } from '../../../core/services/idea.service';
import { IdeaSource, IdeaSourceRequest } from '../../../models/idea.model';

type IdeaSourceStoreState = {
  sources: IdeaSource[];
  loading: boolean;
  error: string | null;
  lastRefreshCount: number | null;
};

const initialState: IdeaSourceStoreState = {
  sources: [],
  loading: false,
  error: null,
  lastRefreshCount: null,
};

export const IdeaSourceStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => {
    const ideaService = inject(IdeaService);

    function loadAll(): void {
      patchState(store, { loading: true, error: null });
      ideaService.listSources().subscribe({
        next: (sources) => patchState(store, { sources, loading: false }),
        error: (err: Error) => patchState(store, { loading: false, error: err.message }),
      });
    }

    return {
      loadAll,
      create(source: IdeaSourceRequest): void {
        ideaService.createSource(source).subscribe({
          next: () => loadAll(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
      update(id: string, source: Partial<IdeaSourceRequest>): void {
        ideaService.updateSource(id, source).subscribe({
          next: () => loadAll(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
      remove(id: string): void {
        ideaService.deleteSource(id).subscribe({
          next: () => loadAll(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
      refreshAll(): void {
        patchState(store, { loading: true });
        ideaService.refreshSources().subscribe({
          next: (count) => {
            patchState(store, { lastRefreshCount: count });
            loadAll();
          },
          error: (err: Error) => patchState(store, { loading: false, error: err.message }),
        });
      },
    };
  })
);
