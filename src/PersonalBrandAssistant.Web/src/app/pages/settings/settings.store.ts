import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { AutonomySettings } from '../../core/models/autonomy.model';
import { BrandProfile, DEFAULT_BRAND_PROFILE } from './brand-profile.model';
import { QUICK_PROMPTS, QUICK_PROMPTS_STORAGE_KEY } from './quick-prompts.defaults';
import { SettingsApiService } from './settings-api.service';

interface SettingsState {
  readonly autonomy: AutonomySettings | undefined;
  readonly brandProfile: BrandProfile;
  readonly quickPrompts: Record<string, string[]>;
  readonly loading: boolean;
  readonly saving: boolean;
}

const initialState: SettingsState = {
  autonomy: undefined,
  brandProfile: DEFAULT_BRAND_PROFILE,
  quickPrompts: { ...QUICK_PROMPTS },
  loading: false,
  saving: false,
};

function loadQuickPromptsFromStorage(): Record<string, string[]> {
  try {
    const stored = localStorage.getItem(QUICK_PROMPTS_STORAGE_KEY);
    if (stored) {
      return { ...QUICK_PROMPTS, ...JSON.parse(stored) };
    }
  } catch { /* fall through */ }
  return { ...QUICK_PROMPTS };
}

export const SettingsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, api = inject(SettingsApiService)) => ({
    loadAutonomy: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          api.getAutonomy().pipe(
            tapResponse({
              next: autonomy => patchState(store, { autonomy, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    saveAutonomy(settings: AutonomySettings, onSuccess: () => void, onError: () => void) {
      patchState(store, { saving: true });
      api.updateAutonomy(settings).subscribe({
        next: autonomy => {
          patchState(store, { autonomy, saving: false });
          onSuccess();
        },
        error: () => {
          patchState(store, { saving: false });
          onError();
        },
      });
    },

    loadBrandProfile() {
      patchState(store, { brandProfile: DEFAULT_BRAND_PROFILE });
    },

    updateBrandProfile(profile: BrandProfile) {
      patchState(store, { brandProfile: profile });
    },

    loadQuickPrompts() {
      patchState(store, { quickPrompts: loadQuickPromptsFromStorage() });
    },

    updateQuickPrompts(routeKey: string, prompts: string[]) {
      const updated = { ...store.quickPrompts(), [routeKey]: prompts };
      localStorage.setItem(QUICK_PROMPTS_STORAGE_KEY, JSON.stringify(updated));
      patchState(store, { quickPrompts: updated });
    },

    resetQuickPrompts() {
      localStorage.removeItem(QUICK_PROMPTS_STORAGE_KEY);
      patchState(store, { quickPrompts: { ...QUICK_PROMPTS } });
    },
  })),
);
