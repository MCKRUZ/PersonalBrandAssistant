import { Component, effect, inject, input, output, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { SliderModule } from 'primeng/slider';
import { InputNumberModule } from 'primeng/inputnumber';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
import { AutonomySettings, AutonomyLevel } from '../../../core/models/autonomy.model';

interface LevelInfo {
  readonly label: string;
  readonly description: string;
}

const LEVEL_INFO: readonly LevelInfo[] = [
  { label: 'Manual', description: 'You initiate everything. AI only responds when asked.' },
  { label: 'Suggest', description: 'AI places drafts in the approval queue for your review.' },
  { label: 'Draft', description: 'AI creates full drafts with brand scores. You approve before publishing.' },
  { label: 'AutoPublish', description: 'AI publishes automatically when brand score meets threshold.' },
  { label: 'FullAuto', description: 'AI skips the approval queue entirely. Content publishes without review.' },
];

const LEVEL_VALUES: readonly AutonomyLevel[] = ['Manual', 'Suggest', 'Draft', 'AutoPublish', 'FullAuto'];

@Component({
  selector: 'app-autonomy-dial',
  standalone: true,
  imports: [CommonModule, FormsModule, CardModule, SliderModule, InputNumberModule, ButtonModule, ConfirmDialog],
  providers: [ConfirmationService],
  template: `
    <p-card header="Autonomy Level">
      <div class="autonomy-dial">
        <p-slider [ngModel]="levelIndex()" (ngModelChange)="onLevelChange($event)" [min]="0" [max]="4" [step]="1" styleClass="w-full" />
        <div class="level-info">
          <span class="level-label">{{ currentInfo().label }}</span>
          <span class="level-desc">{{ currentInfo().description }}</span>
        </div>

        @if (levelIndex() === 3) {
          <div class="threshold-section">
            <label>Minimum Brand Score for Auto-Publish</label>
            <p-inputNumber [ngModel]="threshold()" (ngModelChange)="threshold.set($event)" [min]="0" [max]="100" [showButtons]="true" />
            <small>Content must score at or above this threshold to be auto-published.</small>
          </div>
        }

        <div class="actions">
          <p-button label="Save" icon="pi pi-check" (onClick)="save()" [disabled]="!dirty()" [loading]="false" />
          <p-button label="Reset" icon="pi pi-undo" [text]="true" (onClick)="reset()" [disabled]="!dirty()" />
        </div>
      </div>
    </p-card>
    <p-confirmDialog />
  `,
  styles: `
    .autonomy-dial { display: flex; flex-direction: column; gap: 1.25rem; }
    .level-info { display: flex; flex-direction: column; gap: 0.25rem; }
    .level-label { font-weight: 600; font-size: 1.1rem; }
    .level-desc { color: var(--p-text-muted-color); }
    .threshold-section { display: flex; flex-direction: column; gap: 0.5rem; }
    .threshold-section label { font-weight: 500; }
    .threshold-section small { color: var(--p-text-muted-color); }
    .actions { display: flex; gap: 0.5rem; padding-top: 0.5rem; }
  `,
})
export class AutonomyDialComponent {
  private readonly confirmationService = inject(ConfirmationService);

  readonly autonomy = input<AutonomySettings | undefined>();
  readonly autonomyChange = output<AutonomySettings>();

  readonly levelIndex = signal(0);
  readonly threshold = signal(75);
  private previousIndex = 0;
  private initialized = false;

  readonly currentInfo = computed(() => LEVEL_INFO[this.levelIndex()]);
  readonly dirty = computed(() => {
    const a = this.autonomy();
    if (!a) return this.levelIndex() !== 0;
    return LEVEL_VALUES[this.levelIndex()] !== a.globalLevel || this.threshold() !== a.autoPublishThreshold;
  });

  constructor() {
    effect(() => {
      const a = this.autonomy();
      if (a && !this.initialized) {
        this.initialized = true;
        const idx = LEVEL_VALUES.indexOf(a.globalLevel);
        this.levelIndex.set(idx >= 0 ? idx : 0);
        this.threshold.set(a.autoPublishThreshold);
        this.previousIndex = this.levelIndex();
      }
    });
  }

  onLevelChange(index: number) {
    if (index === 4) {
      this.confirmationService.confirm({
        header: 'Enable Full Autonomy?',
        message: 'FullAuto skips the approval queue entirely. All AI-generated content will be published without human review. Are you sure?',
        icon: 'pi pi-exclamation-triangle',
        acceptButtonStyleClass: 'p-button-danger',
        accept: () => {
          this.previousIndex = index;
          this.levelIndex.set(index);
        },
        reject: () => {
          this.levelIndex.set(this.previousIndex);
        },
      });
    } else {
      this.previousIndex = index;
      this.levelIndex.set(index);
    }
  }

  save() {
    this.autonomyChange.emit({
      globalLevel: LEVEL_VALUES[this.levelIndex()],
      autoPublishThreshold: this.threshold(),
    });
  }

  reset() {
    const a = this.autonomy();
    if (a) {
      const idx = LEVEL_VALUES.indexOf(a.globalLevel);
      this.levelIndex.set(idx >= 0 ? idx : 0);
      this.threshold.set(a.autoPublishThreshold);
      this.previousIndex = this.levelIndex();
    }
  }
}
