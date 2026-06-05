import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { MessageService } from 'primeng/api';
import { NewsStore } from './news.store';
import { IdeaStatus } from '../../../models/idea.model';
import type { Idea } from '../../../models/idea.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('NewsStore dismiss', () => {
  let store: InstanceType<typeof NewsStore>;
  let httpMock: HttpTestingController;

  const makeIdea = (id: string, title: string): Idea => ({
    id,
    title,
    sourceName: 'TestSource',
    category: 'AI/ML',
    summary: 'desc',
    thumbnailUrl: null,
    status: IdeaStatus.New,
    tags: [],
    detectedAt: new Date().toISOString(),
    hasSavedDetails: false,
    description: null,
    url: `https://example.com/${id}`,
    score: null,
    scoreReason: null,
    isDuplicate: false,
  });

  const flushIdeasLoad = (ideas: Idea[]) => {
    const page: PagedResult<Idea> = {
      items: ideas,
      totalCount: ideas.length,
      page: 1,
      pageSize: 5000,
      totalPages: 1,
    };
    const req = httpMock.expectOne(r => r.url.includes('/api/ideas'));
    req.flush(page);
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

  afterEach(() => {
    httpMock.verify();
  });

  it('should have 3 items after load', () => {
    const ideas = [
      makeIdea('aaa', 'First Story'),
      makeIdea('bbb', 'Second Story'),
      makeIdea('ccc', 'Third Story'),
    ];

    store.load(undefined);
    flushIdeasLoad(ideas);

    expect(store.items().length).toBe(3);
    expect(store.filteredItems().length).toBe(3);
    expect(store.groupedByCategory().length).toBeGreaterThan(0);
  });

  it('should remove the dismissed item from state immediately', fakeAsync(() => {
    const ideas = [
      makeIdea('aaa', 'First Story'),
      makeIdea('bbb', 'Second Story'),
      makeIdea('ccc', 'Third Story'),
    ];

    store.load(undefined);
    flushIdeasLoad(ideas);
    tick();

    expect(store.items().length).toBe(3);

    store.dismiss('aaa');
    tick();

    expect(store.items().length).toBe(2);
    expect(store.items().find(i => i.id === 'aaa')).toBeUndefined();
    expect(store.filteredItems().length).toBe(2);

    const dismissReq = httpMock.expectOne(r => r.url.includes('/api/ideas/aaa/dismiss'));
    expect(dismissReq.request.method).toBe('PUT');
    dismissReq.flush(null);
    tick();

    expect(store.items().length).toBe(2);
  }));

  it('should rollback on API error', fakeAsync(() => {
    const ideas = [
      makeIdea('aaa', 'First Story'),
      makeIdea('bbb', 'Second Story'),
    ];

    store.load(undefined);
    flushIdeasLoad(ideas);
    tick();

    store.dismiss('aaa');
    tick();

    expect(store.items().length).toBe(1);

    const dismissReq = httpMock.expectOne(r => r.url.includes('/api/ideas/aaa/dismiss'));
    dismissReq.flush('error', { status: 500, statusText: 'Server Error' });
    tick();

    expect(store.items().length).toBe(2);
  }));

  it('should update groupedByCategory after dismiss', fakeAsync(() => {
    const ideas = [
      makeIdea('aaa', 'First Story'),
      makeIdea('bbb', 'Second Story'),
      makeIdea('ccc', 'Third Story'),
    ];

    store.load(undefined);
    flushIdeasLoad(ideas);
    tick();

    const itemsBefore = store.groupedByCategory().flatMap(g => g.items);
    expect(itemsBefore.length).toBe(3);

    store.dismiss('aaa');
    tick();

    const itemsAfter = store.groupedByCategory().flatMap(g => g.items);
    expect(itemsAfter.length).toBe(2);
    expect(itemsAfter.find(i => i.title === 'First Story')).toBeUndefined();
    expect(itemsAfter[0].title).toBe('Second Story');

    httpMock.expectOne(r => r.url.includes('/api/ideas/aaa/dismiss')).flush(null);
    tick();
  }));
});
