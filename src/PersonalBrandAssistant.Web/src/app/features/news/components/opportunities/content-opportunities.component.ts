import { Component, inject, OnInit, computed } from '@angular/core';
import { SkeletonModule } from 'primeng/skeleton';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { NewsStore } from '../../store/news.store';
import { OpportunityCardComponent } from './opportunity-card.component';
import { ContentOpportunity, NewsFeedItem } from '../../models/news.model';

@Component({
  selector: 'app-content-opportunities',
  standalone: true,
  imports: [SkeletonModule, EmptyStateComponent, OpportunityCardComponent],
  template: `
    @if (store.loading()) {
      <div class="opportunities-skeleton">
        @for (i of skeletonItems; track i) {
          <p-skeleton height="140px" styleClass="mb-3" />
        }
      </div>
    } @else if (opportunities().length === 0) {
      <app-empty-state message="No content opportunities found" icon="pi pi-lightbulb" />
    } @else {
      <div class="opportunities-list">
        @for (opp of opportunities(); track opp.id) {
          <app-opportunity-card [opportunity]="opp" />
        }
      </div>
    }
  `,
  styles: `
    .opportunities-list {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
  `,
})
export class ContentOpportunitiesComponent implements OnInit {
  readonly store = inject(NewsStore);
  readonly skeletonItems = Array.from({ length: 4 }, (_, i) => i);

  readonly opportunities = computed<readonly ContentOpportunity[]>(() => {
    const suggestions = this.store.suggestions();

    return suggestions
      .filter((s) => s.relevanceScore >= 0.7)
      .map((s) => {
        const ageHours = (Date.now() - new Date(s.createdAt).getTime()) / 3_600_000;
        const timeliness: 'Urgent' | 'Timely' | 'Evergreen' =
          ageHours < 6 ? 'Urgent' : ageHours < 48 ? 'Timely' : 'Evergreen';

        const articles: NewsFeedItem[] = s.relatedTrends.map((item, idx) => ({
          id: `${s.id}-${idx}`,
          suggestionId: s.id,
          source: item.source,
          title: item.title,
          url: item.url,
          score: item.score,
          relevanceScore: s.relevanceScore,
          topic: s.topic,
          suggestedContentType: s.suggestedContentType,
          suggestedPlatforms: s.suggestedPlatforms,
          createdAt: s.createdAt,
          saved: false,
          trendItemId: item.trendItemId,
          summary: item.summary,
        }));

        return {
          id: s.id,
          topic: s.topic,
          rationale: s.rationale,
          score: s.relevanceScore,
          suggestedContentType: s.suggestedContentType,
          suggestedPlatforms: s.suggestedPlatforms,
          timeliness,
          articles,
          createdAt: s.createdAt,
        };
      })
      .sort((a, b) => b.score - a.score);
  });

  ngOnInit() {
    if (this.store.suggestions().length === 0) {
      this.store.load(undefined);
    }
  }
}
