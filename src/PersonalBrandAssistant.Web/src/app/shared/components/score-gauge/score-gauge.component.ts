import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-score-gauge',
  standalone: true,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" [attr.viewBox]="viewBox()"
         role="img" [attr.aria-label]="'Score: ' + clampedScore() + ' out of 100'">
      <circle
        [attr.cx]="center()"
        [attr.cy]="center()"
        [attr.r]="radius()"
        fill="none"
        stroke="var(--p-surface-300)"
        [attr.stroke-width]="strokeWidth"
      />
      <circle
        class="gauge-arc"
        [attr.cx]="center()"
        [attr.cy]="center()"
        [attr.r]="radius()"
        fill="none"
        [attr.stroke]="color()"
        [attr.stroke-width]="strokeWidth"
        [attr.stroke-dasharray]="circumference()"
        [attr.stroke-dashoffset]="dashOffset()"
        stroke-linecap="round"
        [attr.transform]="rotation()"
      />
      <text
        [attr.x]="center()"
        [attr.y]="center()"
        text-anchor="middle"
        dominant-baseline="central"
        fill="var(--p-surface-900)"
        [attr.font-size]="fontSize()"
        font-family="'DM Serif Display', serif"
      >{{ clampedScore() }}</text>
    </svg>
  `,
  styles: `
    :host { display: inline-block; }
    .gauge-arc { transition: stroke-dashoffset 400ms ease; }
  `,
})
export class ScoreGaugeComponent {
  score = input.required<number>();
  size = input(120);

  readonly strokeWidth = 8;

  center = computed(() => this.size() / 2);
  radius = computed(() => (this.size() - this.strokeWidth) / 2);
  circumference = computed(() => 2 * Math.PI * this.radius());
  fontSize = computed(() => this.size() * 0.28);
  viewBox = computed(() => `0 0 ${this.size()} ${this.size()}`);
  rotation = computed(() => `rotate(-90 ${this.center()} ${this.center()})`);

  clampedScore = computed(() => Math.max(0, Math.min(100, Math.round(this.score()))));

  dashOffset = computed(() => {
    const pct = this.clampedScore() / 100;
    return this.circumference() * (1 - pct);
  });

  color = computed(() => {
    const s = this.clampedScore();
    if (s >= 80) return '#4ade80';
    if (s >= 60) return '#fbbf24';
    return '#f87171';
  });
}
