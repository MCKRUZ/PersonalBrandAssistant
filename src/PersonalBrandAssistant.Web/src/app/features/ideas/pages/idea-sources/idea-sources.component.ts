import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { IdeaSourceStore } from '../../store/idea-source.store';
import { IdeaSource } from '../../../../models/idea.model';
import { SourceCardComponent } from './source-card/source-card.component';
import { AddSourceDialogComponent } from './add-source-dialog/add-source-dialog.component';

@Component({
  selector: 'app-idea-sources-page',
  standalone: true,
  imports: [RouterLink, ButtonModule, SourceCardComponent, AddSourceDialogComponent],
  template: `
    <div class="sources-page" data-testid="sources-page">
      <header class="sources-header">
        <div class="header-left">
          <a routerLink="/ideas" class="back-link">
            <i class="pi pi-arrow-left"></i>
          </a>
          <h1>Idea Sources</h1>
        </div>
        <div class="header-actions">
          <p-button label="Refresh All" icon="pi pi-refresh" severity="secondary"
            (onClick)="onRefreshAll()" [loading]="store.loading()" data-testid="refresh-btn" />
          <p-button label="Add Source" icon="pi pi-plus"
            (onClick)="addDialogVisible = true" data-testid="add-source-btn" />
        </div>
      </header>

      @if (store.lastRefreshCount() !== null) {
        <div class="refresh-message" data-testid="refresh-message">
          {{ store.lastRefreshCount() }} new ideas discovered
        </div>
      }

      @if (store.error()) {
        <div class="error-banner">{{ store.error() }}</div>
      }

      <div class="source-grid">
        @for (source of store.sources(); track source.id) {
          <app-source-card
            [source]="source"
            (edit)="onEdit(source)"
            (delete)="onDelete(source)"
            (toggleEnabled)="onToggleEnabled(source)" />
        } @empty {
          <div class="empty-state">
            <i class="pi pi-inbox"></i>
            <p>No sources configured yet</p>
            <p-button label="Add Your First Source" (onClick)="addDialogVisible = true" />
          </div>
        }
      </div>

      <app-add-source-dialog
        [editSource]="editingSource"
        [(visible)]="addDialogVisible"
        (saved)="onSourceSaved()"
        (visibleChange)="onDialogClose($event)" />
    </div>
  `,
  styles: [
    `
      .sources-page {
        padding: 16px 24px;
        max-width: 1200px;
      }
      .sources-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 16px;
      }
      .header-left {
        display: flex;
        align-items: center;
        gap: 12px;
      }
      .back-link {
        color: #8b949e;
        font-size: 18px;
        text-decoration: none;
      }
      .back-link:hover {
        color: #f0f6fc;
      }
      .sources-header h1 {
        font-size: 24px;
        font-weight: 600;
        margin: 0;
        color: #f0f6fc;
      }
      .header-actions {
        display: flex;
        gap: 8px;
      }
      .refresh-message {
        background: #23863633;
        color: #3fb950;
        padding: 8px 16px;
        border-radius: 6px;
        font-size: 13px;
        margin-bottom: 16px;
      }
      .error-banner {
        background: #f8514933;
        color: #f85149;
        padding: 8px 16px;
        border-radius: 6px;
        font-size: 13px;
        margin-bottom: 16px;
      }
      .source-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
        gap: 16px;
      }
      .empty-state {
        grid-column: 1 / -1;
        text-align: center;
        padding: 48px 16px;
        color: #8b949e;
      }
      .empty-state i {
        font-size: 32px;
        margin-bottom: 8px;
      }
    `,
  ],
})
export class IdeaSourcesPageComponent implements OnInit {
  readonly store = inject(IdeaSourceStore);

  addDialogVisible = false;
  editingSource: IdeaSource | null = null;

  ngOnInit(): void {
    this.store.loadAll();
  }

  onRefreshAll(): void {
    this.store.refreshAll();
  }

  onEdit(source: IdeaSource): void {
    this.editingSource = source;
    this.addDialogVisible = true;
  }

  onDelete(source: IdeaSource): void {
    if (confirm(`Delete source "${source.name}"?`)) {
      this.store.remove(source.id);
    }
  }

  onToggleEnabled(source: IdeaSource): void {
    this.store.update(source.id, { isEnabled: !source.isEnabled });
  }

  onSourceSaved(): void {
    this.editingSource = null;
  }

  onDialogClose(visible: boolean): void {
    if (!visible) {
      this.editingSource = null;
    }
  }
}
