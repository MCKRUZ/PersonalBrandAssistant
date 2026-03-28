import { Component, inject, input, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { BlogChatComponent } from '../blog-chat/blog-chat.component';
import { SubstackPrepComponent } from '../substack-prep/substack-prep.component';
import { BlogPublishComponent } from '../blog-publish/blog-publish.component';
import { BlogChatService } from '../../services/blog-chat.service';
import { FinalizedDraft } from '../../models/blog-chat.models';
import { Content } from '../../../../shared/models';

type WorkflowStep = 'authoring' | 'substack-prep' | 'blog-publish' | 'complete';

interface StepDef {
  key: WorkflowStep;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-blog-workflow',
  standalone: true,
  imports: [
    CommonModule, Card, Tag, ButtonModule,
    BlogChatComponent, SubstackPrepComponent, BlogPublishComponent,
  ],
  template: `
    <!-- Step Indicator -->
    <div class="flex align-items-center gap-2 mb-4 p-3 surface-card border-round">
      @for (step of steps; track step.key; let i = $index) {
        <div class="flex align-items-center gap-2 cursor-pointer"
             [class.opacity-40]="!isStepReachable(step.key)"
             (click)="goToStep(step.key)">
          <div class="flex align-items-center justify-content-center border-circle w-2rem h-2rem text-sm font-bold"
               [class]="stepCircleClass(step.key)">
            @if (isStepComplete(step.key)) {
              <i class="pi pi-check"></i>
            } @else {
              {{ i + 1 }}
            }
          </div>
          <span class="text-sm font-medium"
                [class.text-primary]="activeStep() === step.key"
                [class.text-color-secondary]="activeStep() !== step.key">
            {{ step.label }}
          </span>
        </div>
        @if (i < steps.length - 1) {
          <div class="flex-1 border-top-1 mx-2"
               [class]="isStepComplete(step.key) ? 'border-green-500' : 'surface-border'"
               style="height: 1px;"></div>
        }
      }
    </div>

    <!-- Active Step Content -->
    @switch (activeStep()) {
      @case ('authoring') {
        <app-blog-chat
          [contentId]="content().id"
          (finalized)="onDraftFinalized($event)" />
      }
      @case ('substack-prep') {
        <app-substack-prep [contentId]="content().id" />
        <div class="flex justify-content-end mt-3">
          <button pButton label="Continue to Blog Publish" icon="pi pi-arrow-right"
                  [disabled]="!content().substackPostUrl"
                  (click)="activeStep.set('blog-publish')"></button>
        </div>
      }
      @case ('blog-publish') {
        <app-blog-publish
          [contentId]="content().id"
          [substackPostUrl]="content().substackPostUrl ?? null" />
      }
      @case ('complete') {
        <p-card>
          <div class="text-center p-4">
            <i class="pi pi-check-circle text-green-500 text-5xl mb-3"></i>
            <div class="text-xl font-semibold mb-2">Blog Published</div>
            <div class="text-color-secondary">
              Published to both Substack and Personal Blog.
            </div>
            @if (content().blogPostUrl) {
              <a [href]="content().blogPostUrl" target="_blank"
                 class="text-primary mt-2 inline-block">
                View on matthewkruczek.ai
              </a>
            }
          </div>
        </p-card>
      }
    }
  `
})
export class BlogWorkflowComponent implements OnInit {
  content = input.required<Content>();

  private readonly chatService = inject(BlogChatService);

  activeStep = signal<WorkflowStep>('authoring');
  hasConversation = signal(false);
  isFinalized = signal(false);

  steps: StepDef[] = [
    { key: 'authoring', label: 'Draft with Claude', icon: 'pi-comments' },
    { key: 'substack-prep', label: 'Substack Prep', icon: 'pi-envelope' },
    { key: 'blog-publish', label: 'Blog Publish', icon: 'pi-upload' },
    { key: 'complete', label: 'Done', icon: 'pi-check' },
  ];

  ngOnInit(): void {
    const c = this.content();

    // Determine current step from content state
    if (c.blogPostUrl) {
      this.activeStep.set('complete');
      this.isFinalized.set(true);
    } else if (c.substackPostUrl) {
      this.activeStep.set('blog-publish');
      this.isFinalized.set(true);
    } else if (this.contentIsFinalized(c)) {
      this.activeStep.set('substack-prep');
      this.isFinalized.set(true);
    } else {
      this.activeStep.set('authoring');
    }

    // Check if chat history exists
    this.chatService.getHistory(c.id).subscribe({
      next: (msgs) => {
        if (msgs.length > 0) this.hasConversation.set(true);
      },
      error: () => {}
    });
  }

  onDraftFinalized(draft: FinalizedDraft): void {
    this.isFinalized.set(true);
    this.activeStep.set('substack-prep');
  }

  isStepComplete(step: WorkflowStep): boolean {
    const c = this.content();
    switch (step) {
      case 'authoring': return this.isFinalized();
      case 'substack-prep': return !!c.substackPostUrl;
      case 'blog-publish': return !!c.blogPostUrl;
      case 'complete': return !!c.blogPostUrl;
    }
  }

  isStepReachable(step: WorkflowStep): boolean {
    switch (step) {
      case 'authoring': return true;
      case 'substack-prep': return this.isFinalized();
      case 'blog-publish': return !!this.content().substackPostUrl;
      case 'complete': return !!this.content().blogPostUrl;
    }
  }

  goToStep(step: WorkflowStep): void {
    if (this.isStepReachable(step)) {
      this.activeStep.set(step);
    }
  }

  stepCircleClass(step: WorkflowStep): string {
    if (this.isStepComplete(step)) return 'bg-green-500 text-white';
    if (this.activeStep() === step) return 'bg-primary text-primary-contrast';
    return 'surface-ground text-color-secondary';
  }

  private contentIsFinalized(c: Content): boolean {
    // Content is considered finalized if body is substantial (not just a brief)
    return (c.body?.length ?? 0) > 500;
  }
}
