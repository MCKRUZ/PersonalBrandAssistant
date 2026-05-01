import { signalStore, withMethods, withState } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';

type Theme = 'light' | 'dark';

interface UiState {
  sidebarCollapsed: boolean;
  sidecarOpen: boolean;
  theme: Theme;
}

const SIDEBAR_KEY = 'pba-sidebar-collapsed';

const initialState: UiState = {
  sidebarCollapsed: typeof localStorage !== 'undefined' && localStorage.getItem(SIDEBAR_KEY) === 'true',
  sidecarOpen: false,
  theme: 'dark',
};

export const UiStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => ({
    toggleSidebar(): void {
      const next = !store.sidebarCollapsed();
      patchState(store, { sidebarCollapsed: next });
      localStorage.setItem(SIDEBAR_KEY, String(next));
    },
    toggleSidecar(): void {
      patchState(store, { sidecarOpen: !store.sidecarOpen() });
    },
    setTheme(theme: Theme): void {
      patchState(store, { theme });
    },
  }))
);
