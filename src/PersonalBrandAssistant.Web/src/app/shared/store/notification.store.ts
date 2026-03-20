import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { Notification } from '../models';
import { NotificationService } from '../services/notification.service';

interface NotificationState {
  readonly notifications: readonly Notification[];
  readonly loading: boolean;
}

const initialState: NotificationState = {
  notifications: [],
  loading: false,
};

export const NotificationStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(store => ({
    unreadCount: computed(() => store.notifications().filter(n => !n.isRead).length),
    unread: computed(() => store.notifications().filter(n => !n.isRead)),
  })),
  withMethods((store, notificationService = inject(NotificationService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          notificationService.getAll({ pageSize: 20 }).pipe(
            tapResponse({
              next: notifications => patchState(store, { notifications, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    markRead: rxMethod<string>(
      pipe(
        switchMap(id =>
          notificationService.markRead(id).pipe(
            tapResponse({
              next: () => patchState(store, {
                notifications: store.notifications().map(n =>
                  n.id === id ? { ...n, isRead: true } : n
                ),
              }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),

    markAllRead: rxMethod<void>(
      pipe(
        switchMap(() =>
          notificationService.markAllRead().pipe(
            tapResponse({
              next: () => patchState(store, {
                notifications: store.notifications().map(n => ({ ...n, isRead: true })),
              }),
              error: () => {},
            }),
          ),
        ),
      ),
    ),
  })),
);
