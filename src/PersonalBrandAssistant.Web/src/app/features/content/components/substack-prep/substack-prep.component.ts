import { Component, inject, input, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { Card } from 'primeng/card';
import { InputText } from 'primeng/inputtext';
import { Tag } from 'primeng/tag';
import { MessageService } from 'primeng/api';
import { LoadingSpinnerComponent } from '../../../../shared/components/loading-spinner/loading-spinner.component';
import { SubstackPrepService } from '../../services/substack-prep.service';
import { SubstackPreparedContent } from '../../models/substack-prep.models';

interface CopyState {
  [key: string]: boolean;
}

@Component({
  selector: 'app-substack-prep',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, Card, InputText, Tag, LoadingSpinnerComponent],
  template: `
    @if (loading()) {
      <app-loading-spinner message="Preparing Substack content..." />
    } @else if (prep(); as p) {
      <p-card>
        <ng-template pTemplate="header">
          <div class="flex align-items-center justify-content-between p-3">
            <span class="text-xl font-semibold">Substack Prep</span>
            <p-tag [value]="statusLabel()" [severity]="statusSeverity()" />
          </div>
        </ng-template>

        @for (field of fields(); track field.key) {
          <div class="mb-4 p-3 surface-ground border-round">
            <div class="flex align-items-center justify-content-between mb-2">
              <span class="font-semibold text-color-secondary">{{ field.label }}</span>
              <button pButton
                [icon]="copied()[field.key] ? 'pi pi-check' : 'pi pi-copy'"
                [severity]="copied()[field.key] ? 'success' : 'secondary'"
                class="p-button-text p-button-sm"
                (click)="copyField(field.key, field.value)"
                [label]="copied()[field.key] ? 'Copied' : 'Copy'">
              </button>
            </div>
            <div class="white-space-pre-wrap">{{ field.value }}</div>
          </div>
        }

        @if (p.tags.length > 0) {
          <div class="mb-4 p-3 surface-ground border-round">
            <div class="flex align-items-center justify-content-between mb-2">
              <span class="font-semibold text-color-secondary">Tags</span>
              <button pButton icon="pi pi-copy" severity="secondary"
                class="p-button-text p-button-sm" label="Copy"
                (click)="copyField('tags', p.tags.join(', '))"></button>
            </div>
            <div class="flex gap-2 flex-wrap">
              @for (tag of p.tags; track tag) {
                <p-tag [value]="tag" severity="info" />
              }
            </div>
          </div>
        }

        <div class="flex align-items-center gap-3 mt-4 pt-3 border-top-1 surface-border">
          <input pInputText [(ngModel)]="substackUrl" placeholder="Substack post URL (optional)"
                 class="flex-1" [disabled]="published()" />
          <button pButton label="Mark as Published" icon="pi pi-check-circle"
                  severity="success"
                  [disabled]="published()"
                  [loading]="publishing()"
                  (click)="markPublished()"></button>
        </div>
      </p-card>
    } @else {
      <p-card>
        <div class="text-color-secondary p-3">
          No Substack prep available. Finalize the blog draft first.
        </div>
      </p-card>
    }
  `
})
export class SubstackPrepComponent implements OnInit {
  contentId = input.required<string>();

  private readonly prepService = inject(SubstackPrepService);
  private readonly messageService = inject(MessageService);

  loading = signal(true);
  prep = signal<SubstackPreparedContent | null>(null);
  copied = signal<CopyState>({});
  published = signal(false);
  publishing = signal(false);
  substackUrl = '';

  fields = signal<{ key: string; label: string; value: string }[]>([]);

  ngOnInit(): void {
    this.prepService.getPrep(this.contentId()).subscribe({
      next: (p) => {
        this.prep.set(p);
        this.fields.set([
          { key: 'title', label: 'Title', value: p.title },
          { key: 'subtitle', label: 'Subtitle', value: p.subtitle },
          { key: 'body', label: 'Body', value: p.body },
          { key: 'seoDescription', label: 'SEO Description', value: p.seoDescription },
          { key: 'previewText', label: 'Preview Text', value: p.previewText },
          ...(p.sectionName ? [{ key: 'section', label: 'Section', value: p.sectionName }] : []),
        ]);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  async copyField(key: string, value: string): Promise<void> {
    await navigator.clipboard.writeText(value);
    this.copied.update(s => ({ ...s, [key]: true }));
    setTimeout(() => this.copied.update(s => ({ ...s, [key]: false })), 2000);
  }

  markPublished(): void {
    this.publishing.set(true);
    this.prepService.markPublished(this.contentId(), this.substackUrl || undefined).subscribe({
      next: () => {
        this.published.set(true);
        this.publishing.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Published',
          detail: 'Substack publication confirmed.'
        });
      },
      error: (err) => {
        this.publishing.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.error ?? 'Failed to mark as published.'
        });
      }
    });
  }

  statusLabel(): string {
    if (this.published()) return 'Published';
    if (this.prep()) return 'Ready to Copy';
    return 'Draft';
  }

  statusSeverity(): 'success' | 'info' | 'secondary' {
    if (this.published()) return 'success';
    if (this.prep()) return 'info';
    return 'secondary';
  }
}
