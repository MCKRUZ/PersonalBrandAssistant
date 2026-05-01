import { Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  BlogPipelineStage,
  PIPELINE_STAGES,
  PIPELINE_STAGE_LABELS,
  PIPELINE_STAGE_ICONS,
} from '../../../features/blog-pipeline/models/blog-pipeline.model';

@Component({
  selector: 'app-pipeline-stage-indicator',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="stage-stepper">
      @for (stage of stages; track stage; let i = $index) {
        @if (i > 0) {
          <div class="step-connector" [class.completed]="stage <= currentStage()"></div>
        }
        <button
          class="stage-step"
          [class.completed]="stage < currentStage()"
          [class.active]="stage === currentStage()"
          [class.pending]="stage > currentStage()"
          [disabled]="disabled() || stage === currentStage()"
          (click)="onStageClick(stage)"
        >
          <div class="step-indicator">
            @if (stage < currentStage()) {
              <i class="pi pi-check"></i>
            } @else {
              <i [class]="icons[stage]"></i>
            }
          </div>
          <span class="step-label">{{ labels[stage] }}</span>
        </button>
      }
    </div>
  `,
  styles: `
    .stage-stepper {
      display: flex;
      align-items: center;
      gap: 0;
      padding: 0.75rem 0;
    }
    .step-connector {
      flex: 1;
      height: 2px;
      background: var(--surface-border);
      transition: background 0.2s;
    }
    .step-connector.completed {
      background: var(--green-400);
    }
    .stage-step {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.25rem;
      background: none;
      border: none;
      cursor: pointer;
      padding: 0.25rem 0.5rem;
      color: var(--text-color-secondary);
      transition: color 0.2s;
    }
    .stage-step:disabled { cursor: default; }
    .stage-step.active { color: var(--primary-color); }
    .stage-step.completed { color: var(--green-400); }
    .step-indicator {
      width: 2rem;
      height: 2rem;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.85rem;
      background: var(--surface-ground);
      border: 2px solid var(--surface-border);
      transition: all 0.2s;
    }
    .completed .step-indicator {
      background: var(--green-400);
      border-color: var(--green-400);
      color: white;
    }
    .active .step-indicator {
      background: var(--primary-color);
      border-color: var(--primary-color);
      color: white;
    }
    .step-label {
      font-size: 0.7rem;
      white-space: nowrap;
    }
  `,
})
export class PipelineStageIndicatorComponent {
  readonly currentStage = input.required<BlogPipelineStage>();
  readonly disabled = input(false);
  readonly stageClicked = output<BlogPipelineStage>();

  readonly stages = PIPELINE_STAGES;
  readonly labels = PIPELINE_STAGE_LABELS;
  readonly icons = PIPELINE_STAGE_ICONS;

  onStageClick(stage: BlogPipelineStage) {
    if (stage !== this.currentStage()) {
      this.stageClicked.emit(stage);
    }
  }
}
