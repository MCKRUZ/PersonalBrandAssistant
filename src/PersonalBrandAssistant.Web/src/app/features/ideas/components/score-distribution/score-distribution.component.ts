import { Component, computed, input } from '@angular/core';
import { Idea } from '../../../../models/idea.model';

@Component({
  selector: 'app-score-distribution',
  standalone: true,
  template: `
    <div class="dist">
      <div class="dist-head">Scores <span class="scope">(this page)</span></div>
      <div class="bands">
        <div class="band high" data-testid="band-high"><b>{{ counts().high }}</b><span>7-10</span></div>
        <div class="band mid" data-testid="band-mid"><b>{{ counts().mid }}</b><span>4-6</span></div>
        <div class="band low" data-testid="band-low"><b>{{ counts().low }}</b><span>0-3</span></div>
      </div>
    </div>
  `,
  styles: [`
    .dist { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 12px; }
    .dist-head { font-size: 12px; color: var(--text-secondary); margin-bottom: 8px; }
    .scope { color: var(--text-muted); }
    .bands { display: flex; gap: 8px; }
    .band { flex: 1; text-align: center; border-radius: var(--r-control); padding: 8px 4px; background: var(--surface-elevated); }
    .band b { display: block; font-size: 18px; }
    .band span { font-size: 11px; color: var(--text-secondary); }
    .band.high b { color: var(--score-success); }
    .band.mid b { color: var(--score-warning); }
    .band.low b { color: var(--score-danger); }
  `],
})
export class ScoreDistributionComponent {
  readonly ideas = input.required<Idea[]>();
  readonly counts = computed(() => {
    let high = 0, mid = 0, low = 0;
    for (const i of this.ideas()) {
      if (i.score === null) continue;
      if (i.score >= 7) high++; else if (i.score >= 4) mid++; else low++;
    }
    return { high, mid, low };
  });
}
