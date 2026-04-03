import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { MessageService } from 'primeng/api';
import { BlogPipelineService } from './services/blog-pipeline.service';
import {
  BlogPipelineItem,
  BlogPipelineStage,
  PIPELINE_STAGES,
  PIPELINE_STAGE_LABELS,
  PIPELINE_STAGE_ICONS,
} from './models/blog-pipeline.model';

@Component({
  selector: 'app-blog-pipeline',
  standalone: true,
  imports: [CommonModule, ButtonModule, CardModule, TagModule, TooltipModule, ProgressSpinnerModule],
  template: `
    <div class="blog-pipeline">
      <div class="pipeline-header">
        <h2>Blog Pipeline Tracker</h2>
        <p-button
          label="Refresh"
          icon="pi pi-refresh"
          [text]="true"
          (onClick)="load()"
        />
      </div>

      @if (loading()) {
        <div class="loading-container">
          <p-progressSpinner strokeWidth="3" />
        </div>
      } @else if (items().length === 0) {
        <div class="empty-state">
          <i class="pi pi-inbox" style="font-size: 3rem; color: var(--text-color-secondary)"></i>
          <p>No blog posts in the pipeline yet.</p>
        </div>
      } @else {
        <!-- Stage columns header -->
        <div class="stage-header">
          @for (stage of stages; track stage) {
            <div class="stage-column-header">
              <i [class]="stageIcons[stage]"></i>
              <span>{{ stageLabels[stage] }}</span>
              <span class="stage-count">{{ stageCountMap()[stage] ?? 0 }}</span>
            </div>
          }
        </div>

        <!-- Post cards -->
        <div class="pipeline-cards">
          @for (item of items(); track item.id) {
            <div class="pipeline-card">
              <div class="card-header">
                <span class="card-title">{{ item.title ?? 'Untitled' }}</span>
                <p-tag
                  [value]="item.status"
                  [severity]="getStatusSeverity(item.status)"
                />
              </div>

              <!-- Stepper -->
              <div class="stage-stepper">
                @for (stage of stages; track stage) {
                  <div
                    class="stage-step"
                    [class.completed]="stage < item.currentBlogStage"
                    [class.active]="stage === item.currentBlogStage"
                    [class.pending]="stage > item.currentBlogStage"
                    [pTooltip]="stageLabels[stage]"
                    tooltipPosition="top"
                    (click)="onStageClick(item, stage)"
                  >
                    <div class="step-indicator">
                      @if (stage < item.currentBlogStage) {
                        <i class="pi pi-check"></i>
                      } @else {
                        <i [class]="stageIcons[stage]"></i>
                      }
                    </div>
                    <span class="step-label">{{ stageLabels[stage] }}</span>
                  </div>
                  @if (stage < stages.length - 1) {
                    <div
                      class="step-connector"
                      [class.completed]="stage < item.currentBlogStage"
                    ></div>
                  }
                }
              </div>

              <div class="card-actions">
                <p-button
                  label="Advance"
                  icon="pi pi-arrow-right"
                  size="small"
                  [disabled]="item.currentBlogStage >= lastStage || advancing() === item.id"
                  [loading]="advancing() === item.id"
                  (onClick)="advance(item)"
                />
                <span class="card-date">{{ item.createdAt | date:'mediumDate' }}</span>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .blog-pipeline {
      padding: 1.5rem;
    }

    .pipeline-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 1.5rem;

      h2 {
        margin: 0;
        font-size: 1.5rem;
        font-weight: 600;
      }
    }

    .loading-container, .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 4rem 2rem;
      gap: 1rem;
      color: var(--text-color-secondary);
    }

    .stage-header {
      display: grid;
      grid-template-columns: repeat(5, 1fr);
      gap: 0.5rem;
      margin-bottom: 1.5rem;
      padding: 0.75rem 1rem;
      background: var(--surface-card);
      border-radius: 8px;
      border: 1px solid var(--surface-border);
    }

    .stage-column-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-weight: 600;
      font-size: 0.875rem;

      .stage-count {
        background: var(--primary-color);
        color: var(--primary-color-text);
        border-radius: 50%;
        width: 1.5rem;
        height: 1.5rem;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 0.75rem;
        margin-left: auto;
      }
    }

    .pipeline-cards {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }

    .pipeline-card {
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: 8px;
      padding: 1.25rem;
      transition: box-shadow 0.2s;

      &:hover {
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      }
    }

    .card-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 1rem;
    }

    .card-title {
      font-weight: 600;
      font-size: 1rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      max-width: 70%;
    }

    .stage-stepper {
      display: flex;
      align-items: center;
      margin-bottom: 1rem;
      padding: 0.5rem 0;
    }

    .stage-step {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.375rem;
      cursor: pointer;
      flex: 0 0 auto;

      .step-indicator {
        width: 2.25rem;
        height: 2.25rem;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 0.875rem;
        transition: all 0.2s;
        border: 2px solid var(--surface-border);
        background: var(--surface-ground);
        color: var(--text-color-secondary);
      }

      .step-label {
        font-size: 0.7rem;
        color: var(--text-color-secondary);
        white-space: nowrap;
      }

      &.completed .step-indicator {
        background: var(--green-500);
        border-color: var(--green-500);
        color: white;
      }

      &.active .step-indicator {
        background: var(--primary-color);
        border-color: var(--primary-color);
        color: var(--primary-color-text);
        box-shadow: 0 0 0 3px var(--primary-100);
      }

      &.active .step-label {
        color: var(--primary-color);
        font-weight: 600;
      }

      &:hover:not(.active) .step-indicator {
        border-color: var(--primary-color);
      }
    }

    .step-connector {
      flex: 1;
      height: 2px;
      background: var(--surface-border);
      margin: 0 0.25rem;
      margin-bottom: 1.25rem;
      transition: background 0.2s;

      &.completed {
        background: var(--green-500);
      }
    }

    .card-actions {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .card-date {
      font-size: 0.8rem;
      color: var(--text-color-secondary);
    }
  `],
})
export class BlogPipelineComponent implements OnInit {
  private readonly pipelineService = inject(BlogPipelineService);
  private readonly messageService = inject(MessageService);

