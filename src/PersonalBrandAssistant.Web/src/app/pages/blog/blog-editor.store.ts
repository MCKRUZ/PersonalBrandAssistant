import { inject, DestroyRef } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { computed } from '@angular/core';
import { Subject, forkJoin, concatMap, EMPTY, of } from 'rxjs';
import { catchError, debounceTime } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ContentItem } from '../../core/models/content.model';
import { BrandVoiceScore } from '../../core/models/brand-voice.model';
import { AgentExecution } from '../../core/models/agent.model';
import { ContentEditorApiService, UpdateContentRequest } from '../content-editor/content-editor-api.service';
import { BlogPipelineApiService } from './blog-pipeline-api.service';
import { BlogPipelineStage, BlogStageTransition, PIPELINE_STAGES } from '../../features/blog-pipeline/models/blog-pipeline.model';

interface BlogEditorState {
  readonly content: ContentItem | undefined;
  readonly brandScore: BrandVoiceScore | undefined;
  readonly versions: readonly ContentItem[];
  readonly executionHistory: readonly AgentExecution[];
  readonly isLoading: boolean;
  readonly isSaving: boolean;
  readonly isScoring: boolean;
  readonly saveError: 'conflict' | 'network' | null;
  readonly activeTab: 'preview' | 'history' | 'versions';
  readonly currentBlogStage: BlogPipelineStage;
  readonly blogStageHistory: readonly BlogStageTransition[];
  readonly blogSkipped: boolean;
  readonly substackPostUrl: string | null;
  readonly blogPostUrl: string | null;
  readonly blogDelayDays: number | null;
  readonly isAdvancing: boolean;
}

const initialState: BlogEditorState = {
  content: undefined,
  brandScore: undefined,
  versions: [],
  executionHistory: [],
  isLoading: false,
  isSaving: false,
  isScoring: false,
  saveError: null,
  activeTab: 'preview',
  currentBlogStage: BlogPipelineStage.Draft,
  blogStageHistory: [],
  blogSkipped: false,
  substackPostUrl: null,
  blogPostUrl: null,
  blogDelayDays: null,
  isAdvancing: false,
};

export const BlogEditorStore = signalStore(
  withState(initialState),
  withComputed(store => ({
    isLastStage: computed(() => store.currentBlogStage() === BlogPipelineStage.Social),
  })),
  withMethods((store) => {
    const contentApi = inject(ContentEditorApiService);
    const pipelineApi = inject(BlogPipelineApiService);
    const destroyRef = inject(DestroyRef);
    const saveSubject = new Subject<UpdateContentRequest>();

    saveSubject.pipe(
      debounceTime(1000),
      concatMap(request => {
        const content = store.content();
        if (!content) return EMPTY;
        patchState(store, { isSaving: true });
        return contentApi.update(content.id, request, content.version).pipe(
          catchError(err => {
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
        forkJoin({
          content: contentApi.getById(id),
          pipeline: pipelineApi.getById(id).pipe(catchError(() => of(null))),
        }).pipe(
          catchError(() => {
            patchState(store, { isLoading: false });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe(({ content, pipeline }) => {
          patchState(store, {
            content,
            isLoading: false,
            ...(pipeline ? {
              currentBlogStage: pipeline.currentBlogStage,
              blogStageHistory: pipeline.blogStageHistory,
              blogSkipped: pipeline.blogSkipped,
              substackPostUrl: pipeline.substackPostUrl,
              blogPostUrl: pipeline.blogPostUrl,
            } : {}),
          });
        });
      },

      updateField(field: keyof UpdateContentRequest, value: string): void {
        const current = store.content();
        if (!current) return;
        patchState(store, { content: { ...current, [field]: value }, saveError: null });
        saveSubject.next({ [field]: value });
      },

      applyDraft(text: string): void {
        const current = store.content();
        if (!current) return;
        patchState(store, { content: { ...current, body: text } });
        saveSubject.next({ body: text });
      },

      scoreContent(): void {
        const content = store.content();
        if (!content) return;
        patchState(store, { isScoring: true });
        contentApi.scoreContent(content.id).pipe(
          catchError(() => {
            patchState(store, { isScoring: false });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe(brandScore => {
          patchState(store, { brandScore, isScoring: false });
        });
      },

      advanceStage(note?: string): void {
        const content = store.content();
        if (!content || store.currentBlogStage() === BlogPipelineStage.Social) return;
        patchState(store, { isAdvancing: true });
        pipelineApi.advanceStage(content.id, note).pipe(
          catchError(() => {
            patchState(store, { isAdvancing: false });
            return EMPTY;
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe(result => {
          patchState(store, { currentBlogStage: result.currentBlogStage, isAdvancing: false });
        });
      },

      setStage(stage: BlogPipelineStage, note?: string): void {
        const content = store.content();
        if (!content) return;
        pipelineApi.setStage(content.id, stage, note).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(result => {
          patchState(store, { currentBlogStage: result.currentBlogStage });
        });
      },

      confirmSchedule(): void {
        const content = store.content();
        if (!content) return;
        pipelineApi.confirmSchedule(content.id).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, {
            content: { ...store.content()!, status: 'Scheduled' },
          });
        });
      },

      updateDelay(days: number | null): void {
        const content = store.content();
        if (!content) return;
        pipelineApi.updateDelay(content.id, days).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(result => {
          patchState(store, { blogDelayDays: result.blogDelayDays });
        });
      },

      skipBlog(): void {
        const content = store.content();
        if (!content) return;
        pipelineApi.skipBlog(content.id).pipe(
          catchError(() => EMPTY),
          takeUntilDestroyed(destroyRef),
        ).subscribe(() => {
          patchState(store, { blogSkipped: true });
        });
      },

      setActiveTab(tab: 'preview' | 'history' | 'versions'): void {
        patchState(store, { activeTab: tab });
      },
    };
  }),
);
