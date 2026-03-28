import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { Content, TrendSuggestion, CalendarSlot, Notification } from '../../../shared/models';
import { DashboardService } from '../services/dashboard.service';

interface DashboardKpis {
  readonly totalContent: number;
  readonly pendingReview: number;
  readonly publishedThisWeek: number;
}

interface DashboardState {
  readonly recentContent: readonly Content[];
  readonly trendSuggestions: readonly TrendSuggestion[];
  readonly upcomingSlots: readonly CalendarSlot[];
  readonly notifications: readonly Notification[];
  readonly kpis: DashboardKpis;
  readonly loading: boolean;
}

const initialState: DashboardState = {
  recentContent: [],
  trendSuggestions: [],
  upcomingSlots: [],
  notifications: [],
  kpis: { totalContent: 0, pendingReview: 0, publishedThisWeek: 0 },
  loading: false,
};

export const DashboardStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, dashboardService = inject(DashboardService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          dashboardService.loadAll().pipe(
            tapResponse({
              next: data => {
                const now = new Date();
                const weekAgo = new Date(now.getTime() - 7 * 86_400_000);
                const publishedThisWeek = data.recentContent.items.filter(
                  c => c.status === 'Published' && new Date(c.publishedAt ?? c.createdAt) >= weekAgo,
                ).length;
                const pendingReview = data.recentContent.items.filter(c => c.status === 'Review').length;

                patchState(store, {
                  recentContent: data.recentContent.items,
                  trendSuggestions: data.trendSuggestions,
                  upcomingSlots: data.upcomingSlots.slice(0, 5),
                  notifications: data.notifications,
                  kpis: {
                    totalContent: data.recentContent.items.length,
                    pendingReview,
                    publishedThisWeek,
                  },
                  loading: false,
                });
              },
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),
  })),
);
