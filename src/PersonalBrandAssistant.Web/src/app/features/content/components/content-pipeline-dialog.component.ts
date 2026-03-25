import { Component, computed, inject, input, OnInit, output, signal, ViewEncapsulation } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { marked } from 'marked';
import { Dialog } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { MultiSelect } from 'primeng/multiselect';
import { MessageService } from 'primeng/api';
import { ContentService } from '../services/content.service';
import { ContentType, PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-content-pipeline-dialog',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [
    ReactiveFormsModule, Dialog, ButtonModule,
    InputTextModule, Select, MultiSelect,
  ],
  template: `
    <p-dialog
      [header]="stepHeader()"
      [(visible)]="visible"
      [modal]="true"
      [style]="{ width: '680px' }"
      [contentStyle]="{ padding: '1.5rem', maxHeight: '75vh', overflowY: 'auto' }"
      [closable]="!processing()"
      (onHide)="close()"
    >

      <!-- Step indicator -->
      <div class="pipeline-steps">
        @for (s of steps; track s.n) {
          <div class="pipeline-step" [class.pipeline-step--active]="step() === s.n" [class.pipeline-step--done]="step() > s.n">
            <div class="pipeline-step__bubble">{{ s.n }}</div>
            <span class="pipeline-step__label">{{ s.label }}</span>
          </div>
          @if (!$last) { <div class="pipeline-step__line"></div> }
        }
      </div>

      <!-- Step 1: Topic -->
      @if (step() === 1) {
        <form [formGroup]="topicForm" style="display: flex; flex-direction: column; gap: 1rem; margin-top: 1.25rem;">
          <div>
            <label style="display: block; font-size: 0.85rem; font-weight: 600; margin-bottom: 0.4rem;">Topic / Angle</label>
            <input pInputText formControlName="topic" style="width: 100%;" placeholder="What's the main angle or insight?" />
          </div>
          <div>
            <label style="display: block; font-size: 0.85rem; font-weight: 600; margin-bottom: 0.4rem;">Content Type</label>
            <p-select
              formControlName="type"
              [options]="contentTypes"
              optionLabel="label"
              optionValue="value"
              [style]="{ width: '100%' }"
            />
          </div>
          <div>
            <label style="display: block; font-size: 0.85rem; font-weight: 600; margin-bottom: 0.4rem;">Platform(s)</label>
            <p-multiselect
              formControlName="targetPlatforms"
              [options]="platforms"
              optionLabel="label"
              optionValue="value"
              [style]="{ width: '100%' }"
              placeholder="Select platform(s)"
            />
          </div>
          <div class="pipeline-footer">
            <p-button label="Cancel" severity="secondary" (onClick)="close()" />
            <p-button
              label="Generate Outline"
              icon="pi pi-sparkles"
              [loading]="processing()"
              [disabled]="topicForm.invalid"
              (onClick)="createAndOutline()"
            />
          </div>
        </form>
      }

      <!-- Step 2: Outline -->
      @if (step() === 2) {
        <div style="display: flex; flex-direction: column; gap: 1rem; margin-top: 1.25rem;">
          @if (bodyHtml()) {
            <div class="pipeline-output pipeline-md" [innerHTML]="bodyHtml()"></div>
          }
          <div class="pipeline-footer">
            <p-button label="Back" severity="secondary" icon="pi pi-arrow-left" (onClick)="step.set(1)" />
            <p-button
              label="Generate Draft"
              icon="pi pi-sparkles"
              [loading]="processing()"
              (onClick)="generateDraft()"
            />
          </div>
        </div>
      }

      <!-- Step 3: Draft -->
      @if (step() === 3) {
        <div style="display: flex; flex-direction: column; gap: 1rem; margin-top: 1.25rem;">
          @if (bodyHtml()) {
            <div class="pipeline-output pipeline-md" [innerHTML]="bodyHtml()"></div>
          }
          <div class="pipeline-footer">
            <p-button label="Back" severity="secondary" icon="pi pi-arrow-left" (onClick)="step.set(2)" />
            <p-button
              label="Submit for Review"
              icon="pi pi-check"
              [loading]="processing()"
              (onClick)="submitForReview()"
            />
          </div>
        </div>
      }

      <!-- Step 4: Done -->
      @if (step() === 4) {
        <div style="display: flex; flex-direction: column; align-items: center; gap: 1rem; padding: 2rem 1rem; text-align: center; margin-top: 1rem;">
          <i class="pi pi-check-circle" style="font-size: 3rem; color: #22c55e;"></i>
          <p style="margin: 0; font-size: 1rem; font-weight: 600; color: var(--p-text-color);">Content submitted for review</p>
          <p style="margin: 0; font-size: 0.85rem; color: var(--p-text-muted-color);">Find it in the Content section to edit and publish.</p>
          <p-button label="Close" (onClick)="close()" />
        </div>
      }

    </p-dialog>
  `,
  styles: [`
    .pipeline-steps {
      display: flex;
      align-items: center;
      gap: 0;
    }
    .pipeline-step {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-shrink: 0;
    }
    .pipeline-step__bubble {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.75rem;
      font-weight: 700;
      border: 2px solid rgba(255, 255, 255, 0.15);
      color: rgba(255, 255, 255, 0.35);
      transition: all 0.2s ease;
    }
    .pipeline-step--active .pipeline-step__bubble {
      border-color: var(--p-primary-color);
      color: var(--p-primary-color);
    }
    .pipeline-step--done .pipeline-step__bubble {
      border-color: #22c55e;
      background: #22c55e;
      color: white;
    }
    .pipeline-step__label {
      font-size: 0.78rem;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.25);
      transition: color 0.2s ease;
    }
    .pipeline-step--active .pipeline-step__label {
      color: var(--p-primary-color);
    }
    .pipeline-step--done .pipeline-step__label {
      color: rgba(255, 255, 255, 0.5);
    }
    .pipeline-step__line {
      flex: 1;
      height: 1px;
      background: rgba(255, 255, 255, 0.1);
      margin: 0 0.5rem;
    }
    .pipeline-output {
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 10px;
      padding: 1rem 1.25rem;
      max-height: 380px;
      overflow-y: auto;
    }
    .pipeline-md { font-size: 0.85rem; line-height: 1.7; color: var(--p-text-color); }
    .pipeline-md p { margin: 0 0 0.75rem; }
    .pipeline-md p:last-child { margin-bottom: 0; }
    .pipeline-md h1, .pipeline-md h2, .pipeline-md h3 {
      font-size: 0.9rem; font-weight: 700; margin: 1rem 0 0.4rem; color: var(--p-text-color);
    }
    .pipeline-md strong { font-weight: 700; color: var(--p-text-color); }
    .pipeline-md em { font-style: italic; }
    .pipeline-md hr { border: none; border-top: 1px solid rgba(255,255,255,0.08); margin: 0.75rem 0; }
    .pipeline-md ul, .pipeline-md ol { padding-left: 1.25rem; margin: 0 0 0.75rem; }
    .pipeline-md li { margin-bottom: 0.25rem; }
    .pipeline-md code { font-family: monospace; font-size: 0.82rem; background: rgba(255,255,255,0.06); padding: 0.1rem 0.3rem; border-radius: 4px; }
    .pipeline-md blockquote { border-left: 3px solid rgba(255,255,255,0.15); margin: 0 0 0.75rem; padding: 0.25rem 0.75rem; color: var(--p-text-muted-color); }
    .pipeline-footer {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      padding-top: 0.5rem;
      border-top: 1px solid rgba(255, 255, 255, 0.06);
    }
  `],
})
export class ContentPipelineDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly contentService = inject(ContentService);
  private readonly messageService = inject(MessageService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly initialIdea = input<{ topic: string; type: ContentType; platform: PlatformType } | null>(null);
  closed = output<void>();

  visible = false;
  readonly step = signal<1 | 2 | 3 | 4>(1);
  readonly processing = signal(false);
  readonly contentId = signal<string | null>(null);
  readonly bodyText = signal<string | null>(null);
  readonly bodyHtml = computed((): SafeHtml | null => {
    const md = this.bodyText();
    if (!md) return null;
    const html = marked.parse(md, { async: false }) as string;
    return this.sanitizer.bypassSecurityTrustHtml(html);
  });

  readonly steps = [
    { n: 1, label: 'Topic' },
    { n: 2, label: 'Outline' },
    { n: 3, label: 'Draft' },
    { n: 4, label: 'Review' },
  ];

  readonly contentTypes = [
    { label: 'Social Post', value: 'SocialPost' as ContentType },
    { label: 'Thread', value: 'Thread' as ContentType },
    { label: 'Blog Post', value: 'BlogPost' as ContentType },
    { label: 'Video Description', value: 'VideoDescription' as ContentType },
  ];

  readonly platforms = [
    { label: 'LinkedIn', value: 'LinkedIn' as PlatformType },
    { label: 'Twitter / X', value: 'TwitterX' as PlatformType },
    { label: 'Instagram', value: 'Instagram' as PlatformType },
    { label: 'YouTube', value: 'YouTube' as PlatformType },
    { label: 'Reddit', value: 'Reddit' as PlatformType },
    { label: 'matthewkruczek.ai', value: 'PersonalBlog' as PlatformType },
    { label: 'Substack', value: 'Substack' as PlatformType },
  ];

  readonly topicForm = this.fb.group({
    topic: ['', Validators.required],
    type: ['SocialPost' as ContentType, Validators.required],
    targetPlatforms: [[] as PlatformType[]],
  });

  /** Called from content-list toolbar — opens with blank form. */
  open() {
    this.topicForm.reset({ type: 'SocialPost', targetPlatforms: [] });
    this.step.set(1);
    this.contentId.set(null);
    this.bodyText.set(null);
    this.visible = true;
  }

  ngOnInit() {
    const idea = this.initialIdea();
    if (idea) {
      this.topicForm.patchValue({
        topic: idea.topic,
        type: idea.type,
        targetPlatforms: [idea.platform],
      });
    }
    this.visible = true;
  }

  stepHeader(): string {
    return ['', 'Topic', 'Outline', 'Draft', 'Done'][this.step()] ?? 'AI Content Pipeline';
  }

  createAndOutline() {
    if (this.topicForm.invalid) return;
    this.processing.set(true);
    const val = this.topicForm.getRawValue();

    this.contentService.createViaPipeline({
      type: val.type!,
      topic: val.topic!,
      targetPlatforms: val.targetPlatforms ?? [],
    }).subscribe({
      next: id => {
        this.contentId.set(id);
        this.contentService.generateOutline(id).subscribe({
          next: outline => {
            this.bodyText.set(outline);
            this.step.set(2);
            this.processing.set(false);
          },
          error: () => this.processing.set(false),
        });
      },
      error: () => this.processing.set(false),
    });
  }

  generateDraft() {
    const id = this.contentId();
    if (!id) return;
    this.processing.set(true);
    this.contentService.generateDraft(id).subscribe({
      next: draft => {
        this.bodyText.set(draft);
        this.step.set(3);
        this.processing.set(false);
      },
      error: () => this.processing.set(false),
    });
  }

  submitForReview() {
    const id = this.contentId();
    if (!id) return;
    this.processing.set(true);
    this.contentService.submitForReview(id).subscribe({
      next: () => {
        this.step.set(4);
        this.processing.set(false);
        this.messageService.add({ severity: 'success', summary: 'Submitted', detail: 'Content submitted for review' });
      },
      error: () => this.processing.set(false),
    });
  }

  close() {
    this.visible = false;
    this.closed.emit();
  }
}
