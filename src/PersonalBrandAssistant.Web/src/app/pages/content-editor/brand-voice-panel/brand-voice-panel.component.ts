import { Component, input, output } from '@angular/core';
import { Button } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';
import { ScoreGaugeComponent } from '../../../shared/components/score-gauge/score-gauge.component';
import { AxisBarComponent } from '../../../shared/components/axis-bar/axis-bar.component';
import { BrandVoiceScore } from '../../../core/models/brand-voice.model';

@Component({
  selector: 'app-brand-voice-panel',
  standalone: true,
  imports: [Button, Skeleton, ScoreGaugeComponent, AxisBarComponent],
  template: `
    <div class="brand-voice-panel">
      <h3 class="panel-title">Brand Voice</h3>

      @if (isScoring()) {
        <div class="scoring-skeleton">
          <p-skeleton shape="circle" size="100px" styleClass="mb-3" />
          @for (_ of [1,2,3,4]; track $index) {
            <p-skeleton height="24px" styleClass="mb-2" />
          }
        </div>
      } @else if (score()) {
        <div class="score-section">
          <app-score-gauge [score]="score()!.overallScore" [size]="100" />
        </div>

        <div class="axes-section">
          <app-axis-bar label="Authoritative" [value]="score()!.authoritative" />
          <app-axis-bar label="Pragmatic" [value]="score()!.pragmatic" />
          <app-axis-bar label="Concise" [value]="score()!.concise" />
          <app-axis-bar label="Practitioner" [value]="score()!.practitioner" />
        </div>

        @if (score()!.issues.length > 0) {
          <div class="issues-section">
            <h4 class="issues-title">Issues</h4>
            @for (issue of score()!.issues; track issue) {
              <div class="issue-item warning">
                <i class="pi pi-exclamation-triangle"></i>
                <span>{{ issue }}</span>
              </div>
            }
          </div>
        }

        @if (score()!.ruleViolations.length > 0) {
          <div class="issues-section">
            <h4 class="issues-title">Rule Violations</h4>
            @for (violation of score()!.ruleViolations; track violation) {
              <div class="issue-item danger">
                <i class="pi pi-times-circle"></i>
                <span>{{ violation }}</span>
              </div>
            }
          </div>
        }
      } @else {
        <div class="empty-score">
          <p>Click Score to evaluate brand voice alignment</p>
        </div>
      }

      <p-button
        label="Score"
        icon="pi pi-chart-bar"
        severity="secondary"
        [style]="{ width: '100%' }"
        (onClick)="scoreRequested.emit()"
        [loading]="isScoring()" />
    </div>
  `,
  styles: [`
    @use '../../../../styles/variables' as *;

    .brand-voice-panel {
      display: flex;
      flex-direction: column;
      gap: $space-3;
    }

    .panel-title {
      font-size: 0.875rem;
      font-weight: 600;
      color: $text-primary;
      margin: 0;
    }

    .score-section {
      display: flex;
      justify-content: center;
      padding: $space-3 0;
    }

    .axes-section {
      display: flex;
      flex-direction: column;
      gap: $space-2;
    }

    .scoring-skeleton {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: $space-3 0;
    }

    .issues-section {
      padding-top: $space-2;
    }

    .issues-title {
      font-size: 0.75rem;
      font-weight: 600;
      color: $text-secondary;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin: 0 0 $space-2;
    }

    .issue-item {
      display: flex;
      align-items: flex-start;
      gap: $space-2;
      font-size: 0.8125rem;
      padding: $space-1 0;

      i { font-size: 0.75rem; margin-top: 2px; }

      &.warning i { color: $score-warning; }
      &.danger i { color: $status-failed; }
    }

    .empty-score {
      text-align: center;
      padding: $space-4 0;
      color: $text-muted;
      font-size: 0.8125rem;

      p { margin: 0; }
    }
  `],
})
export class BrandVoicePanelComponent {
  readonly score = input<BrandVoiceScore | undefined>(undefined);
  readonly isScoring = input<boolean>(false);
  readonly scoreRequested = output<void>();
}
