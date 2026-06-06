import { Component, inject, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { IdeaStore } from '../../store/idea.store';
import { Idea } from '../../../../models/idea.model';
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';

@Component({
  selector: 'app-idea-list',
  standalone: true,
  imports: [DatePipe, ButtonModule, TooltipModule, ScoreBadgeComponent],
  template: `
    <div class="idea-rows" data-testid="idea-list">
      @for (idea of ideas(); track idea.id) {
        <div class="idea-row">
          <app-score-badge [score]="idea.score" [title]="idea.scoreReason ?? ''" />
          <div class="row-main">
            @if (idea.url) {
              <a [href]="idea.url" target="_blank" rel="noopener noreferrer" class="row-title">{{ idea.title }}</a>
            } @else {
              <span class="row-title">{{ idea.title }}</span>
            }
            <div class="row-meta">
              <span class="source">{{ idea.sourceName }}</span>
              <span class="dot">·</span>
              <span>{{ idea.detectedAt | date: 'shortDate' }}</span>
            </div>
          </div>
          <div class="row-actions">
            <p-button icon="pi pi-bookmark" severity="secondary" [text]="true" size="small"
              (onClick)="save.emit(idea.id)" data-testid="save-btn" pTooltip="Save" />
            <p-button icon="pi pi-times" severity="secondary" [text]="true" size="small"
              (onClick)="dismiss.emit(idea.id)" data-testid="dismiss-btn" pTooltip="Dismiss" />
            <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small"
              (onClick)="createContent.emit(idea.id)" data-testid="create-content-btn" pTooltip="Create Content" />
          </div>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .idea-rows { display: flex; flex-direction: column; }
      .idea-row { display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-bottom: 1px solid var(--surface-border); transition: background 0.15s; }
      .idea-row:hover { background: var(--surface-hover); }
      .row-main { flex: 1; min-width: 0; }
      .row-title { color: var(--text-primary); font-weight: 600; font-size: 14px; text-decoration: none; display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      a.row-title:hover { color: var(--brand-primary); }
      .row-meta { font-size: 12px; color: var(--text-secondary); display: flex; gap: 6px; }
      .row-actions { display: flex; gap: 2px; opacity: 0; transition: opacity 0.15s; }
      .idea-row:hover .row-actions { opacity: 1; }
    `,
  ],
})
export class IdeaListComponent {
  readonly store = inject(IdeaStore);
  readonly ideas = input.required<Idea[]>();
  readonly save = output<string>();
  readonly dismiss = output<string>();
  readonly createContent = output<string>();
}
