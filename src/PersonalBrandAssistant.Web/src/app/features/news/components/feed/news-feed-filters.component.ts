import { Component, inject, computed, ViewEncapsulation } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Slider } from 'primeng/slider';
import { Select } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { BadgeModule } from 'primeng/badge';
import { NewsStore } from '../../store/news.store';
import { CATEGORY_COLORS, CATEGORY_ICONS, TIME_WINDOW_OPTIONS, FeedTimeWindow } from '../../models/news.model';

@Component({
  selector: 'app-news-feed-filters',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [FormsModule, Slider, Select, InputTextModule, IconFieldModule, InputIconModule, BadgeModule],
  template: `
    <div class="filter-bar">
      <div class="filter-bar__sources">
        @for (chip of categoryChips(); track chip.category) {
          <button
            class="source-chip"
            [class.active]="chip.active"
            [style.--chip-color]="chip.color"
            (click)="toggleCategory(chip.category)"
          >
            <span class="source-chip__dot"></span>
            <i [class]="chip.icon"></i>
            <span>{{ chip.label }}</span>
          </button>
        }
      </div>

      <div class="filter-bar__divider"></div>

      <button
        class="source-chip"
        [class.active]="store.filters().showSavedOnly"
        style="--chip-color: #f59e0b;"
        (click)="toggleSavedOnly()"
      >
        <span class="source-chip__dot"></span>
        <i class="pi pi-bookmark-fill"></i>
        <span>Saved</span>
      </button>

      <button
        class="source-chip"
        [class.active]="store.filters().showAnalyzedOnly"
        style="--chip-color: #22c55e;"
        (click)="toggleAnalyzedOnly()"
      >
        <span class="source-chip__dot"></span>
        <i class="pi pi-sparkles"></i>
        <span>Analyzed</span>
      </button>

      <div class="filter-bar__divider"></div>

      <p-select
        [options]="timeWindowOptions"
        [(ngModel)]="selectedTimeWindow"
        optionLabel="label"
        (onChange)="onTimeWindowChange()"
        [style]="{ minWidth: '120px' }"
        styleClass="filter-bar__time-select"
      />

      <div class="filter-bar__relevance">
        <span class="filter-bar__label">Min {{ relevanceValue }}%</span>
        <p-slider
          [(ngModel)]="relevanceValue"
          [min]="0" [max]="100" [step]="5"
          (onSlideEnd)="onRelevanceChange()"
          [style]="{ width: '100px' }"
        />
      </div>

      <p-iconfield class="filter-bar__search">
        <p-inputicon styleClass="pi pi-search" />
        <input
          type="text" pInputText placeholder="Search..."
          [(ngModel)]="searchValue" (input)="onSearchChange()"
          class="w-full"
        />
      </p-iconfield>

      @if (hiddenByTimeCount() > 0) {
        <span class="filter-bar__hint">{{ hiddenByTimeCount() }} hidden by time filter</span>
      }

      @if (activeFilterCount() > 0) {
        <p-badge [value]="activeFilterCount().toString()" severity="contrast" />
      }
    </div>
  `,
  styles: `
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-wrap: wrap;
      padding: 0.75rem 1rem;
      border-radius: 12px;
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid rgba(255, 255, 255, 0.06);
    }
    .filter-bar__sources {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    .filter-bar__divider {
      width: 1px;
      height: 24px;
      background: rgba(255, 255, 255, 0.08);
    }
    .filter-bar__relevance {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .filter-bar__label {
      font-size: 0.72rem;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.35);
      white-space: nowrap;
      letter-spacing: 0.03em;
      text-transform: uppercase;
    }
    .filter-bar__search {
      flex: 1;
      min-width: 160px;
      max-width: 260px;
    }
    .filter-bar__time-select .p-select {
      background: rgba(255, 255, 255, 0.03);
      border-color: rgba(255, 255, 255, 0.08);
      font-size: 0.78rem;
      min-height: unset;
    }
    .filter-bar__time-select .p-select .p-select-label {
      padding: 0.4rem 0.6rem;
      font-size: 0.78rem;
    }
    .source-chip {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      padding: 0.4rem 0.75rem;
      border-radius: 20px;
      font-size: 0.78rem;
      font-weight: 600;
      letter-spacing: 0.01em;
      cursor: pointer;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: rgba(255, 255, 255, 0.03);
      color: rgba(255, 255, 255, 0.45);
      transition: all 0.2s ease;
    }
    .source-chip__dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--chip-color);
      opacity: 0.4;
      transition: opacity 0.2s, box-shadow 0.2s;
    }
    .source-chip i {
      font-size: 0.75rem;
    }
    .source-chip:hover {
      border-color: color-mix(in srgb, var(--chip-color) 40%, transparent);
      color: rgba(255, 255, 255, 0.7);
      background: color-mix(in srgb, var(--chip-color) 6%, transparent);
    }
    .source-chip:hover .source-chip__dot {
      opacity: 0.8;
    }
    .source-chip.active {
      border-color: color-mix(in srgb, var(--chip-color) 50%, transparent);
      background: color-mix(in srgb, var(--chip-color) 12%, transparent);
      color: var(--chip-color);
    }
    .source-chip.active .source-chip__dot {
      opacity: 1;
      box-shadow: 0 0 8px var(--chip-color);
    }
    .filter-bar__hint {
      font-size: 0.72rem;
      color: #f59e0b;
      white-space: nowrap;
    }
  `,
})
export class NewsFeedFiltersComponent {
  readonly store = inject(NewsStore);

