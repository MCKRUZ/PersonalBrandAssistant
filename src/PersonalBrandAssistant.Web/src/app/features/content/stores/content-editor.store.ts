import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withMethods, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { ContentService } from '../services/content.service';
import { ContentDetail, ContentStatus } from '../models/content.model';

export type ChatMessage = {
  role: 'user' | 'assistant';
  content: string;
  timestamp: string;
};

type ContentEditorStoreState = {
  content: ContentDetail | null;
  isDirty: boolean;
  isSaving: boolean;
  chatMessages: ChatMessage[];
  isStreaming: boolean;
  currentTokens: string;
  loading: boolean;
  error: string | null;
};

const initialState: ContentEditorStoreState = {
  content: null,
  isDirty: false,
  isSaving: false,
  chatMessages: [],
  isStreaming: false,
  currentTokens: '',
  loading: false,
  error: null,
};

export const ContentEditorStore = signalStore(
  withState(initialState),
  withComputed((state) => ({
    hasContent: computed(() => state.content() !== null),
    canAutoSave: computed(() => state.isDirty() && !state.isSaving() && state.content() !== null),
    statusActions: computed(() => {
      const content = state.content();
      if (!content) return [];
      switch (content.status) {
        case ContentStatus.Idea:
        case ContentStatus.Draft:
          return ['submitForReview'];
        case ContentStatus.Review:
          return ['approve', 'requestChanges'];
        case ContentStatus.Approved:
          return ['schedule', 'publish'];
        case ContentStatus.Scheduled:
          return ['unschedule'];
        case ContentStatus.Published:
          return ['unpublish'];
        case ContentStatus.Archived:
          return ['restore'];
        default:
          return [];
      }
    }),
  })),
  withMethods((store) => {
    const contentService = inject(ContentService);

    const loadContent = rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap((id) =>
          contentService.get(id).pipe(
            tapResponse({
              next: (content) =>
                patchState(store, {
                  content,
                  loading: false,
                  isDirty: false,
                  chatMessages: [],
                  isStreaming: false,
                  currentTokens: '',
                }),
              error: (err: Error) =>
                patchState(store, { loading: false, error: err.message }),
            })
          )
        )
      )
    );

    return {
      loadContent,
      updateField<K extends keyof ContentDetail>(field: K, value: ContentDetail[K]): void {
        const current = store.content();
        if (!current) return;
        patchState(store, {
          content: { ...current, [field]: value },
          isDirty: true,
        });
      },
      autoSave(): void {
        const content = store.content();
        if (!content || !store.isDirty()) return;
        patchState(store, { isSaving: true, error: null });
        contentService
          .update(content.id, {
            title: content.title,
            body: content.body,
            tags: content.tags,
            contentType: content.contentType,
            primaryPlatform: content.primaryPlatform,
            lastUpdatedAt: content.updatedAt,
          })
          .subscribe({
            next: () => patchState(store, { isDirty: false, isSaving: false }),
            error: (err: Error) =>
              patchState(store, { error: err.message, isSaving: false }),
          });
      },
      addChatMessage(text: string): void {
        patchState(store, {
          chatMessages: [
            ...store.chatMessages(),
            { role: 'user' as const, content: text, timestamp: new Date().toISOString() },
          ],
        });
      },
      appendToken(token: string): void {
        patchState(store, {
          currentTokens: store.currentTokens() + token,
          isStreaming: true,
        });
      },
      completeGeneration(fullText: string): void {
        patchState(store, {
          chatMessages: [
            ...store.chatMessages(),
            { role: 'assistant' as const, content: fullText, timestamp: new Date().toISOString() },
          ],
          currentTokens: '',
          isStreaming: false,
        });
      },
      applyToEditor(text: string): void {
        const current = store.content();
        if (!current) return;
        patchState(store, {
          content: { ...current, body: text },
          isDirty: true,
        });
      },
      reset(): void {
        patchState(store, initialState);
      },
    };
  })
);
