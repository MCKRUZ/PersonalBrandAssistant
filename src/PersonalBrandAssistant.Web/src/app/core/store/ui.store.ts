import { signalStore, withMethods, withState } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';

type Theme = 'light' | 'dark';

interface UiState {
  sidebarCollapsed: boolean;
  sidecarOpen: boolean;
  theme: Theme;
}

const initialState: UiState = {
  sidebarCollapsed: false,
  sidecarOpen: false,
  theme: 'light',
};

export const UiStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => ({
    toggleSidebar(): void {
      patchState(store, { sidebarCollapsed: !store.sidebarCollapsed() });
    },
    toggleSidecar(): void {
      patchState(store, { sidecarOpen: !store.sidecarOpen() });
    },
    setTheme(theme: Theme): void {
      patchState(store, { theme });
    },
  }))
);
