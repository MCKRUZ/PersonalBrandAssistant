import { Component, inject, input, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { ProgressSpinner } from 'primeng/progressspinner';
import { MessageService } from 'primeng/api';
import { LoadingSpinnerComponent } from '../../../../shared/components/loading-spinner/loading-spinner.component';
import { BlogPublishService } from '../../services/blog-publish.service';
import { BlogHtmlResult, BlogDeployStatus } from '../../models/blog-publish.models';

type PublishState = 'idle' | 'publishing' | 'verifying' | 'published' | 'failed';

@Component({
  selector: 'app-blog-publish',
  standalone: true,
  imports: [CommonModule, ButtonModule, Card, Tag, ProgressSpinner, LoadingSpinnerComponent],
  template: `
    <p-card header="Blog Publish">
      @if (loading()) {
        <app-loading-spinner message="Loading blog preview..." />
      } @else {
        <!-- HTML Preview -->
        @if (htmlPreview(); as preview) {
          <div class="mb-3">
            <span class="text-sm text-color-secondary">File: {{ preview.filePath }}</span>
          </div>
          <div class="border-1 surface-border border-round mb-4" style="height: 300px; overflow: hidden;">
            <iframe [srcdoc]="preview.html" sandbox="" class="w-full h-full border-none"></iframe>
          </div>
        }

        <!-- Status Display -->
        @if (status(); as s) {
          <div class="flex align-items-center gap-3 mb-4 p-3 surface-ground border-round">
            <p-tag [value]="s.status" [severity]="statusSeverity(s.status)" />
            @if (s.commitSha) {
              <span class="text-sm text-color-secondary">SHA: {{ s.commitSha | slice:0:8 }}</span>
            }
            @if (s.blogUrl) {
              <a [href]="s.blogUrl" target="_blank" class="text-primary no-underline hover:underline">
                {{ s.blogUrl }}
              </a>
            }
            @if (s.publishedAt) {
              <span class="text-sm text-color-secondary">{{ s.publishedAt }}</span>
            }
            @if (s.errorMessage) {
              <span class="text-red-600 text-sm">{{ s.errorMessage }}</span>
            }
          </div>
        }

        <!-- Publish State -->
        @switch (publishState()) {
          @case ('publishing') {
            <div class="flex align-items-center gap-2 p-3">
              <p-progressSpinner strokeWidth="4" [style]="{width: '24px', height: '24px'}" />
              <span>Committing to GitHub...</span>
            </div>
          }
          @case ('verifying') {
            <div class="flex align-items-center gap-2 p-3">
              <p-progressSpinner strokeWidth="4" [style]="{width: '24px', height: '24px'}" />
              <span>Verifying deployment...</span>
            </div>
          }
          @case ('published') {
            <div class="flex align-items-center gap-2 p-3 bg-green-50 border-round">
              <i class="pi pi-check-circle text-green-600 text-xl"></i>
              <span class="text-green-700 font-semibold">Published successfully</span>
            </div>
          }
          @case ('failed') {
            <div class="p-3 bg-red-50 border-round">
              <div class="flex align-items-center gap-2 mb-2">
                <i class="pi pi-times-circle text-red-600 text-xl"></i>
                <span class="text-red-700">{{ errorMessage() }}</span>
              </div>
              <button pButton label="Retry" icon="pi pi-refresh" severity="warning"
                      (click)="publish()"></button>
            </div>
          }
        }

        <!-- Publish Button -->
        @if (publishState() === 'idle') {
          <div class="flex justify-content-end mt-3">
            <button pButton label="Publish to Blog" icon="pi pi-upload"
                    severity="success"
                    [disabled]="!substackPostUrl()"
                    [pTooltip]="!substackPostUrl() ? 'Publish to Substack first' : ''"
                    (click)="publish()"></button>
          </div>
        }
      }
    </p-card>
  `
})
export class BlogPublishComponent implements OnInit {
  contentId = input.required<string>();
  substackPostUrl = input<string | null>(null);

  private readonly publishService = inject(BlogPublishService);
  private readonly messageService = inject(MessageService);

  loading = signal(true);
  htmlPreview = signal<BlogHtmlResult | null>(null);
  status = signal<BlogDeployStatus | null>(null);
  publishState = signal<PublishState>('idle');
  errorMessage = signal('');

  ngOnInit(): void {
    this.publishService.getPrep(this.contentId()).subscribe({
      next: (preview) => {
        this.htmlPreview.set(preview);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });

    this.publishService.getStatus(this.contentId()).subscribe({
      next: (s) => {
        this.status.set(s);
        if (s.status === 'Published') this.publishState.set('published');
        else if (s.status === 'Failed') {
          this.publishState.set('failed');
          this.errorMessage.set(s.errorMessage ?? 'Unknown error');
        }
      },
      error: () => {} // No status yet
    });
  }

  publish(): void {
    this.publishState.set('publishing');

    this.publishService.publish(this.contentId()).subscribe({
      next: (result) => {
        if (result.deployed) {
          this.publishState.set('published');
          this.status.set({
            commitSha: result.commitSha,
            blogUrl: result.blogUrl,
            status: 'Published',
            publishedAt: new Date().toISOString(),
            errorMessage: null
          });
          this.messageService.add({
            severity: 'success',
            summary: 'Published',
            detail: `Blog deployed at ${result.blogUrl}`
          });
        } else {
          this.publishState.set('failed');
          this.errorMessage.set('Deploy verification timed out');
        }
      },
      error: (err) => {
        this.publishState.set('failed');
        this.errorMessage.set(err?.error?.error ?? 'Publish failed');
      }
    });
  }

  statusSeverity(status: string): 'success' | 'warning' | 'danger' | 'info' | 'secondary' {
    switch (status) {
      case 'Published': return 'success';
      case 'Publishing': return 'info';
      case 'Failed': return 'danger';
      case 'Staged': return 'warning';
      default: return 'secondary';
    }
  }
}
