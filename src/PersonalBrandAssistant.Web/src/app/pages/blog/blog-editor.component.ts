import { Component, inject, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputNumber } from 'primeng/inputnumber';
import { EditorModule } from 'primeng/editor';
import { Subscription } from 'rxjs';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { BrandVoicePanelComponent } from '../content-editor/brand-voice-panel/brand-voice-panel.component';
import { PipelineStageIndicatorComponent } from './pipeline-stage-indicator/pipeline-stage-indicator.component';
import { BlogEditorStore } from './blog-editor.store';
import { ContentEditorApiService } from '../content-editor/content-editor-api.service';
import { BlogPipelineStage } from '../../features/blog-pipeline/models/blog-pipeline.model';
import { DraftApplyService } from '../../shell/sidecar/draft-apply.service';

@Component({
  selector: 'app-blog-editor',
  standalone: true,
  imports: [
    CommonModule, FormsModule, CardModule, ButtonModule, InputNumber, EditorModule,
    PageHeaderComponent, LoadingSpinnerComponent, StatusBadgeComponent,
    BrandVoicePanelComponent, PipelineStageIndicatorComponent,
  ],
  providers: [BlogEditorStore, ContentEditorApiService],
  template: `
    <div class="blog-editor">
      @if (store.isLoading()) {
        <app-loading-spinner message="Loading blog post..." />
      } @else {
        @if (store.content(); as content) {
        <div class="editor-header">
          <div class="flex align-items-center gap-3">
            <input
              class="title-input"
              [ngModel]="content.title"
              (ngModelChange)="store.updateField('title', $event)"
              placeholder="Blog post title..."
            />
            <app-status-badge [status]="content.status" />
            @if (store.isSaving()) {
              <span class="text-sm text-color-secondary"><i class="pi pi-spin pi-spinner mr-1"></i>Saving...</span>
            }
          </div>
        </div>

        @if (store.blogSkipped()) {
          <div class="skipped-badge">
            <i class="pi pi-ban mr-2"></i>Blog Skipped
          </div>
        } @else {
          <app-pipeline-stage-indicator
            [currentStage]="store.currentBlogStage()"
            [disabled]="store.isAdvancing()"
            (stageClicked)="store.setStage($event)"
          />
          <div class="pipeline-actions">
            <p-button
              label="Advance Stage"
              icon="pi pi-arrow-right"
              size="small"
              [disabled]="store.isLastStage() || store.isAdvancing()"
              [loading]="store.isAdvancing()"
              (onClick)="store.advanceStage()"
            />
          </div>
        }

        <div class="blog-metadata">
          @if (store.substackPostUrl()) {
            <a [href]="store.substackPostUrl()" target="_blank" class="meta-link">
              <i class="pi pi-external-link mr-1"></i>Substack
            </a>
          }
          @if (store.blogPostUrl()) {
            <a [href]="store.blogPostUrl()" target="_blank" class="meta-link">
              <i class="pi pi-external-link mr-1"></i>Website
            </a>
          }
          <div class="delay-control">
            <label class="text-sm text-color-secondary">Delay (days):</label>
            <p-inputNumber
              [ngModel]="store.blogDelayDays()"
              (ngModelChange)="pendingDelay = $event"
              [min]="0"
              [max]="90"
              [showButtons]="true"
              size="small"
            />
            <p-button label="Update" size="small" severity="secondary" [text]="true" (onClick)="store.updateDelay(pendingDelay ?? store.blogDelayDays())" />
          </div>
          <p-button label="Skip Blog" icon="pi pi-ban" severity="danger" size="small" [text]="true" (onClick)="store.skipBlog()" />
        </div>

        <div class="editor-layout">
          <div class="editor-main">
            <p-editor
              [ngModel]="content.body"
              (ngModelChange)="store.updateField('body', $event)"
              [style]="{ height: '400px' }"
            />
          </div>
          <div class="editor-side">
            <app-brand-voice-panel
              [score]="store.brandScore()"
              [isScoring]="store.isScoring()"
              (scoreRequested)="store.scoreContent()"
            />
          </div>
        </div>

        <div class="action-bar">
          <p-button label="Score" icon="pi pi-chart-bar" severity="secondary" [text]="true" (onClick)="store.scoreContent()" />
          <p-button label="Confirm Schedule" icon="pi pi-calendar" severity="secondary" [text]="true" (onClick)="store.confirmSchedule()" />
        </div>
        }
      }
    </div>
  `,
  styles: `
    .blog-editor { display: flex; flex-direction: column; gap: 1rem; }
    .editor-header { display: flex; flex-direction: column; gap: 0.5rem; }
    .title-input {
      flex: 1;
      background: transparent;
      border: none;
      border-bottom: 1px solid var(--surface-border);
      color: var(--text-color);
      font-size: 1.5rem;
      font-weight: 600;
      padding: 0.5rem 0;
      outline: none;
    }
    .title-input:focus { border-bottom-color: var(--primary-color); }
    .skipped-badge {
      padding: 0.5rem 1rem;
      background: var(--red-400);
      color: white;
      border-radius: 0.5rem;
      font-size: 0.9rem;
      display: inline-flex;
      align-items: center;
    }
    .pipeline-actions { display: flex; gap: 0.5rem; }
    .blog-metadata {
      display: flex;
      align-items: center;
      gap: 1rem;
      flex-wrap: wrap;
      padding: 0.5rem 0;
      border-bottom: 1px solid var(--surface-border);
    }
    .meta-link {
      color: var(--primary-color);
      text-decoration: none;
      font-size: 0.85rem;
    }
    .meta-link:hover { text-decoration: underline; }
    .delay-control {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-left: auto;
    }
    .editor-layout {
      display: grid;
      grid-template-columns: 1fr 300px;
      gap: 1.5rem;
    }
    @media (max-width: 768px) {
      .editor-layout { grid-template-columns: 1fr; }
    }
    .action-bar {
      display: flex;
      gap: 0.5rem;
      padding-top: 0.5rem;
      border-top: 1px solid var(--surface-border);
    }
  `,
})
export class BlogEditorComponent implements OnInit, OnDestroy {
  readonly store = inject(BlogEditorStore);
  private readonly route = inject(ActivatedRoute);
  private readonly draftApply = inject(DraftApplyService);
  private draftSub?: Subscription;
  pendingDelay: number | null = null;

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.store.loadContent(id);
    }
    this.draftSub = this.draftApply.apply$.subscribe(text => {
      this.store.applyDraft(text);
    });
  }

  ngOnDestroy() {
    this.draftSub?.unsubscribe();
  }
}
