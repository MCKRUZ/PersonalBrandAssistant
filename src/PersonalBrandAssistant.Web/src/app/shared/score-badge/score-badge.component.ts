import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-score-badge',
  standalone: true,
  template: `
    @if (score() !== null) {
      <span [class]="'score-badge ' + band()" [title]="title()">{{ score() }}/10</span>
    }
  `,
  styles: [`
    .score-badge {
      font-size: 11px;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: var(--r-pill);
      cursor: default;
      white-space: nowrap;
    }
    .band-success { background: color-mix(in srgb, var(--score-success) 18%, transparent); color: var(--score-success); }
    .band-warning { background: color-mix(in srgb, var(--score-warning) 18%, transparent); color: var(--score-warning); }
    .band-danger  { background: color-mix(in srgb, var(--score-danger) 18%, transparent);  color: var(--score-danger); }
  `],
})
export class ScoreBadgeComponent {
  readonly score = input.required<number | null>();
  readonly title = input<string>('');
  readonly band = computed(() => {
    const s = this.score();
    if (s === null) return '';
    if (s >= 7) return 'band-success';
    if (s >= 4) return 'band-warning';
    return 'band-danger';
  });
}
