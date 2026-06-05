import { Component, inject, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { IdeaStore } from '../../store/idea.store';
import { Idea } from '../../../../models/idea.model';

@Component({
  selector: 'app-idea-list',
  standalone: true,
  imports: [DatePipe, ButtonModule, TagModule],
  template: `
    <div class="idea-list" data-testid="idea-list">
      <div class="list-header">
        <span class="col-status">Status</span>
        <span class="col-title sortable" (click)="onSort('title')">
          Title
          @if (store.sort().field === 'title') {
            <i class="pi" [class.pi-sort-up]="store.sort().direction === 'asc'"
               [class.pi-sort-down]="store.sort().direction === 'desc'"></i>
          }
        </span>
        <span class="col-source">Source</span>
        <span class="col-category">Category</span>
        <span class="col-date sortable" (click)="onSort('detectedAt')">
          Date
          @if (store.sort().field === 'detectedAt') {
            <i class="pi" [class.pi-sort-up]="store.sort().direction === 'asc'"
               [class.pi-sort-down]="store.sort().direction === 'desc'"></i>
          }
        </span>
        <span class="col-score sortable" (click)="onSort('score')">
          Score
          @if (store.sort().field === 'score') {
            <i class="pi" [class.pi-sort-up]="store.sort().direction === 'asc'"
               [class.pi-sort-down]="store.sort().direction === 'desc'"></i>
          }
        </span>
        <span class="col-actions">Actions</span>
      </div>

      @for (idea of ideas(); track idea.id) {
        <div class="list-row" data-testid="idea-row">
          <span class="col-status">
            <span class="status-dot" [attr.data-status]="idea.status"></span>
          </span>
          <span class="col-title">
            @if (idea.url) {
              <a [href]="idea.url" target="_blank" rel="noopener noreferrer" class="title-link">{{ idea.title }}</a>
            } @else {
              {{ idea.title }}
            }
          </span>
          <span class="col-source">{{ idea.sourceName }}</span>
          <span class="col-category">{{ idea.category }}</span>
          <span class="col-date">{{ idea.detectedAt | date: 'shortDate' }}</span>
          <span class="col-score">{{ idea.score != null ? idea.score + '/10' : '' }}</span>
          <span class="col-actions">
            <p-button icon="pi pi-bookmark" severity="secondary" [text]="true" size="small"
              (onClick)="save.emit(idea.id)" pTooltip="Save" />
            <p-button icon="pi pi-times" severity="secondary" [text]="true" size="small"
              (onClick)="dismiss.emit(idea.id)" pTooltip="Dismiss" />
            <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small"
              (onClick)="createContent.emit(idea.id)" pTooltip="Create Content" />
          </span>
        </div>
      } @empty {
        <div class="empty-state">
          <p>No ideas found</p>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .idea-list {
        display: flex;
        flex-direction: column;
      }
      .list-header {
        display: grid;
        grid-template-columns: 60px 1fr 120px 120px 100px 70px 120px;
        gap: 8px;
        padding: 8px 12px;
        font-size: 12px;
        font-weight: 600;
        color: #8b949e;
        text-transform: uppercase;
        border-bottom: 1px solid #21262d;
      }
      .sortable {
        cursor: pointer;
        user-select: none;
      }
      .sortable:hover {
        color: #f0f6fc;
      }
      .list-row {
        display: grid;
        grid-template-columns: 60px 1fr 120px 120px 100px 70px 120px;
        gap: 8px;
        padding: 10px 12px;
        align-items: center;
        font-size: 13px;
        color: #c9d1d9;
        border-bottom: 1px solid #161b22;
        transition: background 0.15s;
      }
      .list-row:hover {
        background: #161b22;
      }
      .status-dot {
        display: inline-block;
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: #8b949e;
      }
      .status-dot[data-status='New'] { background: #58a6ff; }
      .status-dot[data-status='Saved'] { background: #3fb950; }
      .status-dot[data-status='Used'] { background: #8b949e; }
      .status-dot[data-status='Dismissed'] { background: #f85149; }
      .col-title {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .title-link {
        color: #c9d1d9;
        text-decoration: none;
        transition: color 0.15s;
      }
      .title-link:hover {
        color: #58a6ff;
      }
      .col-source, .col-category {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: #8b949e;
      }
      .col-date {
        color: #8b949e;
        font-size: 12px;
      }
      .col-score {
        color: #8b949e;
        font-size: 12px;
        text-align: center;
      }
      .col-actions {
        display: flex;
        gap: 2px;
      }
      .empty-state {
        text-align: center;
        padding: 48px 16px;
        color: #8b949e;
      }

      @media (max-width: 768px) {
        .list-header { display: none; }
        .list-row {
          display: flex;
          flex-direction: column;
          gap: 4px;
          padding: 10px 12px;
          border-bottom: 1px solid #21262d;
        }
        .col-status { order: -1; }
        .col-title {
          white-space: normal;
          font-weight: 600;
          font-size: 0.9rem;
        }
        .col-source, .col-category, .col-date, .col-score {
          font-size: 0.72rem;
        }
        .col-actions {
          justify-content: flex-end;
          margin-top: 4px;
        }
      }
    `,
  ],
})
export class IdeaListComponent {
  readonly store = inject(IdeaStore);
  readonly ideas = input.required<Idea[]>();
  readonly save = output<string>();
  readonly dismiss = output<string>();
  readonly createContent = output<string>();

  onSort(field: string): void {
    const current = this.store.sort();
    const direction = current.field === field && current.direction === 'asc' ? 'desc' : 'asc';
    this.store.setSort({ field, direction });
  }
}
