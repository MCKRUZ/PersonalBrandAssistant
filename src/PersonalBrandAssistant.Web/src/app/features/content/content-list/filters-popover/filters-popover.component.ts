import { ChangeDetectionStrategy, Component, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Popover, PopoverModule } from 'primeng/popover';
import { SelectModule } from 'primeng/select';
import { DatePickerModule } from 'primeng/datepicker';
import { ButtonModule } from 'primeng/button';
import { ContentStore } from '../../stores/content.store';
import { ContentType, Platform } from '../../models/content.model';

/**
 * Secondary filters (platform / type / date range) in a PrimeNG popover. Selections feed
 * `store.setFilter`, folded into the client-side `filtered()` set. Anchored to the orchestrator's
 * "Filters" button via `toggle($event)`.
 */
@Component({
  selector: 'app-filters-popover',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PopoverModule, SelectModule, DatePickerModule, ButtonModule],
  template: `
    <p-popover #op styleClass="filters-pop">
      <div class="filters" data-testid="filters-popover">
        <div class="group">
          <label>Platform</label>
          <p-select
            [options]="platformOptions"
            [(ngModel)]="selectedPlatform"
            (onChange)="onPlatformChange()"
            placeholder="All platforms"
            optionLabel="label"
            optionValue="value"
            [showClear]="true"
            appendTo="body"
            [style]="{ width: '100%' }"
            data-testid="filter-platform" />
        </div>

        <div class="group">
          <label>Type</label>
          <p-select
            [options]="typeOptions"
            [(ngModel)]="selectedType"
            (onChange)="onTypeChange()"
            placeholder="All types"
            optionLabel="label"
            optionValue="value"
            [showClear]="true"
            appendTo="body"
            [style]="{ width: '100%' }"
            data-testid="filter-type" />
        </div>

        <div class="group">
          <label>Created between</label>
          <div class="dates">
            <p-datepicker
              [(ngModel)]="dateFrom"
              (onSelect)="onDateChange()"
              (onClear)="onDateChange()"
              placeholder="From"
              [showClear]="true"
              dateFormat="M dd, yy"
              appendTo="body"
              [style]="{ width: '100%' }"
              data-testid="filter-date-from" />
            <p-datepicker
              [(ngModel)]="dateTo"
              (onSelect)="onDateChange()"
              (onClear)="onDateChange()"
              placeholder="To"
              [showClear]="true"
              dateFormat="M dd, yy"
              appendTo="body"
              [style]="{ width: '100%' }"
              data-testid="filter-date-to" />
          </div>
        </div>

        <div class="actions">
          <p-button
            label="Clear"
            severity="secondary"
            [text]="true"
            (onClick)="clear()"
            data-testid="filter-clear" />
        </div>
      </div>
    </p-popover>
  `,
  styles: [`
    .filters {
      display: flex;
      flex-direction: column;
      gap: 14px;
      width: 260px;
      padding: 4px;
    }
    .group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .group label {
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.4px;
      text-transform: uppercase;
      color: var(--text-secondary);
    }
    .dates {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .actions {
      display: flex;
      justify-content: flex-end;
    }
  `],
})
export class FiltersPopoverComponent {
  readonly store = inject(ContentStore);

  @ViewChild('op') private readonly popover!: Popover;

  readonly platformOptions = [
    { label: 'Blog', value: Platform.Blog },
    { label: 'Medium', value: Platform.Medium },
    { label: 'Substack', value: Platform.Substack },
    { label: 'LinkedIn', value: Platform.LinkedIn },
    { label: 'Twitter', value: Platform.Twitter },
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

  toggle(event: Event): void {
    this.popover.toggle(event);
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

  clear(): void {
    this.selectedPlatform = null;
    this.selectedType = null;
    this.dateFrom = null;
    this.dateTo = null;
    this.store.setFilter('platform', undefined);
    this.store.setFilter('contentType', undefined);
    this.store.setFilter('dateFrom', undefined);
    this.store.setFilter('dateTo', undefined);
  }
}
