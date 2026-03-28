import { Component, input, computed, inject, ViewEncapsulation } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { KnobModule } from 'primeng/knob';
import { AccordionModule } from 'primeng/accordion';
import { FormsModule } from '@angular/forms';
import { PlatformChipComponent } from '../../../../shared/components/platform-chip/platform-chip.component';
import { ContentOpportunity, SOURCE_COLORS } from '../../models/news.model';

@Component({
  selector: 'app-opportunity-card',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [DecimalPipe, FormsModule, ButtonModule, Tag, KnobModule, AccordionModule, PlatformChipComponent],
  template: `
    <article class="opp-card">
      <div class="opp-card__body">
        <div class="opp-card__knob">
          <p-knob
            [(ngModel)]="scoreDisplay"
            [readonly]="true" [size]="68" [strokeWidth]="7"
            valueTemplate="{value}" [valueColor]="scoreColor()"
            [rangeColor]="'rgba(255,255,255,0.06)'"
          />
        </div>

        <div class="opp-card__content">
          <div class="opp-card__header">
            <h3>{{ opportunity().topic }}</h3>
            <p-tag
              [value]="opportunity().timeliness"
              [severity]="timelinessSeverity()"
              [rounded]="true"
            />
          </div>
          <p class="opp-card__rationale">{{ opportunity().rationale }}</p>
          <div class="opp-card__meta">
            <p-tag [value]="opportunity().suggestedContentType" severity="secondary" [rounded]="true" />
            @for (platform of opportunity().suggestedPlatforms; track platform) {
              <app-platform-chip [platform]="platform" />
            }
          </div>
        </div>

        <p-button icon="pi pi-pencil" label="Create" size="small" [rounded]="true" (onClick)="createContent()" />
      </div>

      @if (opportunity().articles.length > 0) {
        <div class="opp-card__expand">
          <p-accordion [multiple]="true">
            <p-accordionpanel>
              <p-accordionheader>Related Articles ({{ opportunity().articles.length }})</p-accordionheader>
              <p-accordioncontent>
                <div class="opp-card__articles">
                  @for (article of opportunity().articles; track article.id) {
                    <a
                      class="opp-card__article"
                      [href]="article.url" target="_blank" rel="noopener noreferrer"
                      [style.--article-color]="getSourceColor(article.source)"
                    >
                      <span class="opp-card__article-dot"></span>
                      {{ article.title }}
                    </a>
                  }
                </div>
              </p-accordioncontent>
            </p-accordionpanel>
          </p-accordion>
        </div>
      }
    </article>
  `,
  styles: `
    .opp-card {
      border-radius: 12px;
      background: var(--p-surface-800, #27272a);
      border: 1px solid var(--p-surface-700, #3f3f46);
      overflow: hidden;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
      transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1),
                  box-shadow 0.25s cubic-bezier(0.4, 0, 0.2, 1);
    }
    .opp-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35);
    }
    .opp-card__body {
      display: flex;
      align-items: center;
      gap: 1.25rem;
      padding: 1.25rem;
    }
    .opp-card__knob {
      flex-shrink: 0;
    }
    .opp-card__content {
      flex: 1;
      min-width: 0;
    }
    .opp-card__header {
      display: flex;
      align-items: center;
      gap: 0.6rem;
      margin-bottom: 0.3rem;
    }
    .opp-card__header h3 {
      margin: 0;
      font-size: 1.05rem;
      font-weight: 800;
      letter-spacing: -0.02em;
    }
    .opp-card__rationale {
      font-size: 0.85rem;
      color: var(--p-text-muted-color);
      margin: 0 0 0.5rem;
      line-height: 1.5;
    }
    .opp-card__meta {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-wrap: wrap;
    }
    .opp-card__expand {
      padding: 0 1.25rem 1.25rem;
    }
    .opp-card__articles {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .opp-card__article {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.35rem 0.5rem;
      border-radius: 6px;
      text-decoration: none;
      color: var(--p-text-color);
      font-size: 0.82rem;
      transition: background 0.15s;
    }
    .opp-card__article:hover {
      background: var(--p-content-hover-background, rgba(255, 255, 255, 0.04));
    }
    .opp-card__article-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--article-color);
      flex-shrink: 0;
    }

    @media (max-width: 640px) {
      .opp-card__body { flex-direction: column; align-items: flex-start; }
    }
  `,
})
export class OpportunityCardComponent {
  opportunity = input.required<ContentOpportunity>();
  private readonly router = inject(Router);
  scoreDisplay = 0;

  ngOnInit() {
    this.scoreDisplay = Math.round(this.opportunity().score * 100);
  }

  readonly scoreColor = computed(() => {
    const s = this.opportunity().score;
    return s >= 0.8 ? '#22c55e' : s >= 0.6 ? '#eab308' : '#f97316';
  });

  readonly timelinessSeverity = computed(() => {
    switch (this.opportunity().timeliness) {
      case 'Urgent': return 'danger' as const;
      case 'Timely': return 'warn' as const;
      case 'Evergreen': return 'success' as const;
    }
  });

  getSourceColor(source: string): string {
    return SOURCE_COLORS[source] ?? '#6b7280';
  }

  createContent() {
    this.router.navigate(['/content/new'], {
      queryParams: { topic: this.opportunity().topic, type: this.opportunity().suggestedContentType },
    });
  }
}
