import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { TrendSuggestion } from '../../../shared/models';
import { NewsService } from '../services/news.service';
import { TopicCluster, NewsFeedItem } from '../models/news.model';

interface HotTopicsState {
  readonly suggestions: readonly TrendSuggestion[];
  readonly loading: boolean;
}

const initialState: HotTopicsState = {
  suggestions: [],
  loading: false,
};

function buildClusters(suggestions: readonly TrendSuggestion[]): readonly TopicCluster[] {
  return suggestions.map((s) => {
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

    const heat = s.relatedTrends.length * s.relevanceScore;
    const ageHours = (Date.now() - new Date(s.createdAt).getTime()) / 3_600_000;
    const velocity: 'rising' | 'stable' | 'falling' =
      ageHours < 6 ? 'rising' : ageHours < 24 ? 'stable' : 'falling';

    return {
      id: s.id,
      topic: s.topic,
      rationale: s.rationale,
      relevanceScore: s.relevanceScore,
      heat,
      velocity,
      itemCount: s.relatedTrends.length,
      suggestedContentType: s.suggestedContentType,
      suggestedPlatforms: s.suggestedPlatforms,
      articles,
      createdAt: s.createdAt,
    };
  });
}

export const HotTopicsStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => {
    const clusters = computed(() => buildClusters(store.suggestions()));
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
          newsService.getSuggestions(100).pipe(
            tapResponse({
              next: (suggestions) => patchState(store, { suggestions, loading: false }),
              error: () => patchState(store, { loading: false }),
            })
          )
        )
      )
    ),
  }))
);
