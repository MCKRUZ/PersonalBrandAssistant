import { inject } from '@angular/core';
import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
import { AnalyticsService } from '../services/analytics.service';

interface DateRange {
  readonly from: string;
  readonly to: string;
}

interface AnalyticsState {
  readonly topContent: readonly TopPerformingContent[];
  readonly selectedReport: ContentPerformanceReport | undefined;
  readonly dateRange: DateRange;
  readonly loading: boolean;
}

function defaultDateRange(): DateRange {
  const to = new Date();
  const from = new Date(to.getTime() - 30 * 86_400_000);
  return { from: from.toISOString(), to: to.toISOString() };
}

const initialState: AnalyticsState = {
  topContent: [],
  selectedReport: undefined,
  dateRange: defaultDateRange(),
  loading: false,
};

export const AnalyticsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store, analyticsService = inject(AnalyticsService)) => ({
    loadTopContent: rxMethod<DateRange>(
      pipe(
        tap(range => patchState(store, { loading: true, dateRange: range })),
        switchMap(range =>
          analyticsService.getTopContent(range.from, range.to).pipe(
            tapResponse({
              next: topContent => patchState(store, { topContent, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadContentReport: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(id =>
          analyticsService.getContentReport(id).pipe(
            tapResponse({
              next: report => patchState(store, { selectedReport: report, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    setDateRange(range: DateRange) {
      patchState(store, { dateRange: range });
    },
  })),
);
