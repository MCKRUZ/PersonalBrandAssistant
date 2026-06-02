import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { PaginatorModule } from 'primeng/paginator';
import { InputTextModule } from 'primeng/inputtext';
import { ContentStore } from '../stores/content.store';
import { ContentFilterSidebarComponent } from './content-filter-sidebar/content-filter-sidebar.component';
import { ContentViewToggleComponent } from './view-toggle/view-toggle.component';
import { ContentGridComponent } from './content-grid/content-grid.component';
import { ContentListTableComponent } from './content-list-table/content-list-table.component';

@Component({
  selector: 'app-content-list',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    ButtonModule,
    PaginatorModule,
    InputTextModule,
    ContentFilterSidebarComponent,
    ContentViewToggleComponent,
    ContentGridComponent,
    ContentListTableComponent,
  ],
  template: `
    <div class="content-layout" data-testid="content-list-page">
      <aside class="filter-sidebar">
        <app-content-filter-sidebar />
      </aside>

      <main class="content-main">
        <header class="content-header">
          <div class="header-top">
            <h1>Content Studio</h1>
            <p-button
              label="New Content"
              icon="pi pi-plus"
              (onClick)="onNewContent()"
              data-testid="new-content-btn" />
          </div>
          <div class="header-controls">
            <div class="search-wrapper">
              <i class="pi pi-search"></i>
              <input type="text" pInputText
                placeholder="Search content..."
                [(ngModel)]="searchText"
                (input)="onSearchInput()"
                data-testid="search-input" />
            </div>
            <app-content-view-toggle />
          </div>
        </header>

        @if (store.loading()) {
          <div class="loading-state">Loading content...</div>
        } @else {
          @if (store.viewMode() === 'grid') {
            <app-content-grid
              [contents]="store.contents()"
              (edit)="onEdit($event)"
              (onDelete)="onDelete($event)"
              (duplicate)="onDuplicate($event)" />
          } @else {
            <app-content-list-table
              [contents]="store.contents()"
              (edit)="onEdit($event)"
              (onDelete)="onDelete($event)"
              (duplicate)="onDuplicate($event)" />
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
    </div>
  `,
  styles: [
    `
      .content-layout {
        display: grid;
        grid-template-columns: 240px 1fr;
        gap: 0;
        height: 100%;
        min-height: 0;
      }
      .filter-sidebar {
        overflow-y: auto;
      }
      .content-main {
        padding: 16px 24px;
        overflow-y: auto;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
      .content-header {
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
    `,
  ],
})
export class ContentListComponent implements OnInit {
  readonly store = inject(ContentStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  searchText = '';
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.store.loadContents();
    this.destroyRef.onDestroy(() => {
      if (this.searchTimer) clearTimeout(this.searchTimer);
    });
  }

  onSearchInput(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.store.setFilter('search', this.searchText || undefined);
    }, 300);
  }

  onNewContent(): void {
    this.router.navigate(['/content/new']);
  }

  onEdit(id: string): void {
    this.router.navigate(['/content', id]);
  }

  onDelete(id: string): void {
    if (confirm('Are you sure you want to delete this content?')) {
      this.store.deleteContent(id);
    }
  }

  onDuplicate(_id: string): void {
    // Will be wired to content duplication in a future section
  }

  onPageChange(event: { first?: number; rows?: number }): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.store.pageSize();
    const page = Math.floor(first / rows) + 1;
    this.store.setPage(page);
  }
}
