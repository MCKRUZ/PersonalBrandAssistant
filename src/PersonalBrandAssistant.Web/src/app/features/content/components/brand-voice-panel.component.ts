import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Card } from 'primeng/card';
import { ProgressBar } from 'primeng/progressbar';
import { Tag } from 'primeng/tag';
import { BrandVoiceScore } from '../../../shared/models';

@Component({
  selector: 'app-brand-voice-panel',
  standalone: true,
  imports: [CommonModule, Card, ProgressBar, Tag],
  template: `
    @if (score()) {
      <p-card header="Brand Voice">
        <div class="grid">
          @for (metric of metrics(); track metric.label) {
            <div class="col-12">
              <div class="flex justify-content-between mb-1">
                <span class="font-semibold">{{ metric.label }}</span>
                <span>{{ metric.value }}%</span>
              </div>
              <p-progressbar [value]="metric.value" [showValue]="false" [style]="{ height: '8px' }" />
            </div>
          }
        </div>

        @if (score()!.issues.length > 0) {
          <h4 class="mt-3 mb-2">Issues</h4>
          <div class="flex flex-wrap gap-2">
            @for (issue of score()!.issues; track issue) {
              <p-tag [value]="issue" severity="warn" />
            }
          </div>
        }
      </p-card>
    }
  `,
})
export class BrandVoicePanelComponent {
  score = input<BrandVoiceScore>();

  metrics = input<{ label: string; value: number }[], BrandVoiceScore | undefined>(undefined, {
    transform: (s) => s ? [
      { label: 'Overall', value: Math.round(s.overallScore * 100) },
      { label: 'Tone', value: Math.round(s.toneScore * 100) },
      { label: 'Vocabulary', value: Math.round(s.vocabularyScore * 100) },
      { label: 'Persona', value: Math.round(s.personaScore * 100) },
    ] : [],
  });
}
