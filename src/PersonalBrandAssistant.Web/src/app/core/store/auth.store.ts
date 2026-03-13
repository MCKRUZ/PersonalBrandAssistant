import { signalStore, withMethods, withState } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';

interface AuthState {
  displayName: string;
  email: string;
}

const initialState: AuthState = {
  displayName: '',
  email: '',
};

export const AuthStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => ({
    setUser(displayName: string, email: string): void {
      patchState(store, { displayName, email });
    },
  }))
);