  readonly timeWindowOptions = [...TIME_WINDOW_OPTIONS];
  selectedTimeWindow: FeedTimeWindow = TIME_WINDOW_OPTIONS.find(
    (o) => o.hours === this.store.filters().maxAgeHours
  ) ?? TIME_WINDOW_OPTIONS[4]; // default '3 days'

  relevanceValue = 0;
  searchValue = '';

  readonly categoryChips = computed(() => {
    const activeCategories = this.store.filters().categories;
    return this.store.availableCategories().map((category) => ({
      category,
      label: category,
      color: CATEGORY_COLORS[category] ?? '#6b7280',
      icon: CATEGORY_ICONS[category] ?? 'pi pi-th-large',
      active: activeCategories.includes(category),
    }));
  });

  readonly activeFilterCount = computed(() => {
    const f = this.store.filters();
    let count = 0;
    if (f.categories.length > 0) count++;
    if (f.maxAgeHours > 0 && f.maxAgeHours !== 72) count++;
    if (f.minRelevance > 0) count++;
    if (f.searchQuery) count++;
    if (f.showSavedOnly) count++;
    if (f.showAnalyzedOnly) count++;
    return count;
  });

  readonly hiddenByTimeCount = computed(() => {
    const filters = this.store.filters();
    if (filters.maxAgeHours <= 0) return 0;
    return this.store.allItems().length - this.store.filteredItems().length;
  });

  toggleSavedOnly() {
    this.store.updateFilters({ showSavedOnly: !this.store.filters().showSavedOnly });
  }

  toggleAnalyzedOnly() {
    this.store.updateFilters({ showAnalyzedOnly: !this.store.filters().showAnalyzedOnly });
  }

  toggleCategory(category: string) {
    const current = this.store.filters().categories;
    const next = current.includes(category)
      ? current.filter((c) => c !== category)
      : [...current, category];
    this.store.updateFilters({ categories: next });
  }

  onTimeWindowChange() {
    this.store.updateFilters({ maxAgeHours: this.selectedTimeWindow.hours });
  }

  onRelevanceChange() {
    this.store.updateFilters({ minRelevance: this.relevanceValue });
  }

  onSearchChange() {
    this.store.updateFilters({ searchQuery: this.searchValue });
  }
}
