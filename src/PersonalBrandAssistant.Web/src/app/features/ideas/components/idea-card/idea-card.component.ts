import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { Idea } from '../../../../models/idea.model';
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';

@Component({
  selector: 'app-idea-card',
  standalone: true,
  imports: [DatePipe, ButtonModule, TagModule, TooltipModule, ScoreBadgeComponent],
  template: `
    <div class="idea-card" data-testid="idea-card">
      @if (idea().thumbnailUrl) {
        <img [src]="idea().thumbnailUrl" [alt]="idea().title" class="card-thumbnail" />
      }
      <div class="card-body">
        <div class="card-header">
          <span class="status-badge" [attr.data-status]="idea().status">{{ idea().status }}</span>
          @if (idea().score !== null) {
            <app-score-badge data-testid="idea-score-badge"
              [score]="idea().score" [title]="idea().scoreReason ?? ''" />
          }
          <span class="source-name">{{ idea().sourceName }}</span>
          <span class="detected-at">{{ idea().detectedAt | date: 'shortDate' }}</span>
        </div>
        @if (idea().url) {
          <a [href]="idea().url" target="_blank" rel="noopener noreferrer" class="card-title-link">
            <h3 class="card-title">{{ idea().title }}</h3>
          </a>
        } @else {
          <h3 class="card-title">{{ idea().title }}</h3>
        }
        @if (idea().summary || idea().description) {
          <p class="card-summary">{{ truncate((idea().summary || idea().description)!, 140) }}</p>
        }
        @if (idea().tags.length > 0) {
          <div class="card-tags">
            @for (tag of idea().tags.slice(0, 3); track tag) {
              <span class="chip">{{ tag }}</span>
            }
            @if (idea().tags.length > 3) {
              <span class="more-tags">+{{ idea().tags.length - 3 }}</span>
            }
          </div>
        }
        <div class="card-actions">
          <p-button icon="pi pi-bookmark" severity="secondary" [text]="true" size="small"
            (onClick)="save.emit(idea().id)" data-testid="save-btn" pTooltip="Save" />
          <p-button icon="pi pi-times" severity="secondary" [text]="true" size="small"
            (onClick)="dismiss.emit(idea().id)" data-testid="dismiss-btn" pTooltip="Dismiss" />
          <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small"
            (onClick)="createContent.emit(idea().id)" data-testid="create-content-btn" pTooltip="Create Content" />
        </div>
      </div>
    </div>
  `,
  styles: [`
    .idea-card {
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: var(--r);
      overflow: hidden;
      transition: border-color 0.2s, transform 0.2s;
      display: flex;
      flex-direction: column;
    }
    .idea-card:hover { border-color: var(--brand-primary); transform: translateY(-1px); }
    .card-thumbnail { width: 100%; height: 140px; object-fit: cover; }
    .card-body { padding: 14px; display: flex; flex-direction: column; gap: 8px; }
    .card-header { display: flex; align-items: center; gap: 8px; font-size: 12px; color: var(--text-secondary); flex-wrap: wrap; }
    .status-badge { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: var(--r-pill); text-transform: uppercase; color: var(--text-secondary); background: var(--surface-hover); }
    .status-badge[data-status='New'] { color: var(--status-idea); background: color-mix(in srgb, var(--status-idea) 16%, transparent); }
    .status-badge[data-status='Saved'] { color: var(--status-approved); background: color-mix(in srgb, var(--status-approved) 16%, transparent); }
    .status-badge[data-status='Dismissed'] { color: var(--score-danger); background: color-mix(in srgb, var(--score-danger) 16%, transparent); }
    .source-name { margin-left: auto; }
    .card-title-link { text-decoration: none; }
    .card-title-link:hover .card-title { color: var(--brand-primary); }
    .card-title { font-size: 15px; font-weight: 600; color: var(--text-primary); margin: 0; line-height: 1.35; transition: color 0.15s; }
    .card-summary { font-size: 13px; color: var(--text-secondary); margin: 0; line-height: 1.45; }
    .card-tags { display: flex; gap: 6px; flex-wrap: wrap; }
    .chip { font-size: 11px; color: var(--text-secondary); background: var(--surface-hover); border-radius: var(--r-pill); padding: 2px 10px; }
    .more-tags { font-size: 11px; color: var(--text-muted); align-self: center; }
    .card-actions { display: flex; gap: 4px; border-top: 1px solid var(--surface-border); padding-top: 8px; margin-top: 4px; }
  `],
})
export class IdeaCardComponent {
  readonly idea = input.required<Idea>();
  readonly save = output<string>();
  readonly dismiss = output<string>();
  readonly createContent = output<string>();

  truncate(text: string, maxLength: number): string {
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
  }
}
