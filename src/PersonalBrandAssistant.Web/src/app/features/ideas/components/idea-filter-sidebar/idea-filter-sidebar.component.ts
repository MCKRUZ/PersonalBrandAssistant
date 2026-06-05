import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { SelectModule } from 'primeng/select';
import { DatePickerModule } from 'primeng/datepicker';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { IdeaStore } from '../../store/idea.store';
import { IdeaSourceStore } from '../../store/idea-source.store';
import { IdeaStatus } from '../../../../models/idea.model';

@Component({
  selector: 'app-idea-filter-sidebar',
  standalone: true,
  imports: [FormsModule, ButtonModule, CheckboxModule, SelectModule, DatePickerModule, InputTextModule, InputNumberModule],
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
        <h4>Source</h4>
        <p-select
          [options]="sourceOptions()"
          [(ngModel)]="selectedSourceId"
          (onChange)="onSourceChange()"
          placeholder="All Sources"
          optionLabel="label"
          optionValue="value"
          [showClear]="true"
          [style]="{ width: '100%' }"
          data-testid="source-filter" />
      </section>

      <section class="filter-section">
        <h4>Category</h4>
        <input type="text" pInputText
          [(ngModel)]="categoryText"
          (input)="onCategoryChange()"
          placeholder="Filter by category"
          [style]="{ width: '100%' }" />
      </section>

      <section class="filter-section">
        <h4>Min Score</h4>
        <p-select
          [options]="scoreOptions"
          [(ngModel)]="selectedMinScore"
          (onChange)="onMinScoreChange()"
          placeholder="Any Score"
          optionLabel="label"
          optionValue="value"
          [showClear]="true"
          [style]="{ width: '100%' }"
          data-testid="min-score-filter" />
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
        background: #0d1117;
        border-right: 1px solid #21262d;
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
        color: #f0f6fc;
        margin: 0;
      }
      .filter-section {
        margin-bottom: 20px;
      }
      .filter-section h4 {
        font-size: 12px;
        font-weight: 600;
        color: #8b949e;
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
export class IdeaFilterSidebarComponent {
  readonly store = inject(IdeaStore);
  private readonly sourceStore = inject(IdeaSourceStore);

  readonly statuses = [
    { label: 'New', value: IdeaStatus.New, checked: false },
    { label: 'Saved', value: IdeaStatus.Saved, checked: false },
    { label: 'Used', value: IdeaStatus.Used, checked: false },
    { label: 'Dismissed', value: IdeaStatus.Dismissed, checked: false },
  ];

  readonly scoreOptions = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((n) => ({
    label: `${n}+`,
    value: n,
  }));

  selectedStatus: IdeaStatus | null = null;
  selectedSourceId: string | null = null;
  selectedMinScore: number | null = null;
  categoryText = '';
  dateFrom: Date | null = null;
  dateTo: Date | null = null;

  readonly sourceOptions = computed(() =>
    this.sourceStore.sources().map((s) => ({ label: s.name, value: s.id })));

  onStatusToggle(value: IdeaStatus): void {
    for (const s of this.statuses) {
      if (s.value !== value) s.checked = false;
    }
    const selected = this.statuses.find((s) => s.checked);
    this.selectedStatus = selected?.value ?? null;
    this.store.setFilter({ status: this.selectedStatus });
  }

  onSourceChange(): void {
    this.store.setFilter({ sourceId: this.selectedSourceId });
  }

  onCategoryChange(): void {
    this.store.setFilter({ category: this.categoryText || null });
  }

  onMinScoreChange(): void {
    this.store.setFilter({ minScore: this.selectedMinScore });
  }

  onDateChange(): void {
    this.store.setFilter({
      dateFrom: this.dateFrom?.toISOString() ?? null,
      dateTo: this.dateTo?.toISOString() ?? null,
    });
  }

  clearAll(): void {
    this.selectedStatus = null;
    for (const s of this.statuses) s.checked = false;
    this.selectedSourceId = null;
    this.selectedMinScore = null;
    this.categoryText = '';
    this.dateFrom = null;
    this.dateTo = null;
    this.store.setFilter({
      status: null,
      sourceId: null,
      category: null,
      tags: [],
      dateFrom: null,
      dateTo: null,
      searchText: null,
      minScore: null,
    });
  }
}
