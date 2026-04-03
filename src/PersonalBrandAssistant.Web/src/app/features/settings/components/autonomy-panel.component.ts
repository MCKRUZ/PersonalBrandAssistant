import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Card } from 'primeng/card';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Select } from 'primeng/select';
import { Slider } from 'primeng/slider';
import { InputText } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { MessageService } from 'primeng/api';
import { AutonomySettingsService } from '../services/autonomy-settings.service';
import { AutonomyLevel } from '../models/autonomy-settings.model';

@Component({
  selector: 'app-autonomy-panel',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    Card, ToggleSwitch, Select, Slider, InputText, ButtonModule, Tag,
  ],
  template: `
    <p-card header="Autonomy Controls">
      @if (loading()) {
        <div class="text-color-secondary">Loading autonomy settings...</div>
      } @else {
        <form [formGroup]="form" (ngSubmit)="save()" class="flex flex-column gap-4">
          <div class="flex flex-column gap-2">
            <label class="font-semibold" for="globalLevel">Global Autonomy Level</label>
            <p-select
              id="globalLevel"
              formControlName="globalLevel"
              [options]="autonomyLevels"
              optionLabel="label"
              optionValue="value"
              placeholder="Select level"
            />
          </div>

          <div class="flex justify-content-between align-items-center">
            <div class="flex flex-column gap-1">
              <span class="font-semibold">Auto-Publish</span>
              <span class="text-color-secondary text-sm">Publish content without manual review</span>
            </div>
            <p-toggleswitch formControlName="autoPublishEnabled" />
          </div>

          <div class="flex justify-content-between align-items-center">
            <div class="flex flex-column gap-1">
              <span class="font-semibold">Require Approval for Social</span>
              <span class="text-color-secondary text-sm">Require manual approval before posting to social media</span>
            </div>
            <p-toggleswitch formControlName="requireApprovalForSocial" />
          </div>

          <div class="flex justify-content-between align-items-center">
            <div class="flex flex-column gap-1">
              <span class="font-semibold">Auto-Schedule</span>
              <span class="text-color-secondary text-sm">Automatically schedule content to optimal time slots</span>
            </div>
            <p-toggleswitch formControlName="autoScheduleEnabled" />
          </div>

          <div class="flex flex-column gap-2">
            <div class="flex justify-content-between align-items-center">
              <span class="font-semibold">Max Auto-Posts Per Day</span>
              <p-tag [value]="form.get('maxAutoPostsPerDay')?.value?.toString() ?? '0'" severity="info" />
            </div>
            <p-slider formControlName="maxAutoPostsPerDay" [min]="0" [max]="50" [step]="1" />
          </div>

          <div class="flex flex-column gap-2">
            <label class="font-semibold" for="defaultTone">Default Tone</label>
            <input
              pInputText
              id="defaultTone"
              formControlName="defaultTone"
              placeholder="e.g. Professional, Casual, Technical"
            />
          </div>

          <div class="flex justify-content-end gap-2 mt-2">
            <p-button
              label="Reset"
              severity="secondary"
              [text]="true"
              (onClick)="reset()"
              [disabled]="!form.dirty || saving()"
            />
            <p-button
              label="Save"
              icon="pi pi-check"
              type="submit"
              [disabled]="!form.dirty || form.invalid || saving()"
              [loading]="saving()"
            />
          </div>
        </form>
      }
    </p-card>
  `,
})
export class AutonomyPanelComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly settingsService = inject(AutonomySettingsService);
  private readonly messageService = inject(MessageService);

  readonly loading = signal(true);
  readonly saving = signal(false);

  readonly autonomyLevels: { label: string; value: AutonomyLevel }[] = [
    { label: 'Manual', value: 'Manual' },
    { label: 'Assisted', value: 'Assisted' },
    { label: 'Semi-Auto', value: 'SemiAuto' },
    { label: 'Autonomous', value: 'Autonomous' },
  ];

  readonly form: FormGroup = this.fb.group({
    globalLevel: ['SemiAuto' as AutonomyLevel, Validators.required],
    autoPublishEnabled: [false],
    requireApprovalForSocial: [true],
    maxAutoPostsPerDay: [5, [Validators.required, Validators.min(0), Validators.max(100)]],
    defaultTone: ['Professional', [Validators.required, Validators.maxLength(50)]],
    autoScheduleEnabled: [false],
  });

  ngOnInit(): void {
    this.loadSettings();
  }

  save(): void {
    if (this.form.invalid || !this.form.dirty) return;

    this.saving.set(true);
    this.settingsService.updateSettings(this.form.getRawValue()).subscribe({
      next: (settings) => {
        this.form.patchValue(settings);
        this.form.markAsPristine();
        this.saving.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Saved',
          detail: 'Autonomy settings updated',
        });
      },
      error: () => {
        this.saving.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'Failed to save autonomy settings',
        });
      },
    });
  }

  reset(): void {
    this.loadSettings();
  }

  private loadSettings(): void {
    this.loading.set(true);
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        this.form.patchValue({
          globalLevel: settings.globalLevel,
          autoPublishEnabled: settings.autoPublishEnabled,
          requireApprovalForSocial: settings.requireApprovalForSocial,
          maxAutoPostsPerDay: settings.maxAutoPostsPerDay,
          defaultTone: settings.defaultTone,
          autoScheduleEnabled: settings.autoScheduleEnabled,
        });
        this.form.markAsPristine();
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'Failed to load autonomy settings',
        });
      },
    });
  }
}
