import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { ContentService } from '../../services/content.service';
import { voiceBandColor } from '../../content-list/content-display.utils';

@Component({
  selector: 'app-voice-meter',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="voice-meter">
      <div class="vm-head">
        <span class="vm-label">Voice</span>
        <button
          class="vm-recheck"
          data-testid="recheck-btn"
          [disabled]="checking() || !contentId()"
          (click)="recheck()">
          {{ checking() ? 'Checking...' : 'Re-check' }}
        </button>
      </div>

      <div class="vm-value" data-testid="voice-value" [style.color]="bandColor()">
        {{ displayScore() === null ? '--' : displayScore() }}
      </div>

      <div class="vm-track">
        <div
          class="vm-fill"
          [style.width.%]="displayScore() ?? 0"
          [style.background]="bandColor()"></div>
      </div>

      <p class="vm-note" [class.vm-note--error]="checkError()" data-testid="band-note">{{ note() }}</p>
    </div>
  `,
  styles: [`
    .voice-meter {
      padding: 16px;
      border-bottom: 1px solid var(--surface-border);
      flex-shrink: 0;
    }
    .vm-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 8px;
    }
    .vm-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-muted);
    }
    .vm-recheck {
      background: transparent;
      border: 1px solid var(--surface-border);
      color: var(--text-secondary);
      font-size: 11px;
      padding: 3px 9px;
      border-radius: var(--r-pill);
      cursor: pointer;
    }
    .vm-recheck:hover:not(:disabled) { color: var(--text-primary); border-color: var(--brand-primary); }
    .vm-recheck:disabled { opacity: 0.5; cursor: not-allowed; }
    .vm-value {
      font-family: var(--font-mono);
      font-size: 38px;
      font-weight: 500;
      line-height: 1;
      margin-bottom: 10px;
    }
    .vm-track {
      height: 6px;
      border-radius: var(--r-pill);
      background: var(--surface-border);
      overflow: hidden;
    }
    .vm-fill {
      height: 100%;
      border-radius: var(--r-pill);
      transition: width 400ms ease;
    }
    .vm-note {
      margin: 10px 0 0;
      font-size: 12.5px;
      color: var(--text-secondary);
      line-height: 1.4;
    }
    .vm-note--error { color: var(--voice-low); }
  `],
})
export class VoiceMeterComponent {
  readonly contentId = input.required<string>();
  readonly voiceScore = input.required<number | null>();
  readonly feedback = input<string | null>(null);

  private readonly contentService = inject(ContentService);

  /** Internal display state, seeded from inputs, self-updated on re-check. */
  readonly displayScore = signal<number | null>(null);
  readonly displayFeedback = signal<string | null>(null);
  readonly checking = signal(false);
  readonly checkError = signal(false);

  constructor() {
    // Keep display in sync with external inputs until a re-check overrides them.
    effect(() => {
      this.displayScore.set(this.normalize(this.voiceScore()));
    });
    effect(() => {
      this.displayFeedback.set(this.feedback());
    });
  }

  readonly bandColor = computed(() => voiceBandColor(this.displayScore()));

  readonly note = computed(() => {
    if (this.checkError()) return "Couldn't check your voice. Try again.";
    const fb = this.displayFeedback();
    if (fb && fb.trim()) return fb;
    const s = this.displayScore();
    if (s === null) return "Doesn't sound like you yet.";
    if (s >= 80) return 'Sounds like you.';
    if (s >= 60) return 'Close - tighten the voice.';
    return "Doesn't sound like you yet.";
  });

  recheck(): void {
    const id = this.contentId();
    if (!id || this.checking()) return;
    this.checking.set(true);
    this.checkError.set(false);
    this.contentService.voiceCheck(id).subscribe({
      next: (result) => {
        this.displayScore.set(this.normalize(result.score));
        this.displayFeedback.set(result.feedback);
        this.checking.set(false);
      },
      error: () => { this.checking.set(false); this.checkError.set(true); },
    });
  }

  /** Backend may return 0-1 or 0-100; normalize to 0-100 for display + band logic. */
  private normalize(score: number | null): number | null {
    if (score === null) return null;
    return score > 0 && score <= 1 ? Math.round(score * 100) : Math.round(score);
  }
}
