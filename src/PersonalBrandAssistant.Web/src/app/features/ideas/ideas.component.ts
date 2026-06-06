import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { PaginatorModule } from 'primeng/paginator';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { IdeaStore } from './store/idea.store';
import { IdeaSourceStore } from './store/idea-source.store';
import { IdeaFilterSidebarComponent } from './components/idea-filter-sidebar/idea-filter-sidebar.component';
import { ViewToggleComponent } from './components/view-toggle/view-toggle.component';
import { IdeaGridComponent } from './components/idea-grid/idea-grid.component';
import { IdeaListComponent } from './components/idea-list/idea-list.component';
import { SaveIdeaDialogComponent } from './components/save-idea-dialog/save-idea-dialog.component';
import { SmartSuggestionsComponent } from './components/smart-suggestions/smart-suggestions.component';
import { ActiveFilterChipsComponent } from './components/active-filter-chips/active-filter-chips.component';
import { ScoreDistributionComponent } from './components/score-distribution/score-distribution.component';
import { Idea, IdeaFilterState } from '../../models/idea.model';
import { IdeaService } from '../../core/services/idea.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-ideas',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    ButtonModule,
    PaginatorModule,
    InputTextModule,
    SelectModule,
    IdeaFilterSidebarComponent,
    ViewToggleComponent,
    IdeaGridComponent,
    IdeaListComponent,
    SaveIdeaDialogComponent,
    SmartSuggestionsComponent,
    ActiveFilterChipsComponent,
    ScoreDistributionComponent,
  ],
  template: `
    <div class="ideas-layout" data-testid="ideas-page">
      <aside class="filter-sidebar">
        <app-idea-filter-sidebar />
      </aside>

      <main class="ideas-main">
        <header class="ideas-header">
          <div class="header-top">
            <h1>Idea Bank</h1>
            <a routerLink="sources" class="manage-sources-link">
              <i class="pi pi-cog"></i> Manage Sources
            </a>
          </div>
          <div class="header-controls">
            <div class="search-wrapper">
              <i class="pi pi-search"></i>
              <input type="text" pInputText
                placeholder="Search ideas..."
                [(ngModel)]="searchText"
                (input)="onSearchInput()"
                data-testid="search-input" />
            </div>
            <p-select [options]="sortOptions" optionLabel="label" optionValue="value"
              [(ngModel)]="sortField" (onChange)="onSortChange($event.value)"
              data-testid="sort-select" styleClass="sort-select" />
            <app-view-toggle />
          </div>
          <app-active-filter-chips [filter]="store.filter()" (clear)="onClearFilter($event)" />
        </header>

        @if (store.loading()) {
          <div class="loading-state">Loading ideas...</div>
        } @else {
          @if (store.viewMode() === 'grid') {
            <app-idea-grid
              [ideas]="store.ideas()"
              (save)="onSave($event)"
              (dismiss)="onDismiss($event)"
              (createContent)="onCreateContent($event)" />
          } @else {
            <app-idea-list
              [ideas]="store.ideas()"
              (save)="onSave($event)"
              (dismiss)="onDismiss($event)"
              (createContent)="onCreateContent($event)" />
          }

          @if (store.totalCount() > store.pageSize()) {
            <p-paginator
              [rows]="store.pageSize()"
              [totalRecords]="store.totalCount()"
              [first]="(store.page() - 1) * store.pageSize()"
              (onPageChange)="onPageChange($event)"
              data-testid="paginator" />
          }
        }
      </main>

      <app-save-idea-dialog
        [idea]="selectedIdeaForSave"
        [(visible)]="saveDialogVisible" />

      <aside class="suggestions-sidebar">
        <app-score-distribution [ideas]="store.ideas()" />
        <app-smart-suggestions (createContent)="onCreateContent($event)" />
      </aside>
    </div>
  `,
  styles: [
    `
      .ideas-layout {
        display: grid;
        grid-template-columns: 240px 1fr 280px;
        gap: 0;
        height: 100%;
        min-height: 0;
      }
      .filter-sidebar {
        overflow-y: auto;
      }
      .ideas-main {
        padding: 16px 24px;
        overflow-y: auto;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
      .ideas-header {
        display: flex;
        flex-direction: column;
        gap: 12px;
      }
      .header-top {
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
      .header-top h1 {
        font-size: 24px;
        font-weight: 600;
        margin: 0;
        color: var(--text-primary);
      }
      .manage-sources-link {
        font-size: 13px;
        color: var(--brand-primary);
        text-decoration: none;
        display: flex;
        align-items: center;
        gap: 4px;
      }
      .manage-sources-link:hover {
        text-decoration: underline;
      }
      .header-controls {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 12px;
      }
      .search-wrapper {
        position: relative;
        flex: 1;
        max-width: 400px;
      }
      .search-wrapper i {
        position: absolute;
        left: 10px;
        top: 50%;
        transform: translateY(-50%);
        color: var(--text-secondary);
        font-size: 14px;
      }
      .search-wrapper input {
        width: 100%;
        padding-left: 32px;
      }
      .loading-state {
        text-align: center;
        padding: 48px;
        color: var(--text-secondary);
      }
      .suggestions-sidebar {
        padding: 16px;
        border-left: 1px solid var(--surface-border);
        overflow-y: auto;
        display: flex;
        flex-direction: column;
      }
    `,
  ],
})
export class IdeasComponent implements OnInit {
  readonly store = inject(IdeaStore);
  private readonly sourceStore = inject(IdeaSourceStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly ideaService = inject(IdeaService);

  searchText = '';
  saveDialogVisible = false;
  selectedIdeaForSave: Idea | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly sortOptions = [
    { label: 'Newest', value: 'detectedAt' },
    { label: 'Highest score', value: 'score' },
    { label: 'Source', value: 'sourceName' },
  ];
  sortField = 'detectedAt';

  ngOnInit(): void {
    this.store.loadIdeas();
    this.sourceStore.loadAll();
    this.destroyRef.onDestroy(() => {
      if (this.searchTimer) clearTimeout(this.searchTimer);
    });
  }

  onSearchInput(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.store.setFilter({ searchText: this.searchText || null });
    }, 300);
  }

  onSortChange(field: string): void {
    this.sortField = field;
    this.store.setSort({ field, direction: 'desc' });
  }

  onClearFilter(key: keyof IdeaFilterState): void {
    this.store.setFilter({ [key]: key === 'tags' ? [] : null } as Partial<IdeaFilterState>);
  }

  onSave(id: string): void {
    this.selectedIdeaForSave = this.store.ideas().find((i) => i.id === id) ?? null;
    this.saveDialogVisible = true;
  }

  onDismiss(id: string): void {
    this.store.dismissIdea(id);
  }

  onCreateContent(id: string): void {
    this.ideaService
      .createContent(id, 'Blog', 'Blog')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (contentId) => this.router.navigate(['/content', contentId]),
        error: (err: Error) => this.store.setError(err.message),
      });
  }

  onPageChange(event: { first?: number; rows?: number }): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.store.pageSize();
    const page = Math.floor(first / rows) + 1;
    this.store.setPage(page);
  }
}
