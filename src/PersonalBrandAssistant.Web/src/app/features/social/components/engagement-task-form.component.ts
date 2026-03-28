import { Component, inject, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { TextareaModule } from 'primeng/textarea';
import { MessageModule } from 'primeng/message';
import { Tooltip } from 'primeng/tooltip';
import { SocialService } from '../services/social.service';

interface SchedulingModeOption {
  readonly label: string;
  readonly value: string;
}

interface CronPreset {
  readonly label: string;
  readonly value: string;
}

@Component({
  selector: 'app-engagement-task-form',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ButtonModule, SelectModule,
    InputTextModule, InputNumberModule, ToggleSwitchModule, TextareaModule,
    MessageModule, Tooltip,
  ],
  template: `
    <div class="form-grid">
      <div class="field">
        <label>Platform</label>
        <p-select
          [options]="platforms"
          [(ngModel)]="form.platform"
          placeholder="Select platform"
          [style]="{width: '100%'}"
          pTooltip="The social media platform to engage on"
        />
      </div>

      <div class="field">
        <label>Action Type</label>
        <p-select
          [options]="taskTypes"
          [(ngModel)]="form.taskType"
          placeholder="Select action"
          [style]="{width: '100%'}"
          pTooltip="The type of engagement action (comment, like, etc.)"
        />
      </div>

      <div class="field">
        <label>Schedule</label>
        <p-select
          [options]="cronPresets"
          [(ngModel)]="selectedPreset"
          (ngModelChange)="onPresetChange($event)"
          optionLabel="label"
          optionValue="value"
          placeholder="Select schedule"
          [style]="{width: '100%'}"
          pTooltip="How frequently to execute this task"
        />
        @if (selectedPreset === 'custom') {
          <input pInputText [(ngModel)]="form.cronExpression" placeholder="* * * * *" class="mt-1 w-full" />
        }
      </div>

      <div class="field">
        <label>Scheduling Mode</label>
        <p-select
          [options]="schedulingModes"
          [(ngModel)]="form.schedulingMode"
          optionLabel="label"
          optionValue="value"
          [style]="{width: '100%'}"
          pTooltip="Fixed runs at exact times; Human-like adds randomization"
        />
        @if (form.schedulingMode === 'HumanLike') {
          <p-message severity="info" text="Execution times will be randomized weekly to avoid detection. Cron defines base frequency." />
        }
      </div>

      <div class="field">
        <label>Target Criteria (JSON)</label>
        <textarea
          pTextarea
          [(ngModel)]="form.targetCriteria"
          [rows]="4"
          placeholder='{"subreddits":["dotnet","angular"],"keywords":["Claude","AI"]}'
          class="w-full"
          pTooltip="JSON criteria for finding relevant posts"
        ></textarea>
      </div>

      <div class="field">
        <label>Max Actions Per Run</label>
        <p-inputNumber
          [(ngModel)]="form.maxActionsPerExecution"
          [min]="1"
          [max]="10"
          [showButtons]="true"
          pTooltip="Maximum actions per execution run"
        />
      </div>

      <div class="field">
        <label>Enabled</label>
        <p-toggleswitch
          [(ngModel)]="form.isEnabled"
          pTooltip="Enable this task for opportunity discovery"
        />
      </div>

      <div class="field">
        <label>Auto-Respond</label>
        <p-toggleswitch
          [(ngModel)]="form.autoRespond"
          pTooltip="Allow automatic posting without manual approval"
        />
      </div>

      <div class="form-actions">
        <p-button label="Cancel" [text]="true" (onClick)="cancelled.emit()" />
        <p-button label="Create" icon="pi pi-check" (onClick)="save()" [loading]="saving" />
      </div>
    </div>
  `,
  styles: [`
    .form-grid {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
    }
    .field label {
      font-weight: 500;
      font-size: 0.875rem;
    }
    .form-actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      padding-top: 0.5rem;
    }
    .mt-1 { margin-top: 0.5rem; }
    .w-full { width: 100%; }
  `],
})
export class EngagementTaskFormComponent {
  private readonly service = inject(SocialService);

  saved = output<void>();
  cancelled = output<void>();

  platforms = ['Reddit', 'TwitterX', 'LinkedIn', 'Instagram'];
  taskTypes = ['Comment', 'Like', 'Share', 'Follow'];
  schedulingModes: SchedulingModeOption[] = [
    { label: 'Human-like (anti-detection)', value: 'HumanLike' },
    { label: 'Fixed (exact cron)', value: 'Fixed' },
  ];
  cronPresets: CronPreset[] = [
    { label: 'Every 4 hours', value: '0 */4 * * *' },
    { label: 'Twice daily', value: '0 9,17 * * *' },
    { label: 'Daily', value: '0 10 * * *' },
    { label: 'Weekly (Monday)', value: '0 10 * * 1' },
    { label: 'Custom', value: 'custom' },
  ];

  selectedPreset = '0 */4 * * *';
  saving = false;

  form = {
    platform: 'Reddit',
    taskType: 'Comment',
    targetCriteria: '',
    cronExpression: '0 */4 * * *',
    isEnabled: true,
    autoRespond: false,
    maxActionsPerExecution: 3,
    schedulingMode: 'HumanLike',
  };

  onPresetChange(value: string) {
    if (value !== 'custom') {
      this.form.cronExpression = value;
    }
  }

  save() {
    this.saving = true;
    const request = {
      ...this.form,
      cronExpression: this.selectedPreset === 'custom'
        ? this.form.cronExpression
        : this.selectedPreset,
    };
    this.service.createTask(request).subscribe({
      next: () => {
        this.saving = false;
        this.saved.emit();
      },
      error: () => {
        this.saving = false;
      },
    });
  }
}
