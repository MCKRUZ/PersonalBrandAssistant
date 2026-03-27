import { Component, effect, inject, input, model, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { ContentService } from '../services/content.service';
import {
  ContentIdeaRecommendation, PlatformFormatOption, ContentType, PlatformType,
} from '../../../shared/models';

@Component({
  selector: 'app-content-idea-wizard',
  standalone: true,
  imports: [
    DecimalPipe, ReactiveFormsModule, Dialog, ButtonModule,
    InputTextModule, TextareaModule,
  ],
  template: `
    <p-dialog
      header="New Content Idea"
      [(visible)]="visible"
      [modal]="true"
      [style]="{ width: '680px' }"
      [contentStyle]="{ padding: '1.25rem 1.5rem', maxHeight: '75vh', overflowY: 'auto' }"
      [closable]="!analyzing()"
    >
      @if (step() === 1) {
        <form [formGroup]="storyForm" style="display: flex; flex-direction: column; gap: 1rem;">
          <div>
            <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 0.4rem;">
              Story / Article Text <span style="color: #ef4444;">*</span>
            </label>
            <textarea
              pTextarea
              formControlName="storyText"
              rows="8"
              style="width: 100%; resize: vertical;"
              placeholder="Paste the story, article summary, or TLDR analysis here..."
            ></textarea>
          </div>
          <div>
            <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 0.4rem;">
              Source URL <span style="font-weight: 400; color: var(--p-text-muted-color); font-size: 0.8rem;">(optional)</span>
            </label>
            <input pInputText formControlName="sourceUrl" style="width: 100%;" placeholder="https://..." />
          </div>
          <div style="display: flex; justify-content: flex-end; gap: 0.5rem; padding-top: 0.25rem;">
            <p-button label="Cancel" severity="secondary" (onClick)="close()" />
            <p-button
              label="Analyze Story"
              icon="pi pi-sparkles"
              [loading]="analyzing()"
              [disabled]="storyForm.invalid"
              (onClick)="analyze()"
            />
          </div>
        </form>
      }

      @if (step() === 2 && analyzing()) {
        <div style="display: flex; flex-direction: column; align-items: center; gap: 1rem; padding: 3rem 1rem; text-align: center;">
          <i class="pi pi-spin pi-spinner" style="font-size: 2.5rem; color: var(--p-primary-color);"></i>
          <p style="margin: 0; color: var(--p-text-muted-color); font-size: 0.9rem;">
            Analyzing story and finding the best platform fit...
          </p>
        </div>
      }

      @if (step() === 2 && !analyzing() && recommendation()) {
        <div style="display: flex; flex-direction: column; gap: 1rem;">

          <!-- Story context summary -->
          <div class="wizard-story-ctx">
            <div class="wizard-story-ctx__title">{{ recommendation()!.storyTitle }}</div>
            <p class="wizard-story-ctx__summary">{{ recommendation()!.storySummary }}</p>
            <div style="display: flex; flex-wrap: wrap; gap: 0.4rem;">
              @for (angle of recommendation()!.angles; track angle) {
                <span class="wizard-angle-tag">{{ angle }}</span>
              }
            </div>
          </div>

          <!-- Section label -->
          <div class="wizard-section-label">Pick a platform &amp; format</div>

          <!-- Recommendation cards -->
          <div style="display: flex; flex-direction: column; gap: 0.6rem;">
            @for (option of recommendation()!.recommendations; track option.platform + option.format) {
              <div
                class="rec-card"
                [class.rec-card--selected]="isSelected(option)"
                (click)="selectOption(option)"
                tabindex="0"
                role="option"
                [attr.aria-selected]="isSelected(option)"
                (keydown.enter)="selectOption(option)"
                (keydown.space)="selectOption(option)"
              >
                <div class="rec-card__header">
                  <i [class]="platformIcon(option.platform)" class="rec-card__icon"></i>
                  <span class="rec-card__platform">{{ platformLabel(option.platform) }}</span>
                  <span class="rec-card__format-tag">{{ formatLabel(option.format) }}</span>
                  <div class="rec-card__score" [attr.data-level]="scoreLevel(option.confidenceScore)">
                    {{ (option.confidenceScore * 100) | number:'1.0-0' }}% fit
                  </div>
                  @if (isSelected(option)) {
                    <i class="pi pi-check-circle rec-card__check"></i>
                  }
                </div>
                <div class="rec-card__angle">
                  <span class="rec-card__label">Angle:</span> {{ option.suggestedAngle }}
                </div>
                <div class="rec-card__rationale">{{ option.rationale }}</div>
              </div>
            }
          </div>

          <!-- Footer -->
          <div class="wizard-footer">
            @if (!storyContext()) {
              <p-button label="Back" severity="secondary" icon="pi pi-arrow-left" (onClick)="step.set(1)" />
            }
            <p-button label="Cancel" severity="secondary" (onClick)="close()" />
            <p-button
              [label]="selectedOptions().length > 1 ? 'Create ' + selectedOptions().length + ' Pieces' : 'Create Content'"
              icon="pi pi-plus"
              [disabled]="selectedOptions().length === 0"
              (onClick)="createContent()"
            />
          </div>
        </div>
      }
    </p-dialog>
  `,
  styles: [`
    /* Story context box */
    .wizard-story-ctx {
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 10px;
      padding: 0.875rem 1rem;
    }
    .wizard-story-ctx__title {
      font-weight: 700;
      font-size: 0.9rem;
      color: var(--p-text-color);
      margin-bottom: 0.4rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .wizard-story-ctx__summary {
      margin: 0 0 0.65rem;
      font-size: 0.8rem;
      color: var(--p-text-muted-color);
      line-height: 1.5;
      display: -webkit-box;
      -webkit-line-clamp: 3;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .wizard-angle-tag {
      font-size: 0.7rem;
      padding: 0.15rem 0.5rem;
      border-radius: 6px;
      background: rgba(139, 92, 246, 0.1);
      color: #a78bfa;
      font-weight: 600;
    }

    /* Section header */
    .wizard-section-label {
      font-size: 0.72rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: rgba(255, 255, 255, 0.35);
      padding: 0 0.25rem;
    }

    /* Recommendation cards */
    .rec-card {
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 10px;
      padding: 0.875rem 1rem;
      cursor: pointer;
      background: rgba(255, 255, 255, 0.02);
      transition: border-color 0.15s ease, background 0.15s ease;
      outline: none;
    }
    .rec-card:hover {
      border-color: rgba(139, 92, 246, 0.35);
      background: rgba(139, 92, 246, 0.04);
    }
    .rec-card:focus-visible {
      border-color: rgba(139, 92, 246, 0.5);
    }
    .rec-card--selected {
      border-color: rgba(139, 92, 246, 0.6) !important;
      background: rgba(139, 92, 246, 0.08) !important;
    }
    .rec-card__header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
      flex-wrap: wrap;
    }
    .rec-card__icon {
      font-size: 1.15rem;
      color: var(--p-primary-color);
      flex-shrink: 0;
    }
    .rec-card__platform {
      font-weight: 700;
      font-size: 0.9rem;
      color: var(--p-text-color);
    }
    .rec-card__format-tag {
      font-size: 0.7rem;
      font-weight: 600;
      padding: 0.15rem 0.5rem;
      border-radius: 6px;
      background: rgba(139, 92, 246, 0.1);
      color: #a78bfa;
    }
    .rec-card__score {
      margin-left: auto;
      font-size: 0.72rem;
      font-weight: 700;
      padding: 0.15rem 0.5rem;
      border-radius: 6px;
    }
    .rec-card__score[data-level="high"] { background: rgba(34, 197, 94, 0.12); color: #22c55e; }
    .rec-card__score[data-level="medium"] { background: rgba(234, 179, 8, 0.12); color: #eab308; }
    .rec-card__score[data-level="low"] { background: rgba(239, 68, 68, 0.12); color: #ef4444; }
    .rec-card__check {
      font-size: 1rem;
      color: var(--p-primary-color);
      flex-shrink: 0;
    }
    .rec-card__angle {
      font-size: 0.78rem;
      color: var(--p-text-muted-color);
      margin-bottom: 0.3rem;
    }
    .rec-card__label {
      font-weight: 600;
      color: rgba(255, 255, 255, 0.5);
    }
    .rec-card__rationale {
      font-size: 0.78rem;
      color: var(--p-text-muted-color);
      line-height: 1.5;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    /* Footer */
    .wizard-footer {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      padding-top: 0.75rem;
      border-top: 1px solid rgba(255, 255, 255, 0.06);
    }
  `],
})
export class ContentIdeaWizardComponent {
  private readonly contentService = inject(ContentService);
  private readonly messageService = inject(MessageService);
  private readonly fb = inject(FormBuilder);

  readonly visible = model(false);
  /** When provided, the wizard skips the paste step and analyzes immediately. */
  readonly storyContext = input<{ title: string; text: string; sourceUrl?: string } | null>(null);
  readonly contentRequested = output<{ options: PlatformFormatOption[]; storyText: string }>();

  readonly step = signal<1 | 2>(1);
  readonly analyzing = signal(false);
  readonly recommendation = signal<ContentIdeaRecommendation | null>(null);
  readonly selectedOptions = signal<PlatformFormatOption[]>([]);

  /** Tracks story text when arriving via storyContext (form is never filled). */
  private readonly contextStoryText = signal<string>('');

  readonly storyForm = this.fb.group({
    storyText: ['', [Validators.required, Validators.minLength(50)]],
    sourceUrl: [''],
  });

  constructor() {
    // When a story context is injected and the dialog becomes visible, auto-analyze
    effect(() => {
      const ctx = this.storyContext();
      if (ctx && this.visible()) {
        this.analyzeContext(ctx);
      }
    });
  }

  private analyzeContext(ctx: { title: string; text: string; sourceUrl?: string }): void {
    this.step.set(2);
    this.analyzing.set(true);
    this.recommendation.set(null);
    this.selectedOptions.set([]);

    const storyText = `# ${ctx.title}\n\n${ctx.text}`;
    this.contextStoryText.set(storyText);
    this.contentService.analyzeStory(storyText, ctx.sourceUrl).subscribe({
      next: result => {
        this.recommendation.set(result);
        this.analyzing.set(false);
      },
      error: () => {
        this.analyzing.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Analysis Failed',
          detail: 'Could not analyze story. Please close and try again.',
        });
      },
    });
  }

  analyze(): void {
    if (this.storyForm.invalid) return;

    const { storyText, sourceUrl } = this.storyForm.getRawValue();
    this.step.set(2);
    this.analyzing.set(true);
    this.recommendation.set(null);
    this.selectedOptions.set([]);

    const text = storyText!;
    this.contextStoryText.set(text);
    this.contentService.analyzeStory(text, sourceUrl ?? undefined).subscribe({
      next: result => {
        this.recommendation.set(result);
        this.analyzing.set(false);
      },
      error: () => {
        this.analyzing.set(false);
        this.step.set(1);
        this.messageService.add({
          severity: 'error',
          summary: 'Analysis Failed',
          detail: 'Could not analyze story. Please try again.',
        });
      },
    });
  }

  selectOption(option: PlatformFormatOption): void {
    const current = this.selectedOptions();
    const idx = current.findIndex(o => o.platform === option.platform && o.format === option.format);
    if (idx >= 0) {
      this.selectedOptions.set(current.filter((_, i) => i !== idx));
    } else {
      this.selectedOptions.set([...current, option]);
    }
  }

  isSelected(option: PlatformFormatOption): boolean {
    return this.selectedOptions().some(o => o.platform === option.platform && o.format === option.format);
  }

  scoreLevel(score: number): string {
    return score >= 0.7 ? 'high' : score >= 0.4 ? 'medium' : 'low';
  }

  createContent(): void {
    const options = this.selectedOptions();
    if (options.length === 0) return;

    // Use form text if on manual step, otherwise fall back to context text
    const storyText = this.storyForm.getRawValue().storyText || this.contextStoryText();
    if (!storyText) return;

    this.contentRequested.emit({ options, storyText });
    this.close();
  }

  close(): void {
    this.visible.set(false);
    this.step.set(1);
    this.analyzing.set(false);
    this.recommendation.set(null);
    this.selectedOptions.set([]);
    this.contextStoryText.set('');
    this.storyForm.reset();
  }

  platformLabel(platform: PlatformType): string {
    const labels: Record<PlatformType, string> = {
      LinkedIn: 'LinkedIn',
      TwitterX: 'Twitter / X',
      Instagram: 'Instagram',
      YouTube: 'YouTube',
      Reddit: 'Reddit',
      PersonalBlog: 'matthewkruczek.ai',
      Substack: 'Substack',
    };
    return labels[platform] ?? platform;
  }

  platformIcon(platform: PlatformType): string {
    const icons: Record<PlatformType, string> = {
      LinkedIn: 'pi pi-linkedin',
      TwitterX: 'pi pi-twitter',
      Instagram: 'pi pi-instagram',
      YouTube: 'pi pi-youtube',
      Reddit: 'pi pi-reddit',
      PersonalBlog: 'pi pi-globe',
      Substack: 'pi pi-at',
    };
    return icons[platform] ?? 'pi pi-globe';
  }

  formatLabel(format: ContentType): string {
    const labels: Record<ContentType, string> = {
      SocialPost: 'Post',
      Thread: 'Thread',
      BlogPost: 'Blog Post',
      VideoDescription: 'Video Description',
    };
    return labels[format] ?? format;
  }
}
