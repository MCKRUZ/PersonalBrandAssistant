import {
  Component,
  OnInit,
  DestroyRef,
  inject,
  signal,
  computed,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { EditorTopBarComponent } from './editor-top-bar/editor-top-bar.component';
import { ManuscriptSurfaceComponent } from './manuscript-surface/manuscript-surface.component';
import { VoiceMeterComponent } from './voice-meter/voice-meter.component';
import { SidecarChatComponent } from './sidecar-chat/sidecar-chat.component';
import { PlatformDotComponent } from '../shared/platform-dot.component';
import { ContentEditorStore } from '../stores/content-editor.store';
import { ContentService } from '../services/content.service';
import {
  ContentStatus,
  ContentType,
  Platform,
} from '../models/content.model';
import type { PlatformConnectionStatus } from '../models/content.model';
import { PublishModalComponent } from './publish-modal/publish-modal.component';

@Component({
  selector: 'app-content-editor',
  standalone: true,
  providers: [ContentEditorStore],
  imports: [
    ButtonModule,
    EditorTopBarComponent,
    ManuscriptSurfaceComponent,
    VoiceMeterComponent,
    SidecarChatComponent,
    PlatformDotComponent,
    PublishModalComponent,
  ],
  template: `
    <div class="editor-page" data-testid="content-editor-page">
      @if (store.loading()) {
        <div class="loading-overlay" data-testid="loading">Loading...</div>
      }

      <app-editor-top-bar
        [status]="store.content()?.status ?? null"
        [contentType]="store.content()?.contentType ?? null"
        [primaryPlatform]="store.content()?.primaryPlatform ?? null"
        [voiceScore]="store.content()?.voiceScore ?? null"
        [isSaving]="store.isSaving()"
        [isDirty]="store.isDirty()"
        [panelOpen]="panelOpen()"
        (togglePanel)="panelOpen.set(!panelOpen())" />

      <div class="editor-body">
        <main class="manuscript-scroll">
          @if (store.content()) {
            <app-manuscript-surface
              [content]="store.content()!"
              [canEdit]="canEdit()"
              (titleChange)="onTitleChange($event)"
              (bodyChange)="onBodyChange($event)"
              (tagsChange)="onTagsChange($event)"
              (startDraft)="onStartDraft()" />
          }
        </main>

        @if (panelOpen()) {
          <aside class="side-panel" data-testid="side-panel">
            <app-voice-meter
              [contentId]="store.content()?.id ?? ''"
              [voiceScore]="store.content()?.voiceScore ?? null"
              [feedback]="voiceFeedback()" />
            <app-sidecar-chat [contentId]="store.content()?.id ?? ''" />
          </aside>
        }
      </div>

      <footer class="editor-action-bar" data-testid="action-bar">
        <div class="targets">
          <span class="targets-label">Targets</span>
          @for (target of store.content()?.targetPlatforms ?? []; track target) {
            <app-platform-dot [platform]="target" variant="dot" />
          }
        </div>

        <div class="actions">
          @switch (store.content()?.status) {
            @case ('Idea') {
              <button class="btn primary" (click)="onStartDraft()">Start Draft</button>
            }
            @case ('Draft') {
              <button class="btn ghost" (click)="store.autoSave()">Save Draft</button>
              <button class="btn ghost" (click)="onSubmitForReview()">Submit for Review</button>
              <button class="btn primary" (click)="onApprove()">Approve</button>
            }
            @case ('Review') {
              <button class="btn ghost" (click)="onRequestChanges()">Request Changes</button>
              <button class="btn primary" (click)="onApprove()">Approve</button>
            }
            @case ('Approved') {
              <button class="btn ghost" (click)="onSchedule()">Schedule</button>
              <button class="btn primary" (click)="onPublish()">Publish Now</button>
            }
            @case ('Scheduled') {
              @if (store.content()?.scheduledAt) {
                <span class="scheduled-time">Scheduled: {{ store.content()!.scheduledAt }}</span>
              }
              <button class="btn ghost" (click)="onUnschedule()">Unschedule</button>
            }
            @case ('Published') {
              <button class="btn ghost" (click)="onUnpublish()">Unpublish</button>
            }
            @case ('Archived') {
              <button class="btn primary" (click)="onRestore()">Restore</button>
            }
          }
        </div>
      </footer>

      @if (store.content()) {
        <app-publish-modal
          [visible]="publishModalVisible()"
          [content]="store.content()!"
          [connectedPlatforms]="connectedPlatforms()"
          [mode]="publishMode()"
          (confirm)="onPublishConfirm($event)"
          (cancel)="publishModalVisible.set(false)" />
      }
    </div>
  `,
  styles: [`
    .editor-page {
      display: flex;
      flex-direction: column;
      height: 100%;
      position: relative;
      background: var(--surface-base);
    }
    .loading-overlay {
      position: absolute;
      inset: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--surface-base);
      color: var(--text-secondary);
      z-index: 50;
    }
    .editor-body {
      display: flex;
      flex: 1;
      min-height: 0;
    }
    .manuscript-scroll {
      flex: 1;
      overflow-y: auto;
    }
    .side-panel {
      width: 340px;
      flex-shrink: 0;
      border-left: 1px solid var(--surface-border);
      background: var(--surface-inset);
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }
    .editor-action-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 8px 16px;
      border-top: 1px solid var(--surface-border);
      flex-shrink: 0;
      background: var(--surface-card);
    }
    .targets {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .targets-label {
      font-size: 12px;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .actions { display: flex; align-items: center; gap: 8px; }
    .btn {
      font-size: 13px;
      font-weight: 600;
      padding: 7px 14px;
      border-radius: var(--r-control);
      cursor: pointer;
    }
    .btn.primary {
      background: var(--brand-primary);
      color: #1a0f0a;
      border: none;
    }
    .btn.primary:hover { filter: brightness(1.05); }
    .btn.ghost {
      background: transparent;
      border: 1px solid var(--surface-border);
      color: var(--text-secondary);
    }
    .btn.ghost:hover { color: var(--text-primary); border-color: var(--brand-primary); }
    .scheduled-time { font-size: 12px; color: var(--text-secondary); }
  `],
})
export class ContentEditorComponent implements OnInit {
  readonly store = inject(ContentEditorStore);
  private readonly contentService = inject(ContentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly panelOpen = signal(true);
  readonly publishModalVisible = signal(false);
  readonly publishMode = signal<'publish' | 'schedule'>('publish');
  readonly connectedPlatforms = signal<PlatformConnectionStatus[]>([]);
  readonly voiceFeedback = signal<string | null>(null);

  private autoSaveTimer: ReturnType<typeof setTimeout> | null = null;

  readonly Platform = Platform;

  readonly canEdit = computed(() => {
    const status = this.store.content()?.status;
    return status === ContentStatus.Idea || status === ContentStatus.Draft || status === ContentStatus.Review;
  });

  ngOnInit(): void {
    const paramMap = this.route.snapshot.paramMap;
    if (paramMap.has('id')) {
      this.store.loadContent(paramMap.get('id')!);
    } else {
      const q = this.route.snapshot.queryParamMap;
      const topic = q.get('topic');
      const type = q.get('type') as ContentType | null;
      const sourceIdeaId = q.get('sourceIdeaId');
      this.contentService
        .create({
          title: topic?.trim() || 'Untitled',
          contentType: type ?? ContentType.BlogPost,
          primaryPlatform: Platform.Blog,
          tags: [],
          ...(sourceIdeaId ? { sourceIdeaId } : {}),
        })
        .subscribe({
          next: (newId) => this.router.navigate(['/content', newId]),
          error: () => this.router.navigate(['/content']),
        });
    }

    this.contentService.getPlatforms().subscribe({
      next: (platforms) => this.connectedPlatforms.set(platforms),
      error: () => this.connectedPlatforms.set([]),
    });

    this.destroyRef.onDestroy(() => {
      if (this.autoSaveTimer) clearTimeout(this.autoSaveTimer);
      this.store.reset();
    });
  }

  onTitleChange(title: string): void {
    this.store.updateField('title', title);
    this.scheduleAutoSave();
  }

  onTagsChange(tags: string[]): void {
    this.store.updateField('tags', tags);
    this.scheduleAutoSave();
  }

  onBodyChange(body: string): void {
    this.store.updateField('body', body);
    this.scheduleAutoSave();
  }

  onStartDraft(): void {
    const id = this.store.content()?.id;
    if (!id) return;
    this.contentService.draft(id, { action: 'draft' }).subscribe({
      next: () => this.store.loadContent(id),
      error: () => this.store.loadContent(id),
    });
  }

  onApprove(): void { this.doStatusAction((id) => this.contentService.approve(id)); }
  onSubmitForReview(): void { this.doStatusAction((id) => this.contentService.submitForReview(id)); }
  onRequestChanges(): void { this.doStatusAction((id) => this.contentService.requestChanges(id)); }
  onUnpublish(): void { this.doStatusAction((id) => this.contentService.unpublish(id)); }
  onUnschedule(): void { this.doStatusAction((id) => this.contentService.unschedule(id)); }
  onRestore(): void { this.doStatusAction((id) => this.contentService.restore(id)); }

  onPublish(): void {
    this.publishMode.set('publish');
    this.publishModalVisible.set(true);
  }

  onSchedule(): void {
    this.publishMode.set('schedule');
    this.publishModalVisible.set(true);
  }

  onPublishConfirm(event: { platforms: Platform[]; scheduledAt?: string }): void {
    const id = this.store.content()?.id;
    if (!id) return;
    // Keep the modal open so it can show its post-confirm result view; it closes via (cancel)/Done.
    if (event.scheduledAt) {
      this.contentService
        .schedule(id, { scheduledAt: event.scheduledAt })
        .subscribe({
          next: () => this.store.loadContent(id),
          error: () => this.store.loadContent(id),
        });
    } else {
      this.contentService
        .publish(id, { targetPlatforms: event.platforms })
        .subscribe({
          next: () => this.store.loadContent(id),
          error: () => this.store.loadContent(id),
        });
    }
  }

  onTargetPlatformsChange(platforms: Platform[]): void {
    this.store.updateField('targetPlatforms', platforms);
    this.scheduleAutoSave();
  }

  private doStatusAction(action: (id: string) => import('rxjs').Observable<void>): void {
    const id = this.store.content()?.id;
    if (!id) return;
    action(id).subscribe({
      next: () => this.store.loadContent(id),
      error: () => this.store.loadContent(id),
    });
  }

  private scheduleAutoSave(): void {
    if (this.autoSaveTimer) clearTimeout(this.autoSaveTimer);
    const status = this.store.content()?.status;
    const autoSaveable = status === ContentStatus.Idea || status === ContentStatus.Draft || status === ContentStatus.Review;
    if (!autoSaveable) return;
    this.autoSaveTimer = setTimeout(() => this.store.autoSave(), 3000);
  }
}