  readonly stages = PIPELINE_STAGES;
  readonly stageLabels = PIPELINE_STAGE_LABELS;
  readonly stageIcons = PIPELINE_STAGE_ICONS;
  readonly lastStage = BlogPipelineStage.Social;

  readonly items = signal<readonly BlogPipelineItem[]>([]);
  readonly loading = signal(true);
  readonly advancing = signal<string | null>(null);

  readonly stageCountMap = computed(() => {
    const counts: Record<number, number> = {};
    for (const item of this.items()) {
      counts[item.currentBlogStage] = (counts[item.currentBlogStage] ?? 0) + 1;
    }
    return counts;
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.pipelineService.getAll().subscribe({
      next: (data) => {
        this.items.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.messageService.add({ severity: 'error', summary: 'Failed to load pipeline' });
        this.loading.set(false);
      },
    });
  }

  advance(item: BlogPipelineItem): void {
    this.advancing.set(item.id);
    this.pipelineService.advanceStage(item.id).subscribe({
      next: (result) => {
        this.items.set(
          this.items().map((i) =>
            i.id === item.id ? { ...i, currentBlogStage: result.currentBlogStage } : i
          )
        );
        this.advancing.set(null);
        this.messageService.add({
          severity: 'success',
          summary: `Advanced to ${PIPELINE_STAGE_LABELS[result.currentBlogStage]}`,
        });
      },
      error: () => {
        this.advancing.set(null);
        this.messageService.add({ severity: 'error', summary: 'Failed to advance stage' });
      },
    });
  }

  onStageClick(item: BlogPipelineItem, stage: BlogPipelineStage): void {
    if (stage === item.currentBlogStage) return;

    this.advancing.set(item.id);
    this.pipelineService.setStage(item.id, stage).subscribe({
      next: (result) => {
        this.items.set(
          this.items().map((i) =>
            i.id === item.id ? { ...i, currentBlogStage: result.currentBlogStage } : i
          )
        );
        this.advancing.set(null);
        this.messageService.add({
          severity: 'success',
          summary: `Set to ${PIPELINE_STAGE_LABELS[result.currentBlogStage]}`,
        });
      },
      error: () => {
        this.advancing.set(null);
        this.messageService.add({ severity: 'error', summary: 'Failed to set stage' });
      },
    });
  }

  getStatusSeverity(status: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case 'Published': return 'success';
      case 'Approved':
      case 'Scheduled': return 'info';
      case 'Review':
      case 'Publishing': return 'warn';
      case 'Failed': return 'danger';
      default: return 'secondary';
    }
  }
}
