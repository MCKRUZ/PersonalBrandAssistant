import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState, withHooks } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { CalendarSlot } from '../../shared/models';
import { PlatformType } from '../../shared/models';
import { CalendarApiService } from './calendar-api.service';

interface DateRange {
  readonly from: string;
  readonly to: string;
}

type ViewMode = 'week' | 'month';

interface CalendarState {
  readonly slots: readonly CalendarSlot[];
  readonly dateRange: DateRange;
  readonly viewMode: ViewMode;
  readonly platformFilter: PlatformType | null;
  readonly selectedSlot: CalendarSlot | undefined;
  readonly loading: boolean;
}

export function getWeekRange(date: Date): DateRange {
  const d = new Date(date);
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(d);
  monday.setDate(d.getDate() + diff);
  monday.setHours(0, 0, 0, 0);
  const sunday = new Date(monday);
  sunday.setDate(monday.getDate() + 6);
  sunday.setHours(23, 59, 59, 999);
  return { from: monday.toISOString(), to: sunday.toISOString() };
}

export function getMonthRange(date: Date): DateRange {
  const from = new Date(date.getFullYear(), date.getMonth(), 1);
  const to = new Date(date.getFullYear(), date.getMonth() + 1, 0, 23, 59, 59);
  return { from: from.toISOString(), to: to.toISOString() };
}

const initialState: CalendarState = {
  slots: [],
  dateRange: getWeekRange(new Date()),
  viewMode: 'week',
  platformFilter: null,
  selectedSlot: undefined,
  loading: false,
};

export const CalendarStore = signalStore(
  withState(initialState),
  withComputed(store => ({
    filteredSlots: computed(() => {
      const filter = store.platformFilter();
      if (!filter) return store.slots();
      return store.slots().filter(s => s.platform === filter);
    }),
    slotsByDate: computed(() => {
      const filter = store.platformFilter();
      const slots = filter ? store.slots().filter(s => s.platform === filter) : store.slots();
      const map = new Map<string, CalendarSlot[]>();
      for (const slot of slots) {
        const d = new Date(slot.scheduledAt);
        const dateKey = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
        const existing = map.get(dateKey) ?? [];
        map.set(dateKey, [...existing, slot]);
      }
      return map;
    }),
    dateLabel: computed(() => {
      const range = store.dateRange();
      const from = new Date(range.from);
      if (store.viewMode() === 'month') {
        return from.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
      }
      const to = new Date(range.to);
      const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
      const fromStr = from.toLocaleDateString('en-US', opts);
      const toStr = to.toLocaleDateString('en-US', { ...opts, year: 'numeric' });
      return `${fromStr} - ${toStr}`;
    }),
  })),
  withMethods((store, api = inject(CalendarApiService)) => ({
    loadSlots: rxMethod<DateRange>(
      pipe(
        tap(range => patchState(store, { loading: true, dateRange: range })),
        switchMap(range =>
          api.getSlots(range.from, range.to).pipe(
            tapResponse({
              next: slots => patchState(store, { slots, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    setViewMode(mode: ViewMode) {
      const currentFrom = new Date(store.dateRange().from);
      const range = mode === 'week' ? getWeekRange(currentFrom) : getMonthRange(currentFrom);
      patchState(store, { viewMode: mode, dateRange: range });
    },

    navigate(offset: number) {
      const current = new Date(store.dateRange().from);
      let range: DateRange;
      if (store.viewMode() === 'week') {
        const next = new Date(current);
        next.setDate(current.getDate() + offset * 7);
        range = getWeekRange(next);
      } else {
        const next = new Date(current.getFullYear(), current.getMonth() + offset, 1);
        range = getMonthRange(next);
      }
      patchState(store, { dateRange: range });
    },

    filterByPlatform(platform: PlatformType | null) {
      patchState(store, { platformFilter: platform });
    },

    selectSlot(slot: CalendarSlot | undefined) {
      patchState(store, { selectedSlot: slot });
    },

    createSlot(request: { scheduledAt: string; platform: PlatformType }) {
      const range = store.dateRange();
      api.createSlot(request).pipe(
        switchMap(() => api.getSlots(range.from, range.to)),
        tapResponse({
          next: slots => patchState(store, { slots }),
          error: () => {},
        }),
      ).subscribe();
    },

    assignContent(slotId: string, contentId: string) {
      const range = store.dateRange();
      api.assignContent(slotId, contentId).pipe(
        switchMap(() => api.getSlots(range.from, range.to)),
        tapResponse({
          next: slots => patchState(store, { slots }),
          error: () => {},
        }),
      ).subscribe();
    },

    autoFill() {
      const range = store.dateRange();
      api.autoFill(range.from, range.to).pipe(
        switchMap(() => api.getSlots(range.from, range.to)),
        tapResponse({
          next: slots => patchState(store, { slots }),
          error: () => {},
        }),
      ).subscribe();
    },
  })),
  withHooks({
    onInit(store) {
      store.loadSlots(store.dateRange());
    },
  }),
);
