import { Component, inject, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { MultiSelect } from 'primeng/multiselect';
import { ButtonModule } from 'primeng/button';
import { Card } from 'primeng/card';
import { ProgressBar } from 'primeng/progressbar';
import { MessageService } from 'primeng/api';
import { ContentService } from '../services/content.service';
import { PlatformType, RepurposingSuggestion } from '../../../shared/models';

@Component({
  selector: 'app-content-repurpose-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, Dialog, MultiSelect, ButtonModule, Card, ProgressBar],
  template: `
    <p-dialog header="Repurpose Content" [(visible)]="visible" [modal]="true" [style]="{ width: '500px' }">
      @if (suggestions.length > 0) {
        <h4>Suggestions</h4>
        @for (s of suggestions; track s.platform) {
          <p-card styleClass="mb-2">
            <div class="flex justify-content-between align-items-center">
              <div>
                <strong>{{ s.platform }}</strong> — {{ s.suggestedType }}
                <div class="text-sm text-color-secondary">{{ s.rationale }}</div>
              </div>
              <p-progressbar [value]="s.confidenceScore * 100" [showValue]="false" [style]="{ width: '80px', height: '6px' }" />
            </div>
          </p-card>
        }
      }

      <div class="mt-3">
        <label class="block font-semibold mb-1">Target Platforms</label>
        <p-multiselect
          [(ngModel)]="selectedPlatforms"
          [options]="platforms"
          optionLabel="label"
          optionValue="value"
          placeholder="Select platforms"
          styleClass="w-full"
        />
      </div>

      <div class="flex justify-content-end gap-2 mt-3">
        <p-button label="Cancel" severity="secondary" (onClick)="visible = false" />
        <p-button label="Repurpose" icon="pi pi-copy" (onClick)="doRepurpose()" [loading]="processing" [disabled]="selectedPlatforms.length === 0" />
      </div>
    </p-dialog>
  `,
})
export class ContentRepurposeDialogComponent {
  private readonly contentService = inject(ContentService);
  private readonly messageService = inject(MessageService);

  repurposed = output<void>();

  visible = false;
  processing = false;
  contentId = '';
  selectedPlatforms: PlatformType[] = [];
  suggestions: RepurposingSuggestion[] = [];

  readonly platforms = [
    { label: 'Twitter/X', value: 'TwitterX' as PlatformType },
    { label: 'LinkedIn', value: 'LinkedIn' as PlatformType },
    { label: 'Instagram', value: 'Instagram' as PlatformType },
    { label: 'YouTube', value: 'YouTube' as PlatformType },
  ];

  open(contentId: string) {
    this.contentId = contentId;
    this.visible = true;
    this.selectedPlatforms = [];
    this.suggestions = [];

    this.contentService.getRepurposeSuggestions(contentId).subscribe({
      next: suggestions => {
        this.suggestions = suggestions;
        this.selectedPlatforms = suggestions.map(s => s.platform);
      },
    });
  }

  doRepurpose() {
    this.processing = true;
    this.contentService.repurpose(this.contentId, this.selectedPlatforms).subscribe({
      next: () => {
        this.processing = false;
        this.visible = false;
        this.messageService.add({ severity: 'success', summary: 'Repurposed', detail: 'Content repurposed successfully' });
        this.repurposed.emit();
      },
      error: () => { this.processing = false; },
    });
  }
}
