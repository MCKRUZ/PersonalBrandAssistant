import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { IdeaStore } from '../../store/idea.store';
import { Idea } from '../../../../models/idea.model';
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';

@Component({
  selector: 'app-idea-ranked',
  standalone: true,
  imports: [ButtonModule, ScoreBadgeComponent],
  template: `
    <div class="idea-ranked">
      <div class="window-toggle" role="group" aria-label="Ranking window">
        <p-button label="Today" size="small"
          [severity]="store.rankedWindow() === 'today' ? 'primary' : 'secondary'"
          [text]="store.rankedWindow() !== 'today'"
          (onClick)="store.setRankedWindow('today')" data-testid="window-today" />
        <p-button label="This week" size="small"
          [severity]="store.rankedWindow() === 'week' ? 'primary' : 'secondary'"
          [text]="store.rankedWindow() !== 'week'"
          (onClick)="store.setRankedWindow('week')" data-testid="window-week" />
      </div>

      @if (ideas.length === 0) {
        <div class="empty" data-testid="ranked-empty">No ranked ideas in this window yet.</div>
      }

      <ol class="ranked-list">
        @for (idea of ideas.slice(0, store.rankedTopN()); track idea.id; let i = $index) {
          <li class="ranked-item" data-testid="ranked-item">
            <div class="rank-numeral" data-testid="rank-numeral">{{ i + 1 }}</div>
            <div class="ranked-body">
              <div class="ranked-head">
                <h3 class="ranked-title">{{ idea.title }}</h3>
                <app-score-badge [score]="idea.score" />
                @if (idea.isAuthorityTopic) {
                  <span class="badge authority" data-testid="authority-badge">Authority</span>
                }
                @if (idea.isAntiTopic) {
                  <span class="badge anti" data-testid="anti-badge">Anti-topic</span>
                }
                @if (idea.stale) {
                  <span class="badge stale" data-testid="stale-badge">Stale</span>
                }
              </div>
              <div class="ranked-meta">{{ idea.sourceName }}</div>

              @if (idea.pillarBreakdown.length > 0) {
                <ul class="breakdown" data-testid="pillar-breakdown">
                  @for (pillar of idea.pillarBreakdown; track pillar.name) {
                    <li class="breakdown-row">
                      <span class="pillar-name">{{ pillar.name }}</span>
                      <span class="pillar-score">{{ pct(pillar.score) }}%</span>
                      <span class="pillar-reason">{{ pillar.reason }}</span>
                    </li>
                  }
                </ul>
              }

              <div class="ranked-actions">
                <p-button label="Save" size="small" [text]="true" (onClick)="save.emit(idea.id)" />
                <p-button label="Dismiss" size="small" [text]="true" severity="secondary"
                  (onClick)="dismiss.emit(idea.id)" />
                <p-button label="Create content" size="small" [text]="true"
                  (onClick)="createContent.emit(idea.id)" />
              </div>
            </div>
          </li>
        }
      </ol>
    </div>
  `,
  styles: [
    `
      .window-toggle { display: flex; gap: 4px; margin-bottom: 16px; }
      .ranked-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 12px; }
      .ranked-item { display: flex; gap: 16px; align-items: flex-start;
        padding: 12px; border: 1px solid var(--surface-border); border-radius: var(--r-card, 8px); }
      .rank-numeral { font-size: 32px; font-weight: 700; line-height: 1; color: var(--brand-primary);
        min-width: 48px; text-align: center; }
      .ranked-body { flex: 1; min-width: 0; }
      .ranked-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
      .ranked-title { font-size: 15px; font-weight: 600; margin: 0; color: var(--text-primary); }
      .ranked-meta { font-size: 12px; color: var(--text-secondary); margin: 2px 0 8px; }
      .badge { font-size: 10px; font-weight: 700; padding: 2px 6px; border-radius: var(--r-pill, 999px); }
      .badge.authority { background: color-mix(in srgb, var(--score-success) 18%, transparent); color: var(--score-success); }
      .badge.anti { background: color-mix(in srgb, var(--score-danger) 18%, transparent); color: var(--score-danger); }
      .badge.stale { background: color-mix(in srgb, var(--score-warning) 18%, transparent); color: var(--score-warning); }
      .breakdown { list-style: none; margin: 0 0 8px; padding: 0; display: flex; flex-direction: column; gap: 2px; }
      .breakdown-row { display: flex; gap: 8px; font-size: 12px; }
      .pillar-name { font-weight: 600; color: var(--text-primary); }
      .pillar-score { color: var(--brand-primary); }
      .pillar-reason { color: var(--text-secondary); }
      .ranked-actions { display: flex; gap: 4px; }
      .empty { padding: 32px; text-align: center; color: var(--text-secondary); }
    `,
  ],
})
export class IdeaRankedComponent {
  readonly store = inject(IdeaStore);
  @Input({ required: true }) ideas: Idea[] = [];
  @Output() save = new EventEmitter<string>();
  @Output() dismiss = new EventEmitter<string>();
  @Output() createContent = new EventEmitter<string>();

  pct(score: number): number {
    return Math.round(score * 100);
  }
}
