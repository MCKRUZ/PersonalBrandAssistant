import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { Platform, PlatformType } from '../../../shared/models';
import { PlatformService } from '../services/platform.service';

interface PlatformState {
  readonly platforms: readonly Platform[];
  readonly selectedStatus?: Platform;
  readonly loading: boolean;
  readonly connecting: boolean;
}

const initialState: PlatformState = {
  platforms: [],
  loading: false,
  connecting: false,
};

export const PlatformStore = signalStore(
  { providedIn: 'root' },
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

    loadStatus: rxMethod<PlatformType>(
      pipe(
        switchMap(type =>
          platformService.getStatus(type).pipe(
            tapResponse({
              next: status => patchState(store, { selectedStatus: status }),
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
);
