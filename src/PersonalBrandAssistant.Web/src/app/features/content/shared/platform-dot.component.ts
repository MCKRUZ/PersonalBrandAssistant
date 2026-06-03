import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Platform } from '../models/content.model';

const PLATFORM_VISUAL: Record<Platform, { color: string; code: string }> = {
  [Platform.Blog]: { color: '#c87156', code: 'Bl' },
  [Platform.Medium]: { color: '#1a8917', code: 'Me' },
  [Platform.Substack]: { color: '#ff6719', code: 'Su' },
  [Platform.LinkedIn]: { color: '#0a66c2', code: 'Li' },
  [Platform.Twitter]: { color: '#1d9bf0', code: 'Tw' },
  [Platform.Reddit]: { color: '#ff4500', code: 'Re' },
  [Platform.YouTube]: { color: '#ff0000', code: 'Yt' },
};

@Component({
  selector: 'app-platform-dot',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (variant() === 'tile') {
      <span class="tile" [style.color]="visual().color" [style.border-color]="visual().color">
        {{ visual().code }}
      </span>
    } @else {
      <span class="dot" [style.background]="visual().color" [title]="platform()"></span>
    }
  `,
  styles: [`
    .dot {
      display: inline-block;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .tile {
      display: inline-grid;
      place-items: center;
      width: 30px;
      height: 30px;
      border-radius: var(--r-control);
      border: 1px solid;
      font-family: var(--font-mono);
      font-size: 12px;
      font-weight: 600;
    }
  `],
})
export class PlatformDotComponent {
  readonly platform = input.required<Platform>();
  readonly variant = input<'dot' | 'tile'>('dot');

  readonly visual = computed(() => PLATFORM_VISUAL[this.platform()]);
}
