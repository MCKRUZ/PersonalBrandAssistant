import { Component, inject, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { Stepper } from 'primeng/stepper';
import { StepPanel, StepList, Step, StepPanels } from 'primeng/stepper';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { Select } from 'primeng/select';
import { MultiSelect } from 'primeng/multiselect';
import { MessageService } from 'primeng/api';
import { ContentService } from '../services/content.service';
import { Content, ContentType, PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-content-pipeline-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, Dialog, Stepper, StepPanel,
    StepList, Step, StepPanels, ButtonModule, InputTextModule,
    TextareaModule, Select, MultiSelect,
  ],
  template: `
    <p-dialog header="AI Content Pipeline" [(visible)]="visible" [modal]="true" [style]="{ width: '650px' }">
      <p-stepper [value]="currentStep">
        <p-step-list>
          <p-step [value]="1">Topic</p-step>
          <p-step [value]="2">Outline</p-step>
          <p-step [value]="3">Draft</p-step>
          <p-step [value]="4">Review</p-step>
        </p-step-list>
        <p-step-panels>
          <p-step-panel [value]="1">
            <form [formGroup]="topicForm" class="p-3">
              <div class="mb-3">
                <label for="topic" class="block font-semibold mb-1">Topic</label>
                <input id="topic" pInputText formControlName="topic" class="w-full" />
              </div>
              <div class="mb-3">
                <label for="type" class="block font-semibold mb-1">Content Type</label>
                <p-select id="type" formControlName="type" [options]="contentTypes" optionLabel="label" optionValue="value" styleClass="w-full" />
              </div>
              <div class="mb-3">
                <label for="platforms" class="block font-semibold mb-1">Platforms</label>
                <p-multiselect id="platforms" formControlName="targetPlatforms" [options]="platforms" optionLabel="label" optionValue="value" styleClass="w-full" />
              </div>
              <div class="flex justify-content-end">
                <p-button label="Create & Generate Outline" icon="pi pi-sparkles" (onClick)="createAndOutline()" [loading]="processing" [disabled]="topicForm.invalid" />
              </div>
            </form>
          </p-step-panel>

          <p-step-panel [value]="2">
            <div class="p-3">
              @if (generatedContent) {
                <h4>Generated Outline</h4>
                <pre class="surface-ground p-3 border-round white-space-pre-wrap">{{ generatedContent.body }}</pre>
              }
              <div class="flex justify-content-end gap-2 mt-3">
                <p-button label="Back" severity="secondary" (onClick)="currentStep = 1" />
                <p-button label="Generate Draft" icon="pi pi-sparkles" (onClick)="generateDraft()" [loading]="processing" />
              </div>
            </div>
          </p-step-panel>

          <p-step-panel [value]="3">
            <div class="p-3">
              @if (generatedContent) {
                <h4>Generated Draft</h4>
                <pre class="surface-ground p-3 border-round white-space-pre-wrap">{{ generatedContent.body }}</pre>
              }
              <div class="flex justify-content-end gap-2 mt-3">
                <p-button label="Back" severity="secondary" (onClick)="currentStep = 2" />
                <p-button label="Submit for Review" icon="pi pi-check" (onClick)="submitForReview()" [loading]="processing" />
              </div>
            </div>
          </p-step-panel>

          <p-step-panel [value]="4">
            <div class="p-3 text-center">
              <i class="pi pi-check-circle text-5xl text-green-500 mb-3"></i>
              <p class="text-lg">Content submitted for review!</p>
              <p-button label="Close" (onClick)="close()" class="mt-3" />
            </div>
          </p-step-panel>
        </p-step-panels>
      </p-stepper>
    </p-dialog>
  `,
})
export class ContentPipelineDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly contentService = inject(ContentService);
  private readonly messageService = inject(MessageService);

  closed = output<void>();

  visible = false;
  currentStep = 1;
  processing = false;
  generatedContent?: Content;

  readonly contentTypes = [
    { label: 'Blog Post', value: 'BlogPost' as ContentType },
    { label: 'Social Post', value: 'SocialPost' as ContentType },
    { label: 'Thread', value: 'Thread' as ContentType },
    { label: 'Video Description', value: 'VideoDescription' as ContentType },
  ];

  readonly platforms = [
    { label: 'Twitter/X', value: 'TwitterX' as PlatformType },
    { label: 'LinkedIn', value: 'LinkedIn' as PlatformType },
    { label: 'Instagram', value: 'Instagram' as PlatformType },
    { label: 'YouTube', value: 'YouTube' as PlatformType },
  ];

  readonly topicForm = this.fb.group({
    topic: ['', Validators.required],
    type: ['BlogPost' as ContentType, Validators.required],
    targetPlatforms: [[] as PlatformType[]],
  });

  open() {
    this.visible = true;
    this.currentStep = 1;
    this.generatedContent = undefined;
    this.topicForm.reset({ type: 'BlogPost', targetPlatforms: [] });
  }

  createAndOutline() {
    if (this.topicForm.invalid) return;
    this.processing = true;
    const val = this.topicForm.getRawValue();
    this.contentService.createViaPipeline({
      type: val.type!,
      topic: val.topic!,
      targetPlatforms: val.targetPlatforms ?? [],
    }).subscribe({
      next: content => {
        this.generatedContent = content;
        this.contentService.generateOutline(content.id).subscribe({
          next: updated => {
            this.generatedContent = updated;
            this.currentStep = 2;
            this.processing = false;
          },
          error: () => { this.processing = false; },
        });
      },
      error: () => { this.processing = false; },
    });
  }

  generateDraft() {
    if (!this.generatedContent) return;
    this.processing = true;
    this.contentService.generateDraft(this.generatedContent.id).subscribe({
      next: updated => {
        this.generatedContent = updated;
        this.currentStep = 3;
        this.processing = false;
      },
      error: () => { this.processing = false; },
    });
  }

  submitForReview() {
    if (!this.generatedContent) return;
    this.processing = true;
    this.contentService.submitForReview(this.generatedContent.id).subscribe({
      next: () => {
        this.currentStep = 4;
        this.processing = false;
        this.messageService.add({ severity: 'success', summary: 'Submitted', detail: 'Content submitted for review' });
      },
      error: () => { this.processing = false; },
    });
  }

  close() {
    this.visible = false;
    this.closed.emit();
  }
}
