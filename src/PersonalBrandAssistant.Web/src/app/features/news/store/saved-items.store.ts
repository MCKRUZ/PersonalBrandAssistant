import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { NewsService } from '../services/news.service';
import { NewsFeedItem, ideaToFeedItem } from '../models/news.model';

interface SavedItemsState {
  readonly items: readonly NewsFeedItem[];
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
          newsService.getSavedIdeas().pipe(
            tapResponse({
              next: (ideas) => patchState(store, { items: ideas.map(ideaToFeedItem), loading: false }),
              error: () => patchState(store, { loading: false }),
            })
          )
        )
      )
    ),

    remove: rxMethod<string>(
      pipe(
        switchMap((id) =>
          newsService.dismissIdea(id).pipe(
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
