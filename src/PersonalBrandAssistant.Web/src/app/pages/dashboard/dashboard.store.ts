import { inject } from '@angular/core';
import { signalStore, withState, withMethods } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ContentItem } from '../../core/models/content.model';
import { CalendarSlot } from '../../core/models/calendar.model';
import { DashboardApiService, DashboardKpis, AiSuggestion } from './dashboard-api.service';

interface DashboardState {
  readonly kpis: DashboardKpis | undefined;
  readonly schedule: readonly CalendarSlot[];
  readonly recentItems: readonly ContentItem[];
  readonly suggestions: readonly AiSuggestion[];
  readonly isLoading: boolean;
  readonly error: string | undefined;
}

export const DashboardStore = signalStore(
  withState<DashboardState>({
    kpis: undefined,
    schedule: [],
    recentItems: [],
    suggestions: [],
    isLoading: false,
    error: undefined,
  }),
  withMethods((store) => {
    const api = inject(DashboardApiService);

    return {
      load(): void {
        patchState(store, { isLoading: true, error: undefined });

        forkJoin({
          kpis: api.getKpis().pipe(catchError(() => of(undefined))),
          schedule: api.getTodaySchedule().pipe(catchError(() => of([] as CalendarSlot[]))),
          recentItems: api.getRecentItems().pipe(catchError(() => of([] as ContentItem[]))),
          suggestions: api.getSuggestions().pipe(catchError(() => of([] as AiSuggestion[]))),
        }).subscribe({
          next: (data) => {
            patchState(store, {
              kpis: data.kpis,
              schedule: data.schedule,
              recentItems: data.recentItems,
              suggestions: data.suggestions,
              isLoading: false,
            });
          },
          error: (err) => {
            patchState(store, {
              isLoading: false,
              error: err?.message ?? 'Failed to load dashboard data',
            });
          },
        });
      },
    };
  }),
);
