import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap, of } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { AutomationService } from '../services/automation.service';
import { AutomationRun, AutomationConfig } from '../../../shared/models';

interface AutomationState {
  readonly runs: readonly AutomationRun[];
  readonly config: AutomationConfig | null;
  readonly loading: boolean;
  readonly triggering: boolean;
  readonly lastTriggerError: string | null;
}

const initialState: AutomationState = {
  runs: [],
  config: null,
  loading: false,
  triggering: false,
  lastTriggerError: null,
};

export const AutomationStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(store => ({
    hasRunningPipeline: computed(() =>
      store.runs().some(r => r.status === 'Running')),
    lastRun: computed(() =>
      store.runs().length > 0 ? store.runs()[0] : null),
  })),
  withMethods((store, service = inject(AutomationService)) => ({
    loadRuns: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          service.getRuns(50).pipe(
            tapResponse({
              next: runs => patchState(store, { runs, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadConfig: rxMethod<void>(
      pipe(
        switchMap(() =>
          service.getConfig().pipe(
            tapResponse({
              next: config => patchState(store, { config }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),

    triggerRun: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { triggering: true, lastTriggerError: null })),
        switchMap(() =>
          service.trigger().pipe(
            tapResponse({
              next: () => patchState(store, { triggering: false }),
              error: (err: any) => patchState(store, {
                triggering: false,
                lastTriggerError: err?.error?.detail ?? 'Trigger failed',
              }),
            }),
          ),
        ),
      ),
    ),

    deleteRun: rxMethod<string>(
      pipe(
        switchMap((id) => {
          patchState(store, { runs: store.runs().filter(r => r.id !== id) });
          return service.deleteRun(id).pipe(
            tapResponse({
              next: () => {},
              error: () => {
                // Reload on failure to restore
                return of(undefined);
              },
            }),
          );
        }),
      ),
    ),

    clearRuns: rxMethod<void>(
      pipe(
        switchMap(() =>
          service.clearRuns().pipe(
            tapResponse({
              next: () => patchState(store, {
                runs: store.runs().filter(r => r.status === 'Running'),
              }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),
  })),
);
