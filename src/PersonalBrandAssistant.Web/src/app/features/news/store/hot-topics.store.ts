import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { NewsService } from '../services/news.service';
import { TopicCluster, NewsFeedItem, ideaToFeedItem } from '../models/news.model';

interface HotTopicsState {
  readonly items: readonly NewsFeedItem[];
  readonly loading: boolean;
}

const initialState: HotTopicsState = {
  items: [],
  loading: false,
};

function buildClusters(items: readonly NewsFeedItem[]): readonly TopicCluster[] {
  const grouped = new Map<string, NewsFeedItem[]>();
  for (const item of items) {
    const key = item.sourceCategory;
    const list = grouped.get(key);
    if (list) {
      list.push(item);
    } else {
      grouped.set(key, [item]);
    }
  }

  return Array.from(grouped, ([category, articles]) => {
    const sources = [...new Set(articles.map((a) => a.sourceName))];
    const newestDate = articles.reduce(
      (max, a) => Math.max(max, new Date(a.createdAt).getTime()),
      0
    );
    const ageHours = (Date.now() - newestDate) / 3_600_000;
    const velocity: 'rising' | 'stable' | 'falling' =
      ageHours < 6 ? 'rising' : ageHours < 24 ? 'stable' : 'falling';

    return {
      id: category,
      topic: category,
      rationale: `${articles.length} articles from ${sources.slice(0, 3).join(', ')}${sources.length > 3 ? ` +${sources.length - 3} more` : ''}`,
      heat: articles.length * (velocity === 'rising' ? 1.5 : velocity === 'stable' ? 1 : 0.5),
      velocity,
      itemCount: articles.length,
      suggestedContentType: 'Article' as const,
      suggestedPlatforms: [],
      articles,
      createdAt: new Date(newestDate).toISOString(),
    };
  });
}

export const HotTopicsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => {
    const clusters = computed(() => buildClusters(store.items()));
    return {
      clusters,
      sortedByHeat: computed(() =>
        [...clusters()].sort((a, b) => b.heat - a.heat)
      ),
      risingTopics: computed(() =>
        clusters().filter((c) => c.velocity === 'rising')
      ),
      featuredCluster: computed(() => {
        const sorted = [...clusters()].sort((a, b) => b.heat - a.heat);
        return sorted[0] ?? null;
      }),
    };
  }),
  withMethods((store, newsService = inject(NewsService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap(() =>
          newsService.getIdeas(500).pipe(
            tapResponse({
              next: (ideas) => patchState(store, { items: ideas.map(ideaToFeedItem), loading: false }),
              error: () => patchState(store, { loading: false }),
            })
          )
        )
      )
    ),
  }))
);
