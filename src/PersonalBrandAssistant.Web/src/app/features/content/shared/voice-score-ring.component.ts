import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { voiceBandColor } from '../content-list/content-display.utils';

/**
 * Circular voice-score indicator. Renders a conic ring filled to `score`% in the band color
 * with the numeric value centered, or a dashed-empty ring when score is null.
 */
@Component({
  selector: 'app-voice-score-ring',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (score() === null) {
      <span class="ring empty" [style.width.px]="size()" [style.height.px]="size()"></span>
    } @else {
      <span
        class="ring"
        [style.width.px]="size()"
        [style.height.px]="size()"
        [style.background]="gradient()"
      >
        <span class="inner" [style.color]="color()">{{ score() }}</span>
      </span>
    }
  `,
  styles: [`
    .ring {
      display: inline-grid;
      place-items: center;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .ring.empty {
      border: 1.5px dashed var(--surface-border);
    }
    .inner {
      display: grid;
      place-items: center;
      width: calc(100% - 5px);
      height: calc(100% - 5px);
      border-radius: 50%;
      background: var(--surface-card);
      font-family: var(--font-mono);
      font-size: 10.5px;
      font-weight: 500;
    }
  `],
})
export class VoiceScoreRingComponent {
  readonly score = input.required<number | null>();
  readonly size = input(40);

  readonly color = computed(() => voiceBandColor(this.score()));
  readonly gradient = computed(() => {
    const pct = Math.max(0, Math.min(100, this.score() ?? 0));
    return `conic-gradient(${this.color()} ${pct}%, var(--surface-border) ${pct}%)`;
  });
}
