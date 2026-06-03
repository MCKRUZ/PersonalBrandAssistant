import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { ContentService } from '../services/content.service';
import { Content, ContentFilterState, ContentStatus } from '../models/content.model';
import { LEGAL_TRANSITIONS } from '../content-list/content-display.utils';

// Upper bound for the single load-all fetch (the redesign renders the full pipeline client-side).
const LOAD_ALL_PAGE_SIZE = 1000;

const ALL_STATUSES: ContentStatus[] = [
  ContentStatus.Idea,
  ContentStatus.Draft,
  ContentStatus.Review,
  ContentStatus.Approved,
  ContentStatus.Scheduled,
  ContentStatus.Published,
  ContentStatus.Archived,
];

type ContentStoreState = {
  // Load-all source of truth (Content Studio redesign).
  allContents: Content[];
  activeStatus: ContentStatus | null;
  search: string;
  filters: Partial<ContentFilterState>;
  viewMode: 'board' | 'grid' | 'table';

  loading: boolean;
  error: string | null;
};

const initialState: ContentStoreState = {
  allContents: [],
  activeStatus: null,
  search: '',
  filters: {},
  viewMode: 'board',
  loading: false,
  error: null,
};

function emptyCounts(): Record<ContentStatus, number> {
  return ALL_STATUSES.reduce(
    (acc, s) => ((acc[s] = 0), acc),
    {} as Record<ContentStatus, number>
  );
}

export const ContentStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((state) => ({
    counts: computed(() => {
      const counts = emptyCounts();
      for (const c of state.allContents()) counts[c.status]++;
      return counts;
    }),
    filtered: computed(() => {
      // Search comes from the top-level `search` signal (the redesign's search box), NOT
      // `filters.search` — the popover only contributes platform/type/date.
      const status = state.activeStatus();
      const term = state.search().trim().toLowerCase();
      const f = state.filters();
      return state.allContents().filter((c) => {
        if (status && c.status !== status) return false;
        if (term) {
          const inTitle = c.title.toLowerCase().includes(term);
          const inTags = c.tags.some((t) => t.toLowerCase().includes(term));
          if (!inTitle && !inTags) return false;
        }
        if (f.platform && c.primaryPlatform !== f.platform && !c.targetPlatforms.includes(f.platform))
          return false;
        if (f.contentType && c.contentType !== f.contentType) return false;
        if (f.dateFrom && c.createdAt < f.dateFrom) return false;
        if (f.dateTo && c.createdAt > f.dateTo) return false;
        return true;
      });
    }),
  })),
  withComputed((state) => ({
    // board columns, grouped from the filtered set
    byStatus: computed(() => {
      const groups = ALL_STATUSES.reduce(
        (acc, s) => ((acc[s] = [] as Content[]), acc),
        {} as Record<ContentStatus, Content[]>
      );
      for (const c of state.filtered()) groups[c.status].push(c);
      return groups;
    }),
  })),
  withMethods((store) => {
    const contentService = inject(ContentService);

    // The redesign's single fetch: pull the whole pipeline, render/filter client-side.
    const loadAll = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          contentService.list({}, 1, LOAD_ALL_PAGE_SIZE).pipe(
            tapResponse({
              next: (result) =>
                patchState(store, { allContents: result.items, loading: false }),
              error: (err: Error) => patchState(store, { loading: false, error: err.message }),
            })
          )
        )
      )
    );

    /** Map a legal (current -> target) status move to its ContentService endpoint. */
    function endpointFor(
      current: ContentStatus,
      target: ContentStatus,
      id: string
    ): ReturnType<ContentService['approve']> | null {
      if (current === ContentStatus.Idea && target === ContentStatus.Draft)
        return contentService.draft(id, { action: 'draft' });
      if (current === ContentStatus.Draft && target === ContentStatus.Review)
        return contentService.submitForReview(id);
      if (current === ContentStatus.Draft && target === ContentStatus.Approved)
        return contentService.approve(id);
      if (current === ContentStatus.Review && target === ContentStatus.Approved)
        return contentService.approve(id);
      if (current === ContentStatus.Review && target === ContentStatus.Draft)
        return contentService.requestChanges(id);
      if (current === ContentStatus.Approved && target === ContentStatus.Published)
        return contentService.publish(id);
      if (current === ContentStatus.Scheduled && target === ContentStatus.Approved)
        return contentService.unschedule(id);
      if (current === ContentStatus.Scheduled && target === ContentStatus.Published)
        return contentService.publish(id);
      if (current === ContentStatus.Published && target === ContentStatus.Approved)
        return contentService.unpublish(id);
      return null;
    }

    function patchStatus(id: string, status: ContentStatus): void {
      patchState(store, {
        allContents: store.allContents().map((c) => (c.id === id ? { ...c, status } : c)),
      });
    }

    return {
      loadAll,

      setActiveStatus(status: ContentStatus | null): void {
        patchState(store, { activeStatus: store.activeStatus() === status ? null : status });
      },
      setSearch(term: string): void {
        patchState(store, { search: term });
      },
      setView(mode: 'board' | 'grid' | 'table'): void {
        patchState(store, { viewMode: mode });
      },

      /**
       * Dispatch a status change through the legal state-machine endpoint (NOT a raw update —
       * UpdateContentRequest has no status field). Optimistically patches only `status`, reloads
       * the affected record on success for the authoritative updatedAt, rolls back on error.
       * Approved -> Scheduled is a no-op here: scheduling needs a date, so the caller opens the
       * schedule dialog and calls ContentService.schedule directly.
       */
      transition(id: string, target: ContentStatus): void {
        const record = store.allContents().find((c) => c.id === id);
        if (!record) return;
        const current = record.status;

        if (current === ContentStatus.Approved && target === ContentStatus.Scheduled) return;

        const restore =
          current === ContentStatus.Archived; // Archived -> any restored status uses restore()
        const legal = restore || LEGAL_TRANSITIONS[current].includes(target);
        if (!legal) {
          patchState(store, { error: `Cannot move ${current} to ${target}.` });
          return;
        }

        const call = restore ? contentService.restore(id) : endpointFor(current, target, id);
        if (!call) {
          patchState(store, { error: `No transition endpoint for ${current} -> ${target}.` });
          return;
        }

        patchStatus(id, target); // optimistic: status only, keep updatedAt
        call.subscribe({
          next: () => {
            contentService.get(id).subscribe({
              next: (detail) =>
                patchState(store, {
                  allContents: store
                    .allContents()
                    .map((c) =>
                      c.id === id ? { ...c, status: detail.status, updatedAt: detail.updatedAt } : c
                    ),
                }),
              error: () => {
                /* status already applied optimistically; ignore reload failure */
              },
            });
          },
          error: (err: Error) => {
            patchStatus(id, current); // rollback
            patchState(store, { error: err.message });
          },
        });
      },

      /** Merge popover filters (platform/type/date) into the client-side `filtered()` set. */
      setFilter<K extends keyof ContentFilterState>(key: K, value: ContentFilterState[K]): void {
        patchState(store, { filters: { ...store.filters(), [key]: value } });
      },

      deleteContent(id: string): void {
        contentService.delete(id).subscribe({
          next: () => loadAll(),
          error: (err: Error) => patchState(store, { error: err.message }),
        });
      },
    };
  })
);
