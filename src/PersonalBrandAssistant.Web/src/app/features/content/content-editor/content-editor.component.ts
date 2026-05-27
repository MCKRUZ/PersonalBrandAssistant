import {
  Component,
  OnInit,
  DestroyRef,
  inject,
  signal,
  computed,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { ChipModule } from 'primeng/chip';
import { InputTextModule } from 'primeng/inputtext';
import { KnobModule } from 'primeng/knob';
import { SplitterModule } from 'primeng/splitter';
import { TooltipModule } from 'primeng/tooltip';
import { MarkdownComponent as MarkdownRenderer } from 'ngx-markdown';
import { MarkdownEditorComponent } from './markdown-editor/markdown-editor.component';
import { EditorToolbarComponent, DraftActionEvent } from './editor-toolbar/editor-toolbar.component';
import { SidecarChatComponent } from './sidecar-chat/sidecar-chat.component';
import { ContentEditorStore } from '../stores/content-editor.store';
import { ContentService } from '../services/content.service';
import {
  ContentStatus,
  ContentType,
  Platform,
} from '../models/content.model';
import type { PlatformConnectionStatus } from '../models/content.model';
import { PlatformTargetsComponent } from './platform-targets/platform-targets.component';
import { PublishModalComponent } from './publish-modal/publish-modal.component';

@Component({
  selector: 'app-content-editor',
  standalone: true,
  providers: [ContentEditorStore],
  imports: [
    FormsModule,
    ButtonModule,
    SelectModule,
    TagModule,
    ChipModule,
    InputTextModule,
    KnobModule,
    SplitterModule,
    TooltipModule,
    MarkdownRenderer,
    MarkdownEditorComponent,
    EditorToolbarComponent,
    SidecarChatComponent,
    PlatformTargetsComponent,
    PublishModalComponent,
  ],
  template: `
    <div class="editor-page" data-testid="content-editor-page">
      @if (store.loading()) {
        <div class="loading-overlay" data-testid="loading">Loading...</div>
      }

      <header class="editor-top-bar">
        <p-select
          [options]="platformOptions"
          [ngModel]="store.content()?.primaryPlatform"
          (ngModelChange)="onPlatformChange($event)"
          placeholder="Platform"
          data-testid="platform-selector"
          [style]="{ minWidth: '140px' }" />

        <p-select
          [options]="typeOptions"
          [ngModel]="store.content()?.contentType"
          (ngModelChange)="onTypeChange($event)"
          placeholder="Type"
          data-testid="type-selector"
          [style]="{ minWidth: '160px' }" />

        <p-tag
          [value]="store.content()?.status ?? ''"
          [severity]="statusSeverity()"
          data-testid="status-badge" />

        <div class="tags-input" data-testid="tags-input">
          @for (tag of store.content()?.tags ?? []; track tag) {
            <p-chip [label]="tag" [removable]="true" (onRemove)="removeTag(tag)" />
          }
          <input pInputText type="text" placeholder="Add tag..."
            class="tag-add-input"
            (keydown.enter)="addTag($event)" />
        </div>

        @if (store.content()?.voiceScore !== null && store.content()?.voiceScore !== undefined) {
          <p-knob
            [ngModel]="store.content()!.voiceScore!"
            [size]="48"
            [readonly]="true"
            [strokeWidth]="6"
            [valueColor]="voiceColor()"
            data-testid="voice-knob" />
        }

        <span class="save-indicator" data-testid="save-indicator">
          @if (store.isSaving()) {
            Saving...
          } @else if (store.isDirty()) {
            Unsaved changes
          } @else {
            Saved
          }
        </span>
      </header>

      <app-editor-toolbar
        [isLoading]="isAiLoading()"
        [status]="store.content()?.status ?? null"
        [hasBody]="!!store.content()?.body"
        (draftAction)="onDraftAction($event)"
        (crossPostAction)="onCrossPostAction()" />

      <app-platform-targets
        [selectedPlatforms]="store.content()?.targetPlatforms ?? []"
        [primaryPlatform]="store.content()?.primaryPlatform ?? Platform.Blog"
        [connectedPlatforms]="connectedPlatforms()"
        [bodyLength]="(store.content()?.body ?? '').length"
        [wordCount]="wordCount()"
        (targetPlatformsChange)="onTargetPlatformsChange($event)"
        data-testid="platform-targets" />

      <p-splitter [style]="{ flex: 1, minHeight: 0 }" [panelSizes]="[50, 50]">
        <ng-template pTemplate>
          <app-markdown-editor
            [value]="store.content()?.body ?? ''"
            [readOnly]="!canEdit()"
            (valueChange)="onBodyChange($event)" />
        </ng-template>
        <ng-template pTemplate>
          <div class="preview-panel">
            <markdown [data]="store.content()?.body ?? ''" />
          </div>
        </ng-template>
      </p-splitter>

      <footer class="editor-action-bar" data-testid="action-bar">
        @switch (store.content()?.status) {
          @case ('Idea') {
            <p-button label="Start Draft" icon="pi pi-pencil" (onClick)="onStartDraft()" />
          }
          @case ('Draft') {
            <p-button label="Save Draft" icon="pi pi-save" severity="secondary"
              (onClick)="store.autoSave()" />
            <p-button label="Approve" icon="pi pi-check" severity="success"
              (onClick)="onApprove()" />
            <p-button label="Submit for Review" icon="pi pi-send" severity="info"
              (onClick)="onSubmitForReview()" />
          }
          @case ('Review') {
            <p-button label="Approve" icon="pi pi-check" severity="success"
              (onClick)="onApprove()" />
            <p-button label="Request Changes" icon="pi pi-replay" severity="warn"
              (onClick)="onRequestChanges()" />
          }
          @case ('Approved') {
            <p-button label="Schedule" icon="pi pi-calendar" severity="info"
              (onClick)="onSchedule()" />
            <p-button label="Publish Now" icon="pi pi-cloud-upload" severity="success"
              (onClick)="onPublish()" />
          }
          @case ('Scheduled') {
            <p-button label="Unschedule" icon="pi pi-times" severity="warn"
              (onClick)="onUnschedule()" />
            @if (store.content()?.scheduledAt) {
              <span class="scheduled-time">
                Scheduled: {{ store.content()!.scheduledAt }}
              </span>
            }
          }
          @case ('Published') {
            <p-button label="Unpublish" icon="pi pi-undo" severity="warn"
              (onClick)="onUnpublish()" />
          }
          @case ('Archived') {
            <p-button label="Restore" icon="pi pi-refresh" severity="info"
              (onClick)="onRestore()" />
          }
        }
      </footer>

      <p-button icon="pi pi-comments" [rounded]="true"
        class="chat-toggle-btn"
        (onClick)="chatPanelVisible.set(!chatPanelVisible())"
        pTooltip="AI Chat" data-testid="chat-toggle" />

      <app-sidecar-chat
        [visible]="chatPanelVisible()"
        (visibleChange)="chatPanelVisible.set($event)"
        [contentId]="store.content()?.id ?? ''" />

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
    }
    .loading-overlay {
      position: absolute;
      inset: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #0d111780;
      color: #8b949e;
      z-index: 50;
    }
    .editor-top-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 8px 16px;
      border-bottom: 1px solid #21262d;
      flex-shrink: 0;
    }
    .tags-input {
      display: flex;
      align-items: center;
      gap: 4px;
      flex: 1;
      flex-wrap: wrap;
    }
    .tag-add-input {
      border: none;
      background: transparent;
      color: #f0f6fc;
      font-size: 13px;
      min-width: 80px;
      flex: 1;
      outline: none;
    }
    .save-indicator {
      font-size: 12px;
      color: #8b949e;
      white-space: nowrap;
    }
    :host ::ng-deep .p-splitter {
      flex: 1;
      min-height: 0;
      border: none;
    }
    .preview-panel {
      padding: 16px;
      overflow-y: auto;
      height: 100%;
      color: #c9d1d9;
    }
    .editor-action-bar {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      border-top: 1px solid #21262d;
      flex-shrink: 0;
    }
    .scheduled-time {
      font-size: 12px;
      color: #8b949e;
    }
    :host ::ng-deep .chat-toggle-btn {
      position: fixed;
      bottom: 72px;
      right: 24px;
      z-index: 100;
    }
  `],
})
export class ContentEditorComponent implements OnInit {
  readonly store = inject(ContentEditorStore);
  private readonly contentService = inject(ContentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly chatPanelVisible = signal(false);
  readonly isAiLoading = signal(false);
  readonly publishModalVisible = signal(false);
  readonly publishMode = signal<'publish' | 'schedule'>('publish');
  readonly connectedPlatforms = signal<PlatformConnectionStatus[]>([]);

  private autoSaveTimer: ReturnType<typeof setTimeout> | null = null;

  readonly platformOptions = Object.values(Platform);
  readonly typeOptions = Object.values(ContentType);

  readonly wordCount = computed(() => {
    const body = this.store.content()?.body ?? '';
    return body.trim() ? body.trim().split(/\s+/).length : 0;
  });

  readonly Platform = Platform;

  readonly canEdit = computed(() => {
    const status = this.store.content()?.status;
    return status === ContentStatus.Idea || status === ContentStatus.Draft || status === ContentStatus.Review;
  });

  readonly statusSeverity = computed((): 'info' | 'warn' | 'secondary' | 'success' | 'danger' | 'contrast' | undefined => {
    switch (this.store.content()?.status) {
      case ContentStatus.Idea: return 'info';
      case ContentStatus.Draft: return 'warn';
      case ContentStatus.Review: return 'secondary';
      case ContentStatus.Approved: return 'success';
      case ContentStatus.Scheduled: return 'contrast';
      case ContentStatus.Published: return 'success';
      case ContentStatus.Archived: return 'secondary';
      default: return undefined;
    }
  });

  readonly voiceColor = computed(() => {
    const score = this.store.content()?.voiceScore;
    if (score === null || score === undefined) return '#8b949e';
    if (score > 80) return '#3fb950';
    if (score >= 60) return '#d29922';
    return '#f85149';
  });

  ngOnInit(): void {
    const paramMap = this.route.snapshot.paramMap;
    if (paramMap.has('id')) {
      this.store.loadContent(paramMap.get('id')!);
    } else {
      this.contentService
        .create({
          title: 'Untitled',
          contentType: ContentType.BlogPost,
          primaryPlatform: Platform.Blog,
          tags: [],
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

  onPlatformChange(value: Platform): void {
    this.store.updateField('primaryPlatform', value);
    this.scheduleAutoSave();
  }

  onTypeChange(value: ContentType): void {
    this.store.updateField('contentType', value);
    this.scheduleAutoSave();
  }

  addTag(event: Event): void {
    const input = event.target as HTMLInputElement;
    const tag = input.value.trim();
    if (!tag) return;
    const current = this.store.content()?.tags ?? [];
    if (!current.includes(tag)) {
      this.store.updateField('tags', [...current, tag]);
      this.scheduleAutoSave();
    }
    input.value = '';
  }

  removeTag(tag: string): void {
    const current = this.store.content()?.tags ?? [];
    this.store.updateField('tags', current.filter((t) => t !== tag));
    this.scheduleAutoSave();
  }

  onBodyChange(body: string): void {
    this.store.updateField('body', body);
    this.scheduleAutoSave();
  }

  onDraftAction(event: DraftActionEvent): void {
    const id = this.store.content()?.id;
    if (!id) return;
    this.isAiLoading.set(true);
    this.contentService.draft(id, event).subscribe({
      next: () => {
        this.store.loadContent(id);
        this.isAiLoading.set(false);
      },
      error: () => this.isAiLoading.set(false),
    });
  }

  onCrossPostAction(): void {
    const id = this.store.content()?.id;
    if (!id) return;
    const platform = prompt('Enter target platform (Blog, LinkedIn, Twitter, Substack, Reddit, YouTube):');
    if (!platform) return;
    const validPlatforms = Object.values(Platform) as string[];
    if (!validPlatforms.includes(platform)) {
      alert(`Invalid platform. Must be one of: ${validPlatforms.join(', ')}`);
      return;
    }
    this.contentService
      .crossPost(id, { targetPlatform: platform as Platform })
      .subscribe((childId) => {
        this.router.navigate(['/content', childId]);
      });
  }

  onStartDraft(): void {
    const id = this.store.content()?.id;
    if (!id) return;
    this.isAiLoading.set(true);
    this.contentService.draft(id, { action: 'draft' }).subscribe({
      next: () => {
        this.store.loadContent(id);
        this.isAiLoading.set(false);
      },
      error: () => this.isAiLoading.set(false),
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
    this.publishModalVisible.set(false);
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
