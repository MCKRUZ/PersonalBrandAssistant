import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { NewsService } from '../services/news.service';
import { SavedNewsItem } from '../models/news.model';

interface SavedItemsState {
  readonly items: readonly SavedNewsItem[];
  readonly loading: boolean;
}

const initialState: SavedItemsState = {
  items: [],
  loading: false,
};

export const SavedItemsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, newsService = inject(NewsService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          newsService.getSavedItems().pipe(
            tapResponse({
              next: (items) => patchState(store, { items, loading: false }),
              error: () => patchState(store, { loading: false }),
            })
          )
        )
      )
    ),

    save: rxMethod<string>(
      pipe(
        switchMap((trendItemId) =>
          newsService.saveItem(trendItemId).pipe(
            tapResponse({
              next: (saved) =>
                patchState(store, { items: [...store.items(), saved] }),
              error: () => {},
            })
          )
        )
      )
    ),

    remove: rxMethod<string>(
      pipe(
        switchMap((id) =>
          newsService.removeSavedItem(id).pipe(
            tapResponse({
              next: () =>
                patchState(store, {
                  items: store.items().filter((i) => i.id !== id),
                }),
              error: () => {},
            })
          )
        )
      )
    ),
  }))
);
