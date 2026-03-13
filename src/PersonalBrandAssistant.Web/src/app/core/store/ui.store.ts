import { signalStore, withMethods, withState } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';

type Theme = 'light' | 'dark';

interface UiState {
  sidebarCollapsed: boolean;
  theme: Theme;
}

const initialState: UiState = {
  sidebarCollapsed: false,
  theme: 'light',
};

export const UiStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => ({
    toggleSidebar(): void {
      patchState(store, { sidebarCollapsed: !store.sidebarCollapsed() });
    },
    setTheme(theme: Theme): void {
      patchState(store, { theme });
    },
  }))
);
