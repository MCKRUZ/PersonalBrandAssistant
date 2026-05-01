import { inject, DestroyRef } from '@angular/core';
import { signalStore, withState, withMethods } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';
import { Subject, switchMap, debounceTime, concatMap, of, EMPTY } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ContentItem } from '../../core/models/content.model';
import { BrandVoiceScore } from '../../core/models/brand-voice.model';
import { AgentExecution } from '../../core/models/agent.model';
import { ContentEditorApiService, CreateContentRequest, UpdateContentRequest } from './content-editor-api.service';

interface ContentEditorState {
  readonly content: ContentItem | undefined;
  readonly brandScore: BrandVoiceScore | undefined;
  readonly versions: readonly ContentItem[];
  readonly executionHistory: readonly AgentExecution[];
  readonly isLoading: boolean;
  readonly isSaving: boolean;
  readonly isScoring: boolean;
  readonly saveError: 'conflict' | 'network' | null;
  readonly activeTab: 'preview' | 'history' | 'versions';
}

export const ContentEditorStore = signalStore(
  withState<ContentEditorState>({
    content: undefined,
    brandScore: undefined,
    versions: [],
    executionHistory: [],
    isLoading: false,
    isSaving: false,
    isScoring: false,
    saveError: null,
    activeTab: 'preview',
  }),
  withMethods((store) => {
    const api = inject(ContentEditorApiService);
    const destroyRef = inject(DestroyRef);
    const saveSubject = new Subject<UpdateContentRequest>();

    saveSubject.pipe(
      debounceTime(1000),
      concatMap((request) => {
        const content = store.content();
        if (!content) return EMPTY;
        patchState(store, { isSaving: true });
        return api.update(content.id, request, content.version).pipe(
          catchError((err) => {
            const saveError = err.status === 409 ? 'conflict' as const : 'network' as const;
            patchState(store, { isSaving: false, saveError });
            return EMPTY;
          }),
        );
      }),
      takeUntilDestroyed(destroyRef),
    ).subscribe(() => {
      const current = store.content();
      if (current) {
        patchState(store, {
          isSaving: false,
          saveError: null,
          content: { ...current, version: current.version + 1 },
        });
      }
    });

    return {
      loadContent(id: string): void {
        patchState(store, { isLoading: true, saveError: null });
        api.getById(id).pipe(
          catchError(() => {
            patchState(store, { isLoading: false });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe((content) => {
          patchState(store, { content, isLoading: false });
        });
      },

      createContent(request: CreateContentRequest): void {
        patchState(store, { isLoading: true });
        api.create(request).pipe(
          switchMap(({ id }) => api.getById(id)),
          catchError(() => {
            patchState(store, { isLoading: false });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe((content) => {
          patchState(store, { content, isLoading: false });
        });
      },

      updateField(field: keyof UpdateContentRequest, value: string): void {
        const current = store.content();
        if (!current) return;
        patchState(store, { content: { ...current, [field]: value }, saveError: null });
        saveSubject.next({ [field]: value });
      },

      scoreContent(): void {
        const content = store.content();
        if (!content) return;
        patchState(store, { isScoring: true });
        api.scoreContent(content.id).pipe(
          catchError(() => {
            patchState(store, { isScoring: false });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe((brandScore) => {
          patchState(store, { brandScore, isScoring: false });
        });
      },

      approveAndPublish(): void {
        const content = store.content();
        if (!content) return;
        api.approve(content.id).pipe(
          switchMap(() => api.publish(content.id)),
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, {
            content: { ...store.content()!, status: 'Published' },
          });
        });
      },

      scheduleContent(scheduledAt: string): void {
        const content = store.content();
        if (!content) return;
        api.schedule(content.id, scheduledAt).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, {
            content: { ...store.content()!, status: 'Scheduled', scheduledAt },
          });
        });
      },

      loadHistory(): void {
        const content = store.content();
        if (!content) return;
        api.getExecutionHistory(content.id).pipe(
          catchError(() => of([] as AgentExecution[])),
          takeUntilDestroyed(destroyRef),
        ).subscribe((executionHistory) => {
          patchState(store, { executionHistory });
        });
      },

      setActiveTab(tab: 'preview' | 'history' | 'versions'): void {
        patchState(store, { activeTab: tab });
        if (tab === 'history') {
          const content = store.content();
          if (content && store.executionHistory().length === 0) {
            api.getExecutionHistory(content.id).pipe(
              catchError(() => of([] as AgentExecution[])),
              takeUntilDestroyed(destroyRef),
            ).subscribe((executionHistory) => {
              patchState(store, { executionHistory });
            });
          }
        }
      },

      applyDraft(text: string): void {
        const current = store.content();
        if (!current) return;
        patchState(store, { content: { ...current, body: text } });
        saveSubject.next({ body: text });
      },
    };
  }),
);
