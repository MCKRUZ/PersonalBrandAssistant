import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { Content, ContentStatus, ContentType, BrandVoiceScore, WorkflowTransitionLog } from '../../../shared/models';
import { ContentService } from '../services/content.service';

interface ContentFilters {
  readonly contentType?: ContentType;
  readonly status?: ContentStatus;
}

interface ContentState {
  readonly items: readonly Content[];
  readonly cursor: string | undefined;
  readonly hasMore: boolean;
  readonly selectedContent: Content | undefined;
  readonly allowedTransitions: readonly ContentStatus[];
  readonly brandVoiceScore: BrandVoiceScore | undefined;
  readonly workflowLog: readonly WorkflowTransitionLog[];
  readonly loading: boolean;
  readonly saving: boolean;
  readonly filters: ContentFilters;
}

const initialState: ContentState = {
  items: [],
  cursor: undefined,
  hasMore: false,
  selectedContent: undefined,
  allowedTransitions: [],
  brandVoiceScore: undefined,
  workflowLog: [],
  loading: false,
  saving: false,
  filters: {},
};

export const ContentStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(store => ({
    filteredItems: computed(() => store.items()),
    hasContent: computed(() => store.items().length > 0),
  })),
  withMethods((store, contentService = inject(ContentService)) => ({
    loadContent: rxMethod<ContentFilters>(
      pipe(
        tap(filters => patchState(store, { loading: true, filters, items: [], cursor: undefined, hasMore: false })),
        switchMap(filters =>
          contentService.getAll({ ...filters, pageSize: 20 }).pipe(
            tapResponse({
              next: result => patchState(store, {
                items: result.items,
                cursor: result.cursor,
                hasMore: result.hasMore,
                loading: false,
              }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadMore: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          contentService.getAll({ ...store.filters(), pageSize: 20, cursor: store.cursor() }).pipe(
            tapResponse({
              next: result => patchState(store, {
                items: [...store.items(), ...result.items],
                cursor: result.cursor,
                hasMore: result.hasMore,
                loading: false,
              }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadContentById: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loading: true, selectedContent: undefined, allowedTransitions: [], brandVoiceScore: undefined, workflowLog: [] })),
        switchMap(id =>
          contentService.getById(id).pipe(
            tapResponse({
              next: content => patchState(store, { selectedContent: content, loading: false }),
              error: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadTransitions: rxMethod<string>(
      pipe(
        switchMap(id =>
          contentService.getAllowedTransitions(id).pipe(
            tapResponse({
              next: transitions => patchState(store, { allowedTransitions: transitions }),
              error: () => patchState(store, { allowedTransitions: [] }),
            }),
          ),
        ),
      ),
    ),

    loadBrandVoice: rxMethod<string>(
      pipe(
        switchMap(id =>
          contentService.getBrandVoiceScore(id).pipe(
            tapResponse({
              next: score => patchState(store, { brandVoiceScore: score }),
              error: () => patchState(store, { brandVoiceScore: undefined }),
            }),
          ),
        ),
      ),
    ),

    loadWorkflowLog: rxMethod<string>(
      pipe(
        switchMap(id =>
          contentService.getAuditLog(id).pipe(
            tapResponse({
              next: log => patchState(store, { workflowLog: log }),
              error: () => patchState(store, { workflowLog: [] }),
            }),
          ),
        ),
      ),
    ),

    setSaving(saving: boolean) {
      patchState(store, { saving });
    },

    clearSelected() {
      patchState(store, { selectedContent: undefined, allowedTransitions: [], brandVoiceScore: undefined, workflowLog: [] });
    },

    updateFilters(filters: ContentFilters) {
      patchState(store, { filters });
    },
  })),
);
