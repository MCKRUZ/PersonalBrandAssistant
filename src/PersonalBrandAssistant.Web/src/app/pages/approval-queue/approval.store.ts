import { computed, inject, DestroyRef } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, withHooks } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';
import { EMPTY } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ContentItem } from '../../core/models/content.model';
import { PlatformType } from '../../core/models/platform.model';
import { ApprovalApiService } from './approval-api.service';

interface ApprovalState {
  readonly items: readonly ContentItem[];
  readonly selectedIds: readonly string[];
  readonly platformFilter: PlatformType | null;
  readonly isLoading: boolean;
  readonly error: string | null;
}

export const ApprovalStore = signalStore(
  withState<ApprovalState>({
    items: [],
    selectedIds: [],
    platformFilter: null,
    isLoading: false,
    error: null,
  }),
  withComputed((store) => ({
    filteredItems: computed(() => {
      const filter = store.platformFilter();
      if (!filter) return store.items();
      return store.items().filter(item => item.platform === filter);
    }),
    hasSelection: computed(() => store.selectedIds().length > 0),
    selectedCount: computed(() => store.selectedIds().length),
    pendingCount: computed(() => store.items().length),
  })),
  withMethods((store) => {
    const api = inject(ApprovalApiService);
    const destroyRef = inject(DestroyRef);

    return {
      loadPending(): void {
        patchState(store, { isLoading: true, error: null });
        api.getPending().pipe(
          catchError((err) => {
            patchState(store, { isLoading: false, error: err.message ?? 'Failed to load' });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe((items) => {
          patchState(store, { items, selectedIds: [], platformFilter: null, isLoading: false });
        });
      },

      approve(id: string): void {
        api.approve(id).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, {
            items: store.items().filter(i => i.id !== id),
            selectedIds: store.selectedIds().filter(sid => sid !== id),
          });
        });
      },

      reject(id: string, feedback: string): void {
        api.reject(id, feedback).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, {
            items: store.items().filter(i => i.id !== id),
          });
        });
      },

      batchApprove(): void {
        const ids = store.selectedIds();
        if (ids.length === 0) return;
        api.batchApprove(ids).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, {
            items: store.items().filter(i => !ids.includes(i.id)),
            selectedIds: [],
          });
        });
      },

      filterByPlatform(platform: PlatformType | null): void {
        patchState(store, { platformFilter: platform });
      },

      toggleSelection(id: string): void {
        const current = store.selectedIds();
        const next = current.includes(id)
          ? current.filter(sid => sid !== id)
          : [...current, id];
        patchState(store, { selectedIds: next });
      },

      selectAll(): void {
        const ids = store.filteredItems().map(i => i.id);
        patchState(store, { selectedIds: ids });
      },

      clearSelection(): void {
        patchState(store, { selectedIds: [] });
      },
    };
  }),
  withHooks({
    onInit(store) {
      store.loadPending();
    },
  }),
);
