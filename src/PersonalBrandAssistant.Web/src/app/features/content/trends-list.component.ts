import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent, PageAction } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { TrendCardComponent } from './components/trend-card.component';
import { TrendStore } from './store/trend.store';

@Component({
  selector: 'app-trends-list',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, EmptyStateComponent, LoadingSpinnerComponent, TrendCardComponent],
  template: `
    <app-page-header title="Trending Topics" [actions]="actions" />

    @if (store.loading()) {
      <app-loading-spinner message="Loading trends..." />
    } @else if (store.suggestions().length === 0) {
      <app-empty-state message="No trend suggestions yet" icon="pi pi-chart-line" />
    } @else {
      <div class="grid">
        @for (trend of store.suggestions(); track trend.id) {
          <div class="col-12 md:col-6 lg:col-4">
            <app-trend-card
              [trend]="trend"
              (accepted)="store.accept(trend.id)"
              (dismissed)="store.dismiss(trend.id)"
            />
          </div>
        }
      </div>
    }
  `,
})
export class TrendsListComponent implements OnInit {
  readonly store = inject(TrendStore);

  readonly actions: PageAction[] = [
    { label: 'Refresh', icon: 'pi pi-refresh', command: () => this.store.refresh(undefined) },
  ];

  ngOnInit() {
    this.store.loadSuggestions(undefined);
  }
}
