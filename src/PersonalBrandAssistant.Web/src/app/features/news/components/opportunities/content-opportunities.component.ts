import { Component, inject, OnInit, computed } from '@angular/core';
import { SkeletonModule } from 'primeng/skeleton';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { NewsStore } from '../../store/news.store';
import { OpportunityCardComponent } from './opportunity-card.component';
import { ContentOpportunity, NewsFeedItem, CATEGORY_ORDER } from '../../models/news.model';

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
    const items = this.store.allItems();
    const grouped = new Map<string, NewsFeedItem[]>();
    for (const item of items) {
      const cat = item.sourceCategory;
      const list = grouped.get(cat);
      if (list) {
        list.push(item);
      } else {
        grouped.set(cat, [item]);
      }
    }

    const results: ContentOpportunity[] = [];
    for (const [category, articles] of grouped) {
      if (articles.length < 2) continue;

      const newestDate = articles.reduce(
        (max, a) => Math.max(max, new Date(a.createdAt).getTime()),
        0
      );
      const ageHours = (Date.now() - newestDate) / 3_600_000;
      const timeliness: 'Urgent' | 'Timely' | 'Evergreen' =
        ageHours < 6 ? 'Urgent' : ageHours < 48 ? 'Timely' : 'Evergreen';

      const sources = [...new Set(articles.map((a) => a.sourceName))];
      const rawScore = Math.min(1, articles.length / 10) * (timeliness === 'Urgent' ? 1.2 : timeliness === 'Timely' ? 1 : 0.7);

      results.push({
        id: category,
        topic: category,
        rationale: `${articles.length} articles from ${sources.slice(0, 3).join(', ')}`,
        score: Math.min(1, rawScore),
        suggestedContentType: 'Article',
        suggestedPlatforms: [],
        timeliness,
        articles: articles.slice(0, 5),
        createdAt: new Date(newestDate).toISOString(),
      });
    }
    return results.sort((a, b) => b.score - a.score);
  });

  ngOnInit() {
    if (this.store.allItems().length === 0) {
      this.store.load(undefined);
    }
  }
}
