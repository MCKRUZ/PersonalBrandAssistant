import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { Router } from '@angular/router';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';
import { StageTrackerComponent } from '../stage-tracker/stage-tracker.component';
import { VoiceScoreRingComponent } from '../../shared/voice-score-ring.component';

@Component({
  selector: 'app-editor-top-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StageTrackerComponent, VoiceScoreRingComponent],
  template: `
    <header class="editor-top-bar">
      <button class="back-btn" data-testid="back-to-studio" (click)="goBack()">
        <span class="arrow">&larr;</span> Studio
      </button>

      <app-stage-tracker [status]="status()" />

      @if (contentType() || primaryPlatform()) {
        <span class="meta" data-testid="type-platform-meta">
          {{ contentType() }} &middot; {{ primaryPlatform() }}
        </span>
      }

      <span class="spacer"></span>

      <span class="save-indicator" data-testid="save-indicator">
        @if (isSaving()) { Saving... }
        @else if (isDirty()) { Unsaved }
        @else { Saved }
      </span>

      <app-voice-score-ring [score]="voiceScore()" [size]="32" />

      @if (!panelOpen()) {
        <button class="assistant-toggle" data-testid="assistant-toggle" (click)="togglePanel.emit()">
          &#10022; Assistant
        </button>
      }
    </header>
  `,
  styles: [`
    .editor-top-bar {
      height: 58px;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 0 16px;
      border-bottom: 1px solid var(--surface-border);
      flex-shrink: 0;
      background: var(--surface-card);
    }
    .back-btn {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      background: transparent;
      border: none;
      color: var(--text-secondary);
      font-size: 13px;
      cursor: pointer;
      padding: 4px 6px;
      border-radius: var(--r-control);
    }
    .back-btn:hover { color: var(--text-primary); background: var(--surface-elevated); }
    .back-btn .arrow { font-size: 15px; }
    .meta {
      font-size: 12.5px;
      color: var(--text-secondary);
      white-space: nowrap;
    }
    .spacer { flex: 1; }
    .save-indicator {
      font-family: var(--font-mono);
      font-size: 12px;
      color: var(--text-muted);
      white-space: nowrap;
    }
    .assistant-toggle {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      background: transparent;
      border: 1px solid var(--surface-border);
      color: var(--brand-primary);
      font-size: 12.5px;
      cursor: pointer;
      padding: 5px 11px;
      border-radius: var(--r-pill);
    }
    .assistant-toggle:hover { background: var(--accent-soft); border-color: var(--brand-primary); }
  `],
})
export class EditorTopBarComponent {
  readonly status = input.required<ContentStatus | null>();
  readonly contentType = input.required<ContentType | null>();
  readonly primaryPlatform = input.required<Platform | null>();
  readonly voiceScore = input.required<number | null>();
  readonly isSaving = input.required<boolean>();
  readonly isDirty = input.required<boolean>();
  readonly panelOpen = input.required<boolean>();

  readonly togglePanel = output<void>();

  private readonly router = inject(Router);

  goBack(): void {
    this.router.navigate(['/content']);
  }
}
