import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { AgentBudget, AgentUsage } from '../../../shared/models';
import { AgentService } from '../../../shared/services/agent.service';

interface SettingsState {
  readonly budget: AgentBudget | undefined;
  readonly usage: AgentUsage | undefined;
  readonly loading: boolean;
}

const initialState: SettingsState = {
  budget: undefined,
  usage: undefined,
  loading: false,
};

export const SettingsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, agentService = inject(AgentService)) => ({
    loadBudget: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          agentService.getBudget().pipe(
            tapResponse({
              next: budget => patchState(store, { budget, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadUsage: rxMethod<{ from: string; to: string }>(
      pipe(
        switchMap(({ from, to }) =>
          agentService.getUsage(from, to).pipe(
            tapResponse({
              next: usage => patchState(store, { usage }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),
  })),
);
