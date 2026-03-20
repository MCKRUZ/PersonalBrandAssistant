import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { CalendarSlot, ContentSeries } from '../../../shared/models';
import { CalendarService } from '../services/calendar.service';

interface DateRange {
  readonly from: string;
  readonly to: string;
}

interface CalendarState {
  readonly slots: readonly CalendarSlot[];
  readonly series: readonly ContentSeries[];
  readonly dateRange: DateRange;
  readonly selectedSlot?: CalendarSlot;
  readonly loading: boolean;
}

function getMonthRange(date: Date): DateRange {
  const from = new Date(date.getFullYear(), date.getMonth(), 1);
  const to = new Date(date.getFullYear(), date.getMonth() + 1, 0, 23, 59, 59);
  return { from: from.toISOString(), to: to.toISOString() };
}

const initialState: CalendarState = {
  slots: [],
  series: [],
  dateRange: getMonthRange(new Date()),
  loading: false,
};

export const CalendarStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(store => ({
    slotsByDate: computed(() => {
      const map = new Map<string, CalendarSlot[]>();
      for (const slot of store.slots()) {
        const dateKey = new Date(slot.scheduledAt).toISOString().split('T')[0];
        const existing = map.get(dateKey) ?? [];
        map.set(dateKey, [...existing, slot]);
      }
      return map;
    }),
  })),
  withMethods((store, calendarService = inject(CalendarService)) => ({
    loadSlots: rxMethod<DateRange>(
      pipe(
        tap(range => patchState(store, { loading: true, dateRange: range })),
        switchMap(range =>
          calendarService.getSlots(range.from, range.to).pipe(
            tapResponse({
              next: slots => patchState(store, { slots, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    navigateMonth(offset: number) {
      const current = new Date(store.dateRange().from);
      const next = new Date(current.getFullYear(), current.getMonth() + offset, 1);
      const range = getMonthRange(next);
      patchState(store, { dateRange: range });
      return range;
    },

    selectSlot(slot: CalendarSlot | undefined) {
      patchState(store, { selectedSlot: slot });
    },
  })),
);
