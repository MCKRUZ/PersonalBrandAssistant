import { Component, computed, input, output } from '@angular/core';
import { IdeaFilterState } from '../../../../models/idea.model';

interface Chip { key: keyof IdeaFilterState; label: string; }

@Component({
  selector: 'app-active-filter-chips',
  standalone: true,
  template: `
    @if (chips().length > 0) {
      <div class="chips">
        @for (chip of chips(); track chip.key) {
          <span class="filter-chip" data-testid="filter-chip">
            {{ chip.label }}
            <button type="button" aria-label="Remove filter" (click)="clear.emit(chip.key)">
              <i class="pi pi-times"></i>
            </button>
          </span>
        }
      </div>
    }
  `,
  styles: [`
    .chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .filter-chip {
      display: inline-flex; align-items: center; gap: 6px;
      font-size: 12px; color: var(--text-primary);
      background: var(--accent-soft); border: 1px solid var(--surface-border);
      border-radius: var(--r-pill); padding: 3px 6px 3px 10px;
    }
    .filter-chip button { background: none; border: none; color: var(--text-secondary); cursor: pointer; display: flex; padding: 0; }
    .filter-chip button:hover { color: var(--text-primary); }
    .filter-chip i { font-size: 10px; }
  `],
})
export class ActiveFilterChipsComponent {
  readonly filter = input.required<IdeaFilterState>();
  readonly clear = output<keyof IdeaFilterState>();

  readonly chips = computed<Chip[]>(() => {
    const f = this.filter();
    const out: Chip[] = [];
    if (f.searchText) out.push({ key: 'searchText', label: `Search: ${f.searchText}` });
    if (f.status) out.push({ key: 'status', label: `Status: ${f.status}` });
    if (f.category) out.push({ key: 'category', label: `Category: ${f.category}` });
    if (f.sourceId) out.push({ key: 'sourceId', label: 'Source' });
    if (f.minScore != null) out.push({ key: 'minScore', label: `Score ≥ ${f.minScore}` });
    if (f.dateFrom) out.push({ key: 'dateFrom', label: `From ${f.dateFrom}` });
    if (f.dateTo) out.push({ key: 'dateTo', label: `To ${f.dateTo}` });
    for (const tag of f.tags) out.push({ key: 'tags', label: `#${tag}` });
    return out;
  });
}
