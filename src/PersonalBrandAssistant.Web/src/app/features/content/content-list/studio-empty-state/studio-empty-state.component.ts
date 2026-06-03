import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ContentType } from '../../models/content.model';

export interface IdeaSuggestion {
  title: string;
  blurb: string;
  topic: string;
  type: ContentType;
}

/**
 * Two-variant empty state. `inspire` (no content at all) shows idea-suggestion cards that seed
 * /content/new with topic/type query params. `filtered` (content exists but nothing matches the
 * active filter) offers a "Clear filters" reset emitted to the orchestrator.
 */
@Component({
  selector: 'app-studio-empty-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ButtonModule],
  template: `
    @if (variant() === 'inspire') {
      <div class="empty inspire" data-testid="empty-inspire">
        <span class="mark">✎</span>
        <h2 class="title">Your studio is quiet.</h2>
        <p class="sub">
          Nothing in the pipeline yet. Start from one of these prompts or strike out on your own.
        </p>

        <div class="ideas">
          @for (idea of suggestions(); track idea.title) {
            <a
              class="idea-card"
              [routerLink]="['/content/new']"
              [queryParams]="{ topic: idea.topic, type: idea.type }"
              data-testid="idea-card">
              <span class="idea-title">{{ idea.title }}</span>
              <span class="idea-blurb">{{ idea.blurb }}</span>
            </a>
          }
        </div>

        <div class="divider"><span>or</span></div>

        <a class="scratch" [routerLink]="['/content/new']" data-testid="start-scratch">
          + Start from scratch
        </a>
      </div>
    } @else {
      <div class="empty filtered" data-testid="empty-filtered">
        <span class="mark search">⌕</span>
        <h2 class="title small">Nothing matches that filter</h2>
        <p class="sub">Loosen the filters to see more of your pipeline.</p>
        <p-button
          label="Clear filters"
          severity="secondary"
          [text]="true"
          (onClick)="clearFilters.emit()"
          data-testid="clear-filters" />
      </div>
    }
  `,
  styles: [`
    .empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      padding: 56px 28px;
    }
    .mark {
      display: grid;
      place-items: center;
      width: 64px;
      height: 64px;
      border-radius: 18px;
      font-size: 28px;
      color: var(--brand-primary);
      background: var(--accent-soft);
      border: 1px solid var(--brand-primary);
      margin-bottom: 22px;
    }
    .mark.search {
      font-size: 30px;
    }
    .title {
      font-family: var(--font-display);
      font-size: 34px;
      font-weight: 400;
      color: var(--text-primary);
      margin: 0 0 12px;
    }
    .title.small {
      font-size: 24px;
    }
    .sub {
      font-size: 15px;
      line-height: 1.55;
      color: var(--text-secondary);
      max-width: 520px;
      margin: 0 0 26px;
    }
    .ideas {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 14px;
      width: 100%;
      max-width: 560px;
    }
    .idea-card {
      display: flex;
      flex-direction: column;
      gap: 6px;
      text-align: left;
      padding: 16px;
      border-radius: var(--r-inner);
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      text-decoration: none;
      transition: border-color 0.14s, box-shadow 0.14s;
    }
    .idea-card:hover {
      border-color: var(--brand-primary);
      box-shadow: 0 6px 20px -10px rgba(0, 0, 0, 0.6);
    }
    .idea-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--text-primary);
    }
    .idea-blurb {
      font-size: 12.5px;
      line-height: 1.5;
      color: var(--text-secondary);
    }
    .divider {
      display: flex;
      align-items: center;
      width: 100%;
      max-width: 560px;
      margin: 24px 0 18px;
      color: var(--text-muted);
      font-size: 12px;
    }
    .divider::before,
    .divider::after {
      content: '';
      flex: 1;
      height: 1px;
      background: var(--surface-border);
    }
    .divider span {
      padding: 0 12px;
    }
    .scratch {
      font-size: 14px;
      font-weight: 500;
      color: var(--brand-primary);
      text-decoration: none;
    }
    .scratch:hover {
      color: var(--brand-primary-hover);
    }
  `],
})
export class StudioEmptyStateComponent {
  readonly variant = input.required<'inspire' | 'filtered'>();
  readonly suggestions = input<IdeaSuggestion[]>([]);
  readonly clearFilters = output<void>();
}
