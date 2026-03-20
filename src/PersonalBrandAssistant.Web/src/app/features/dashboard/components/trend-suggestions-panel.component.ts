import { Component, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Card } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { ProgressBar } from 'primeng/progressbar';
import { Tag } from 'primeng/tag';
import { TrendSuggestion } from '../../../shared/models';
import { TrendStore } from '../../content/store/trend.store';

@Component({
  selector: 'app-trend-suggestions-panel',
  standalone: true,
  imports: [CommonModule, RouterLink, Card, ButtonModule, ProgressBar, Tag],
  template: `
    <p-card>
      <div class="flex justify-content-between align-items-center mb-3">
        <h3 class="m-0">Trending Topics</h3>
        <p-button label="View All" [text]="true" icon="pi pi-arrow-right" iconPos="right" routerLink="/content/trends" />
      </div>
      @for (trend of items(); track trend.id) {
        <div class="flex justify-content-between align-items-center py-2 border-bottom-1 surface-border">
          <div class="flex-1">
            <div class="font-semibold">{{ trend.topic }}</div>
            <p-progressbar [value]="trend.relevanceScore * 100" [showValue]="false" [style]="{ height: '4px', 'max-width': '120px' }" />
          </div>
          <div class="flex gap-1">
            <p-button icon="pi pi-check" severity="success" [text]="true" size="small" (onClick)="trendStore.accept(trend.id)" />
            <p-button icon="pi pi-times" severity="secondary" [text]="true" size="small" (onClick)="trendStore.dismiss(trend.id)" />
          </div>
        </div>
      }
      @if (items().length === 0) {
        <div class="text-center text-color-secondary py-3">No trends available</div>
      }
    </p-card>
  `,
})
export class TrendSuggestionsPanelComponent {
  readonly trendStore = inject(TrendStore);
  items = input<readonly TrendSuggestion[]>([]);
}
