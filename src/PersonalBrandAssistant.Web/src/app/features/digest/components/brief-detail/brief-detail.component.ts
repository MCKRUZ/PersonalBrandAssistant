import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Digest } from '../../models/digest.model';
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';

@Component({
  selector: 'app-brief-detail',
  standalone: true,
  imports: [DatePipe, ScoreBadgeComponent],
  template: `
    @if (digest(); as d) {
      <article class="brief">
        <header class="brief-head">
          <span class="brief-date">{{ d.date | date: 'fullDate' }}</span>
          <h1>{{ d.title }}</h1>
          <p class="intro">{{ d.intro }}</p>
        </header>

        @if (hero(); as h) {
          <a class="hero" data-testid="brief-hero"
             [class.no-link]="!h.url" [href]="h.url || null" [target]="h.url ? '_blank' : null" rel="noopener">
            <div class="hero-top">
              <span class="rank">#1</span>
              <app-score-badge [score]="h.score" />
            </div>
            <h2>{{ h.title }}</h2>
            <p>{{ h.whyItMatters }}</p>
          </a>
        }

        <ol class="rest">
          @for (item of rest(); track item.ideaId) {
            <li class="brief-item" data-testid="brief-item">
              <span class="rank">#{{ item.rank }}</span>
              <app-score-badge [score]="item.score" />
              <div class="item-main">
                @if (item.url) {
                  <a [href]="item.url" target="_blank" rel="noopener" class="item-title">{{ item.title }}</a>
                } @else {
                  <span class="item-title">{{ item.title }}</span>
                }
                <p class="why">{{ item.whyItMatters }}</p>
              </div>
            </li>
          }
        </ol>
      </article>
    } @else {
      <div class="empty" data-testid="brief-empty">
        <i class="pi pi-inbox"></i>
        <p>No brief selected yet.</p>
      </div>
    }
  `,
  styles: [`
    .brief { padding: 24px 28px; max-width: 760px; }
    .brief-head { margin-bottom: 20px; }
    .brief-date { font-size: 12px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.04em; }
    .brief-head h1 { font-size: 26px; color: var(--text-primary); margin: 6px 0 8px; }
    .intro { color: var(--text-secondary); font-size: 15px; line-height: 1.5; }
    .hero { display: block; text-decoration: none; background: var(--surface-card);
      border: 1px solid var(--surface-border); border-left: 3px solid var(--brand-primary);
      border-radius: var(--r); padding: 18px; margin-bottom: 16px; transition: border-color 0.2s; }
    .hero:not(.no-link):hover { border-color: var(--brand-primary); }
    .hero-top { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .hero .rank { font-weight: 700; color: var(--brand-primary); }
    .hero h2 { font-size: 19px; color: var(--text-primary); margin: 0 0 8px; }
    .hero p { color: var(--text-secondary); margin: 0; line-height: 1.5; }
    .rest { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .brief-item { display: flex; gap: 12px; padding: 12px; border-radius: var(--r-control); align-items: flex-start; }
    .brief-item:hover { background: var(--surface-hover); }
    .brief-item .rank { color: var(--text-muted); font-weight: 600; min-width: 28px; }
    .item-main { flex: 1; min-width: 0; }
    .item-title { color: var(--text-primary); font-weight: 600; text-decoration: none; }
    a.item-title:hover { color: var(--brand-primary); }
    .why { color: var(--text-secondary); font-size: 13px; margin: 4px 0 0; line-height: 1.45; }
    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; height: 100%; color: var(--text-muted); }
    .empty i { font-size: 32px; }
  `],
})
export class BriefDetailComponent {
  readonly digest = input.required<Digest | null>();
  readonly hero = computed(() => this.digest()?.items.find((i) => i.rank === 1) ?? null);
  readonly rest = computed(() => (this.digest()?.items ?? []).filter((i) => i.rank !== 1));
}
