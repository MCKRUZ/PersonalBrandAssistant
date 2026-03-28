import { Component, input, computed, inject, ViewEncapsulation } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { PlatformChipComponent } from '../../../../shared/components/platform-chip/platform-chip.component';
import { VelocityIndicatorComponent } from './velocity-indicator.component';
import { TopicCluster, SOURCE_COLORS } from '../../models/news.model';

@Component({
  selector: 'app-topic-cluster-card',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [DecimalPipe, ButtonModule, BadgeModule, PlatformChipComponent, VelocityIndicatorComponent],
  template: `
    <article
      class="cluster-card"
      [class.featured]="featured()"
      [class.rising]="cluster().velocity === 'rising'"
      [style.--heat-intensity]="heatIntensity()"
    >
      <div class="cluster-card__header">
        <div>
          <h3 class="cluster-card__title">{{ cluster().topic }}</h3>
          <div class="cluster-card__meta">
            <app-velocity-indicator [velocity]="cluster().velocity" [itemCount]="cluster().itemCount" />
            <span class="cluster-card__count">{{ cluster().itemCount }} articles</span>
          </div>
        </div>
        <div class="cluster-card__heat-ring">
          {{ (cluster().relevanceScore * 100) | number:'1.0-0' }}
        </div>
      </div>

      <p class="cluster-card__rationale">{{ cluster().rationale }}</p>

      <div class="cluster-card__articles">
        @for (article of topArticles(); track article.id) {
          <a
            class="cluster-card__article"
            [href]="article.url" target="_blank" rel="noopener noreferrer"
            [style.--article-color]="getSourceColor(article.source)"
          >
            <span class="cluster-card__article-dot"></span>
            <span class="cluster-card__article-title">{{ article.title }}</span>
          </a>
        }
      </div>

      <div class="cluster-card__footer">
        <div class="cluster-card__platforms">
          @for (platform of cluster().suggestedPlatforms; track platform) {
            <app-platform-chip [platform]="platform" />
          }
        </div>
        <p-button icon="pi pi-pencil" label="Create Content" size="small" [rounded]="true" (onClick)="createContent()" />
      </div>
    </article>
  `,
  styles: `
    .cluster-card {
      --heat-intensity: 0;
      border-radius: 12px;
      padding: 1.25rem;
      background: var(--p-surface-800, #27272a);
      border: 1px solid var(--p-surface-700, #3f3f46);
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      height: 100%;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
      transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1),
                  box-shadow 0.25s cubic-bezier(0.4, 0, 0.2, 1),
                  border-color 0.25s ease;
    }
    .cluster-card:hover {
      transform: translateY(-3px);
      border-color: rgba(139, 92, 246, 0.3);
      box-shadow:
        0 12px 32px rgba(0, 0, 0, 0.35),
        0 0 0 1px rgba(139, 92, 246, 0.1);
    }
    .cluster-card.rising {
      border-left: 3px solid #8b5cf6;
      animation: pulse-glow 3s ease-in-out infinite;
    }
    .cluster-card.featured {
      border-color: rgba(139, 92, 246, 0.25);
      background: linear-gradient(
        135deg,
        var(--p-surface-800, #27272a) 0%,
        color-mix(in srgb, #8b5cf6 5%, var(--p-surface-800, #27272a)) 100%
      );
    }
    .cluster-card.featured .cluster-card__title {
      font-size: 1.35rem;
    }
    .cluster-card__header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 0.75rem;
    }
    .cluster-card__title {
      margin: 0;
      font-size: 1.05rem;
      font-weight: 800;
      letter-spacing: -0.02em;
      line-height: 1.3;
    }
    .cluster-card__meta {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-top: 0.3rem;
    }
    .cluster-card__count {
      font-size: 0.75rem;
      color: var(--p-text-muted-color);
      font-weight: 500;
    }
    .cluster-card__heat-ring {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.75rem;
      font-weight: 800;
      flex-shrink: 0;
      background: rgba(139, 92, 246, calc(var(--heat-intensity) * 0.5 + 0.1));
      color: white;
      border: 2px solid rgba(139, 92, 246, 0.4);
      box-shadow: 0 0 calc(var(--heat-intensity) * 16px) rgba(139, 92, 246, calc(var(--heat-intensity) * 0.4));
    }
    .cluster-card__rationale {
      font-size: 0.85rem;
      color: var(--p-text-muted-color);
      margin: 0;
      line-height: 1.5;
    }
    .cluster-card__articles {
      display: flex;
      flex-direction: column;
      gap: 0.3rem;
    }
    .cluster-card__article {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.4rem 0.6rem;
      border-radius: 6px;
      text-decoration: none;
      color: var(--p-text-color);
      font-size: 0.82rem;
      transition: background 0.15s ease;
    }
    .cluster-card__article:hover {
      background: var(--p-content-hover-background, rgba(255, 255, 255, 0.04));
    }
    .cluster-card__article-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--article-color);
      flex-shrink: 0;
    }
    .cluster-card__article-title {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .cluster-card__footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-top: auto;
    }
    .cluster-card__platforms {
      display: flex;
      gap: 0.3rem;
      flex-wrap: wrap;
    }

    @keyframes pulse-glow {
      0%, 100% { border-left-color: #8b5cf6; box-shadow: none; }
      50% { border-left-color: #a78bfa; box-shadow: -4px 0 16px rgba(139, 92, 246, 0.15); }
    }

    @media (prefers-reduced-motion: reduce) {
      .cluster-card.rising { animation: none; }
    }
  `,
})
export class TopicClusterCardComponent {
  cluster = input.required<TopicCluster>();
  featured = input(false);

  private readonly router = inject(Router);

  readonly topArticles = computed(() => this.cluster().articles.slice(0, 3));
  readonly heatIntensity = computed(() => Math.min(this.cluster().heat / 5, 1));

  getSourceColor(source: string): string {
    return SOURCE_COLORS[source] ?? '#6b7280';
  }

  createContent() {
    this.router.navigate(['/content/new'], {
      queryParams: { topic: this.cluster().topic, type: this.cluster().suggestedContentType },
    });
  }
}
