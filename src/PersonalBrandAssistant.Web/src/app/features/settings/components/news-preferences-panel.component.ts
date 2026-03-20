import { Component, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Slider } from 'primeng/slider';
import { Button } from 'primeng/button';
import { TIME_WINDOW_OPTIONS, FeedTimeWindow, TrendSettings } from '../../news/models/news.model';
import { NewsStore } from '../../news/store/news.store';
import { NewsService } from '../../news/services/news.service';

@Component({
  selector: 'app-news-preferences-panel',
  standalone: true,
  imports: [Card, Select, FormsModule, ToggleSwitch, Slider, Button],
  template: `
    <p-card header="News Feed">
      <div class="flex flex-column gap-3">
        <div class="flex justify-content-between align-items-center">
          <div>
            <span class="font-semibold">Default article age</span>
            <p style="margin: 0.25rem 0 0; font-size: 0.8rem; color: rgba(255,255,255,0.35);">
              Only show articles newer than this
            </p>
          </div>
          <p-select
            [options]="timeWindowOptions"
            [(ngModel)]="selectedTimeWindow"
            optionLabel="label"
            (onChange)="onTimeWindowChange()"
            [style]="{ minWidth: '140px' }"
          />
        </div>

        <hr style="border-color: rgba(255,255,255,0.08); margin: 0.5rem 0;" />

        <div class="flex justify-content-between align-items-center">
          <div>
            <span class="font-semibold">Relevance filter</span>
            <p style="margin: 0.25rem 0 0; font-size: 0.8rem; color: rgba(255,255,255,0.35);">
              Filter articles below a relevance threshold
            </p>
          </div>
          <p-toggleswitch [(ngModel)]="relevanceFilterEnabled" />
        </div>

        @if (relevanceFilterEnabled) {
          <div>
            <div class="flex justify-content-between align-items-center mb-2">
              <span style="font-size: 0.85rem;">Minimum relevance</span>
              <span style="font-size: 0.85rem; color: rgba(255,255,255,0.5);">{{ relevancePercent }}%</span>
            </div>
            <p-slider [(ngModel)]="relevancePercent" [min]="0" [max]="100" [step]="5" />
          </div>
        }

        <div>
          <div class="flex justify-content-between align-items-center mb-2">
            <span style="font-size: 0.85rem;">Max articles per refresh</span>
            <span style="font-size: 0.85rem; color: rgba(255,255,255,0.5);">{{ maxSuggestionsPerCycle }}</span>
          </div>
          <p-slider [(ngModel)]="maxSuggestionsPerCycle" [min]="1" [max]="100" [step]="1" />
        </div>

        <p-button
          label="Save"
          icon="pi pi-check"
          [loading]="saving"
          (onClick)="saveTrendSettings()"
          severity="primary"
          [style]="{ width: '100%' }"
        />
      </div>
    </p-card>
  `,
})
export class NewsPreferencesPanelComponent implements OnInit {
  private readonly newsStore = inject(NewsStore);
  private readonly newsService = inject(NewsService);

  readonly timeWindowOptions = [...TIME_WINDOW_OPTIONS];
  selectedTimeWindow: FeedTimeWindow = TIME_WINDOW_OPTIONS[2];

  relevanceFilterEnabled = true;
  relevancePercent = 60;
  maxSuggestionsPerCycle = 10;
  saving = false;

  ngOnInit() {
    const currentHours = this.newsStore.filters().maxAgeHours;
    this.selectedTimeWindow = TIME_WINDOW_OPTIONS.find((o) => o.hours === currentHours)
      ?? TIME_WINDOW_OPTIONS[2];

    this.newsService.getTrendSettings().subscribe({
      next: (settings) => {
        this.relevanceFilterEnabled = settings.relevanceFilterEnabled;
        this.relevancePercent = Math.round(settings.relevanceScoreThreshold * 100);
        this.maxSuggestionsPerCycle = settings.maxSuggestionsPerCycle;
      },
    });
  }

  onTimeWindowChange() {
    this.newsStore.updateFilters({ maxAgeHours: this.selectedTimeWindow.hours });
  }

  saveTrendSettings() {
    this.saving = true;
    const settings: TrendSettings = {
      relevanceFilterEnabled: this.relevanceFilterEnabled,
      relevanceScoreThreshold: this.relevancePercent / 100,
      maxSuggestionsPerCycle: this.maxSuggestionsPerCycle,
    };
    this.newsService.updateTrendSettings(settings).subscribe({
      next: () => this.saving = false,
      error: () => this.saving = false,
    });
  }
}
