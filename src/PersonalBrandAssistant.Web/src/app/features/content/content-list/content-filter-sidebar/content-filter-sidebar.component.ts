import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { SelectModule } from 'primeng/select';
import { DatePickerModule } from 'primeng/datepicker';
import { ContentStore } from '../../stores/content.store';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';

@Component({
  selector: 'app-content-filter-sidebar',
  standalone: true,
  imports: [FormsModule, ButtonModule, CheckboxModule, SelectModule, DatePickerModule],
  template: `
    <div class="filter-sidebar" data-testid="filter-sidebar">
      <div class="filter-header">
        <h3>Filters</h3>
        <p-button label="Clear All" severity="secondary" [text]="true" size="small"
          (onClick)="clearAll()" data-testid="clear-filters" />
      </div>

      <section class="filter-section">
        <h4>Status</h4>
        @for (status of statuses; track status.value) {
          <div class="filter-checkbox">
            <p-checkbox
              [binary]="true"
              [(ngModel)]="status.checked"
              (onChange)="onStatusToggle(status.value)"
              [inputId]="'status-' + status.value" />
            <label [for]="'status-' + status.value">{{ status.label }}</label>
          </div>
        }
      </section>

      <section class="filter-section">
        <h4>Platform</h4>
        <p-select
          [options]="platformOptions"
          [(ngModel)]="selectedPlatform"
          (onChange)="onPlatformChange()"
          placeholder="All Platforms"
          optionLabel="label"
          optionValue="value"
          [showClear]="true"
          [style]="{ width: '100%' }"
          data-testid="platform-filter" />
      </section>

      <section class="filter-section">
        <h4>Content Type</h4>
        <p-select
          [options]="typeOptions"
          [(ngModel)]="selectedType"
          (onChange)="onTypeChange()"
          placeholder="All Types"
          optionLabel="label"
          optionValue="value"
          [showClear]="true"
          [style]="{ width: '100%' }"
          data-testid="type-filter" />
      </section>

      <section class="filter-section">
        <h4>Date Range</h4>
        <div class="date-range">
          <p-datepicker
            [(ngModel)]="dateFrom"
            (onSelect)="onDateChange()"
            (onClear)="onDateChange()"
            placeholder="From"
            [showClear]="true"
            dateFormat="mm/dd/yy"
            [style]="{ width: '100%' }"
            data-testid="date-from" />
          <p-datepicker
            [(ngModel)]="dateTo"
            (onSelect)="onDateChange()"
            (onClear)="onDateChange()"
            placeholder="To"
            [showClear]="true"
            dateFormat="mm/dd/yy"
            [style]="{ width: '100%' }"
            data-testid="date-to" />
        </div>
      </section>
    </div>
  `,
  styles: [
    `
      .filter-sidebar {
        padding: 16px;
        background: var(--surface-base);
        border-right: 1px solid var(--surface-elevated);
        height: 100%;
        overflow-y: auto;
      }
      .filter-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 16px;
      }
      .filter-header h3 {
        font-size: 14px;
        font-weight: 600;
        color: var(--text-primary);
        margin: 0;
      }
      .filter-section {
        margin-bottom: 20px;
      }
      .filter-section h4 {
        font-size: 12px;
        font-weight: 600;
        color: var(--text-secondary);
        text-transform: uppercase;
        margin: 0 0 8px;
      }
      .filter-checkbox {
        margin-bottom: 6px;
      }
      .date-range {
        display: flex;
        flex-direction: column;
        gap: 8px;
      }
    `,
  ],
})
export class ContentFilterSidebarComponent {
  readonly store = inject(ContentStore);

  readonly statuses = [
    { label: 'Idea', value: ContentStatus.Idea, checked: false },
    { label: 'Draft', value: ContentStatus.Draft, checked: false },
    { label: 'Review', value: ContentStatus.Review, checked: false },
    { label: 'Approved', value: ContentStatus.Approved, checked: false },
    { label: 'Scheduled', value: ContentStatus.Scheduled, checked: false },
    { label: 'Published', value: ContentStatus.Published, checked: false },
    { label: 'Archived', value: ContentStatus.Archived, checked: false },
  ];

  readonly platformOptions = [
    { label: 'Blog', value: Platform.Blog },
    { label: 'LinkedIn', value: Platform.LinkedIn },
    { label: 'Twitter', value: Platform.Twitter },
    { label: 'Substack', value: Platform.Substack },
    { label: 'Reddit', value: Platform.Reddit },
    { label: 'YouTube', value: Platform.YouTube },
  ];

  readonly typeOptions = [
    { label: 'Blog Post', value: ContentType.BlogPost },
    { label: 'LinkedIn Post', value: ContentType.LinkedInPost },
    { label: 'Tweet', value: ContentType.Tweet },
    { label: 'Threaded Tweet', value: ContentType.ThreadedTweet },
    { label: 'Newsletter', value: ContentType.SubstackNewsletter },
    { label: 'Reddit Post', value: ContentType.RedditPost },
    { label: 'YouTube Video', value: ContentType.YouTubeVideo },
    { label: 'YouTube Short', value: ContentType.YouTubeShort },
  ];

  selectedPlatform: Platform | null = null;
  selectedType: ContentType | null = null;
  dateFrom: Date | null = null;
  dateTo: Date | null = null;

  onStatusToggle(value: ContentStatus): void {
    for (const s of this.statuses) {
      if (s.value !== value) s.checked = false;
    }
    const selected = this.statuses.find((s) => s.checked);
    this.store.setFilter('status', selected?.value);
  }

  onPlatformChange(): void {
    this.store.setFilter('platform', this.selectedPlatform ?? undefined);
  }

  onTypeChange(): void {
    this.store.setFilter('contentType', this.selectedType ?? undefined);
  }

  onDateChange(): void {
    this.store.setFilter('dateFrom', this.dateFrom?.toISOString());
    this.store.setFilter('dateTo', this.dateTo?.toISOString());
  }

  clearAll(): void {
    for (const s of this.statuses) s.checked = false;
    this.selectedPlatform = null;
    this.selectedType = null;
    this.dateFrom = null;
    this.dateTo = null;
    this.store.setFilter('status', undefined);
    this.store.setFilter('platform', undefined);
    this.store.setFilter('contentType', undefined);
    this.store.setFilter('dateFrom', undefined);
    this.store.setFilter('dateTo', undefined);
    this.store.setFilter('search', undefined);
  }
}
