import { inject } from '@angular/core';
import { signalStore, withState, withMethods, withHooks, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { Platform, PlatformType } from '../../../shared/models';
import { PlatformService } from '../services/platform.service';

interface PlatformState {
  readonly platforms: readonly Platform[];
  readonly loading: boolean;
  readonly connecting: boolean;
}

const initialState: PlatformState = {
  platforms: [],
  loading: false,
  connecting: false,
};

export const PlatformStore = signalStore(
  withState(initialState),
  withMethods((store, platformService = inject(PlatformService)) => ({
    loadPlatforms: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          platformService.getAll().pipe(
            tapResponse({
              next: platforms => patchState(store, { platforms, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    disconnect: rxMethod<PlatformType>(
      pipe(
        switchMap(type =>
          platformService.disconnect(type).pipe(
            tapResponse({
              next: () => patchState(store, s => ({
                platforms: s.platforms.filter(p => p.type !== type),
              })),
              error: () => {},
            }),
          ),
        ),
      ),
    ),

    setConnecting(connecting: boolean) {
      patchState(store, { connecting });
    },
  })),
  withHooks({
    onInit(store) {
      store.loadPlatforms();
    },
  }),
);
