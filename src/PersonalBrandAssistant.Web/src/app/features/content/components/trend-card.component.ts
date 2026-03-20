import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Card } from 'primeng/card';
import { ProgressBar } from 'primeng/progressbar';
import { Tag } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { TrendSuggestion } from '../../../shared/models';

@Component({
  selector: 'app-trend-card',
  standalone: true,
  imports: [CommonModule, Card, ProgressBar, Tag, ButtonModule, PlatformChipComponent],
  template: `
    <p-card>
      <div class="flex flex-column gap-2">
        <div class="flex justify-content-between align-items-start">
          <h3 class="m-0">{{ trend().topic }}</h3>
          <p-tag [value]="trend().suggestedContentType" severity="info" />
        </div>

        <p class="text-color-secondary m-0">{{ trend().rationale }}</p>

        <div>
          <span class="text-sm font-semibold">Relevance</span>
          <p-progressbar
            [value]="trend().relevanceScore * 100"
            [showValue]="false"
            [style]="{ height: '8px' }"
          />
        </div>

        <div class="flex gap-1 flex-wrap">
          @for (p of trend().suggestedPlatforms; track p) {
            <app-platform-chip [platform]="p" />
          }
        </div>

        <div class="flex gap-2 justify-content-end">
          <p-button label="Dismiss" severity="secondary" size="small" icon="pi pi-times" (onClick)="dismissed.emit()" />
          <p-button label="Accept" severity="success" size="small" icon="pi pi-check" (onClick)="accepted.emit()" />
        </div>
      </div>
    </p-card>
  `,
  styles: `h3 { font-size: 1.1rem; }`,
})
export class TrendCardComponent {
  trend = input.required<TrendSuggestion>();
  accepted = output<void>();
  dismissed = output<void>();
}
