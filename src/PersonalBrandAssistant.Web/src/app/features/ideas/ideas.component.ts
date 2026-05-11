import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { PaginatorModule } from 'primeng/paginator';
import { InputTextModule } from 'primeng/inputtext';
import { IdeaStore } from './store/idea.store';
import { IdeaSourceStore } from './store/idea-source.store';
import { IdeaFilterSidebarComponent } from './components/idea-filter-sidebar/idea-filter-sidebar.component';
import { ViewToggleComponent } from './components/view-toggle/view-toggle.component';
import { IdeaGridComponent } from './components/idea-grid/idea-grid.component';
import { IdeaListComponent } from './components/idea-list/idea-list.component';
import { SaveIdeaDialogComponent } from './components/save-idea-dialog/save-idea-dialog.component';
import { Idea } from '../../models/idea.model';

@Component({
  selector: 'app-ideas',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    ButtonModule,
    PaginatorModule,
    InputTextModule,
    IdeaFilterSidebarComponent,
    ViewToggleComponent,
    IdeaGridComponent,
    IdeaListComponent,
    SaveIdeaDialogComponent,
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
            <app-view-toggle />
          </div>
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
        <h3>Smart Suggestions</h3>
        <p class="placeholder-text">Coming soon...</p>
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
        color: #f0f6fc;
      }
      .manage-sources-link {
        font-size: 13px;
        color: #58a6ff;
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
        color: #8b949e;
        font-size: 14px;
      }
      .search-wrapper input {
        width: 100%;
        padding-left: 32px;
      }
      .loading-state {
        text-align: center;
        padding: 48px;
        color: #8b949e;
      }
      .suggestions-sidebar {
        padding: 16px;
        border-left: 1px solid #21262d;
        overflow-y: auto;
      }
      .suggestions-sidebar h3 {
        font-size: 14px;
        font-weight: 600;
        color: #f0f6fc;
        margin: 0 0 8px;
      }
      .placeholder-text {
        font-size: 13px;
        color: #8b949e;
        margin: 0;
      }
    `,
  ],
})
export class IdeasComponent implements OnInit {
  readonly store = inject(IdeaStore);
  private readonly sourceStore = inject(IdeaSourceStore);
  private readonly destroyRef = inject(DestroyRef);

  searchText = '';
  saveDialogVisible = false;
  selectedIdeaForSave: Idea | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

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

  onSave(id: string): void {
    this.selectedIdeaForSave = this.store.ideas().find((i) => i.id === id) ?? null;
    this.saveDialogVisible = true;
  }

  onDismiss(id: string): void {
    this.store.dismissIdea(id);
  }

  onCreateContent(id: string): void {
    // Will be wired to content creation flow in section 16
  }

  onPageChange(event: { first?: number; rows?: number }): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.store.pageSize();
    const page = Math.floor(first / rows) + 1;
    this.store.setPage(page);
  }
}
