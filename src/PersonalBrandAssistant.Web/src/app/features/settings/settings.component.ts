import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { BudgetPanelComponent } from './components/budget-panel.component';
import { UsagePanelComponent } from './components/usage-panel.component';
import { NewsFeedsPanelComponent } from './components/news-feeds-panel.component';
import { NewsPreferencesPanelComponent } from './components/news-preferences-panel.component';
import { SettingsStore } from './store/settings.store';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    CommonModule, PageHeaderComponent, LoadingSpinnerComponent,
    BudgetPanelComponent, UsagePanelComponent, NewsFeedsPanelComponent, NewsPreferencesPanelComponent,
  ],
  template: `
    <app-page-header title="Settings" />

    @if (store.loading()) {
      <app-loading-spinner message="Loading settings..." />
    } @else {
      <div class="grid">
        <div class="col-12 md:col-6">
          <app-budget-panel [budget]="store.budget()" />
        </div>
        <div class="col-12 md:col-6">
          <app-usage-panel />
        </div>
        <div class="col-12 md:col-6" style="margin-top: 1rem;">
          <app-news-preferences-panel />
        </div>
        <div class="col-12" style="margin-top: 1rem;">
          <app-news-feeds-panel />
        </div>
      </div>
    }
  `,
})
export class SettingsComponent implements OnInit {
  readonly store = inject(SettingsStore);

  ngOnInit() {
    this.store.loadBudget(undefined);
  }
}
