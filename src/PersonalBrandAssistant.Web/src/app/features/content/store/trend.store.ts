import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { TrendSuggestion } from '../../../shared/models';
import { TrendService } from '../services/trend.service';

interface TrendState {
  readonly suggestions: readonly TrendSuggestion[];
  readonly loading: boolean;
  readonly refreshing: boolean;
}

const initialState: TrendState = {
  suggestions: [],
  loading: false,
  refreshing: false,
};

export const TrendStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, trendService = inject(TrendService)) => ({
    loadSuggestions: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          trendService.getSuggestions().pipe(
            tapResponse({
              next: suggestions => patchState(store, { suggestions, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    refresh: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { refreshing: true })),
        switchMap(() =>
          trendService.refresh().pipe(
            tapResponse({
              next: () => patchState(store, { refreshing: false }),
              error: () => patchState(store, { refreshing: false }),
            }),
          ),
        ),
      ),
    ),

    accept: rxMethod<string>(
      pipe(
        switchMap(id =>
          trendService.accept(id).pipe(
            tapResponse({
              next: () => patchState(store, {
                suggestions: store.suggestions().filter(s => s.id !== id),
              }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),

    dismiss: rxMethod<string>(
      pipe(
        switchMap(id =>
          trendService.dismiss(id).pipe(
            tapResponse({
              next: () => patchState(store, {
                suggestions: store.suggestions().filter(s => s.id !== id),
              }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),
  })),
);
