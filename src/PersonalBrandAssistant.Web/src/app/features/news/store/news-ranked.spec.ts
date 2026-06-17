import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { MessageService } from 'primeng/api';
import { NewsStore } from './news.store';
import { IdeaStatus } from '../../../models/idea.model';
import type { Idea } from '../../../models/idea.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('NewsStore ranked view', () => {
  let store: InstanceType<typeof NewsStore>;
  let httpMock: HttpTestingController;

  const makeIdea = (id: string, rank: number, detectedAt: string): Idea => ({
    id,
    title: `Story ${id}`,
    sourceName: 'TestSource',
    category: 'AI/ML',
    summary: 'desc',
    thumbnailUrl: null,
    status: IdeaStatus.New,
    tags: [],
    detectedAt,
    hasSavedDetails: false,
    description: null,
    url: `https://example.com/${id}`,
    score: Math.round(rank * 10),
    scoreReason: null,
    isDuplicate: false,
    rank,
    brandFit: rank,
    pillarBreakdown: [],
    isAntiTopic: null,
    isAuthorityTopic: null,
    recencyFactor: 0,
    stale: false,
  });

  const flushIdeasLoad = (ideas: Idea[]) => {
    const page: PagedResult<Idea> = {
      items: ideas, totalCount: ideas.length, page: 1, pageSize: 5000, totalPages: 1,
    };
    httpMock.expectOne(r => r.url.includes('/api/ideas')).flush(page);
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        NewsStore,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MessageService, useValue: jasmine.createSpyObj('MessageService', ['add']) },
      ],
    });
    store = TestBed.inject(NewsStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('rankedItems orders the filtered feed by rank descending', () => {
    const now = Date.now();
    store.load(undefined);
    flushIdeasLoad([
      makeIdea('low', 0.2, new Date(now).toISOString()),
      makeIdea('high', 0.9, new Date(now).toISOString()),
      makeIdea('mid', 0.5, new Date(now).toISOString()),
    ]);

    expect(store.rankedItems().map(i => i.id)).toEqual(['high', 'mid', 'low']);
  });

  it('breaks rank ties by recency (newer first)', () => {
    const now = Date.now();
    store.load(undefined);
    flushIdeasLoad([
      makeIdea('older', 0.5, new Date(now - 60_000).toISOString()),
      makeIdea('newer', 0.5, new Date(now).toISOString()),
    ]);

    expect(store.rankedItems().map(i => i.id)).toEqual(['newer', 'older']);
  });

  it('rankedItems respects the active category filter', () => {
    const now = new Date().toISOString();
    store.load(undefined);
    const aiml = makeIdea('aiml', 0.9, now);
    const sec = { ...makeIdea('sec', 0.95, now), category: 'Security' };
    flushIdeasLoad([aiml, sec]);

    store.updateFilters({ categories: ['AI/ML'] });

    expect(store.rankedItems().map(i => i.id)).toEqual(['aiml']);
  });

  it('setRankedView toggles the view flag', () => {
    expect(store.rankedView()).toBe(false);
    store.setRankedView(true);
    expect(store.rankedView()).toBe(true);
    store.setRankedView(false);
    expect(store.rankedView()).toBe(false);
  });
});
